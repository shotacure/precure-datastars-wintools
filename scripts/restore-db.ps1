<#
.SYNOPSIS
  backup-db.ps1 のダンプを復元する。既定では検証用の別スキーマに復元して本番と突き合わせる（復元の訓練）。

.DESCRIPTION
  指定したダンプ（.sql.gz または .sql。省略時は保存先にある最新の .sql.gz）を展開し、
  既定では検証用スキーマ（precure_datastars_restore_test）に流し込んで、本番（-CompareWith、既定 precure_datastars）
  とテーブルごとの行数と CHECKSUM TABLE を突き合わせ、結果を表で出す。終わったら検証用スキーマは消す
  （-KeepTestDatabase で残す）。検証用の流し込みは binlog に残さない（sql_log_bin=0）。

  復元できたことのないバックアップはバックアップではないので、仕組みを入れたときと、月に 1 回はこれを回す。
  本番がダンプ以後に変わっていれば、その分は「不一致」になる（取った直後なら全テーブル一致する）。

  -ToProduction を付けると本番スキーマそのものに復元する。実行前に backup-db.ps1 -Label before-restore で
  直前の状態を退避し、スキーマ名の入力で確認してから DROP → CREATE → 流し込みを行う。
  ダンプ以後の操作を binlog から足すには mysqlbinlog を使う（README「データベースのバックアップと復元」）。

  接続は Catalog の App.config（PrecureDataStars.Catalog/App.config）の DatastarsMySql 接続文字列（root）を読んで
  使う。パスワードは一時的なオプションファイルに書き、終了時に消す。

.PARAMETER DumpFile
  復元するダンプ（.sql.gz または .sql）。省略時は LocalDir にある最新の .sql.gz。

.PARAMETER LocalDir
  バックアップの保存先。既定 D:\backup\precure-datastars。

.PARAMETER TargetDatabase
  検証用スキーマ名。既定 precure_datastars_restore_test。-ToProduction のときは使わない。

.PARAMETER CompareWith
  突き合わせる本番スキーマ。既定 precure_datastars。-ToProduction のときは復元先になる。

.PARAMETER ToProduction
  本番スキーマそのものに復元する。

.PARAMETER KeepTestDatabase
  検証用スキーマを消さずに残す。

.PARAMETER Yes
  本番復元の確認入力を省く（非対話実行用）。

.PARAMETER SkipSafetyBackup
  本番復元の前の退避（backup-db.ps1 -Label before-restore）を省く。

.PARAMETER AppConfigPath
  接続文字列を読む App.config。既定 PrecureDataStars.Catalog\App.config。

.PARAMETER MysqlPath
  mysql.exe の場所。既定 C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe。

.EXAMPLE
  .\scripts\restore-db.ps1
  最新のダンプを検証用スキーマに復元し、本番と突き合わせる。

.EXAMPLE
  .\scripts\restore-db.ps1 -DumpFile D:\backup\precure-datastars\precure_datastars_20261004-0400.sql.gz -ToProduction
  指定のダンプで本番を置き換える。
#>
[CmdletBinding()]
param(
    [string]$DumpFile,
    [string]$LocalDir = 'D:\backup\precure-datastars',
    [string]$TargetDatabase = 'precure_datastars_restore_test',
    [string]$CompareWith = 'precure_datastars',
    [switch]$ToProduction,
    [switch]$KeepTestDatabase,
    [switch]$Yes,
    [switch]$SkipSafetyBackup,
    [string]$AppConfigPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'PrecureDataStars.Catalog\App.config'),
    [string]$MysqlPath = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.IO.Compression

# ---------------------------------------------------------------------------------------------------
# 補助関数
# ---------------------------------------------------------------------------------------------------

# 外部コマンドを引数の配列で起動し、終了コードと stdout / stderr を返す（backup-db.ps1 と同じ）。
function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    [pscustomobject]@{ ExitCode = $p.ExitCode; StdOut = $outTask.Result; StdErr = $errTask.Result }
}

# App.config の DatastarsMySql 接続文字列（MySqlConnector 形式）から接続先と root の資格情報を読む。
function Read-ConnectionString {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "App.config がありません: $Path" }
    $xml = [xml](Get-Content -LiteralPath $Path -Raw -Encoding utf8)
    $node = $xml.configuration.connectionStrings.add | Where-Object { $_.name -eq 'DatastarsMySql' } | Select-Object -First 1
    if (-not $node) { throw "App.config に DatastarsMySql の接続文字列がありません: $Path" }
    $map = @{}
    foreach ($part in ($node.connectionString -split ';')) {
        if ($part -notmatch '=') { continue }
        $k, $v = $part -split '=', 2
        $map[$k.Trim().ToLowerInvariant()] = $v.Trim()
    }
    $user = if ($map['user id']) { $map['user id'] } elseif ($map['uid']) { $map['uid'] } else { 'root' }
    $pass = if ($map.ContainsKey('password')) { $map['password'] } else { $map['pwd'] }
    [pscustomobject]@{
        Host     = if ($map['server']) { $map['server'] } else { 'localhost' }
        Port     = if ($map['port']) { $map['port'] } else { '3306' }
        User     = $user
        Password = $pass
    }
}

# MySQL のオプションファイルに書く値（引用符で囲み、\ と " をエスケープ）。
function ConvertTo-CnfValue {
    param([string]$Value)
    return '"' + (($Value -replace '\\', '\\') -replace '"', '\"') + '"'
}

# gzip 展開。
function Expand-GZipFile {
    param([string]$Source, [string]$Target)
    $in  = [System.IO.File]::OpenRead($Source)
    $gz  = [System.IO.Compression.GZipStream]::new($in, [System.IO.Compression.CompressionMode]::Decompress)
    $out = [System.IO.File]::Create($Target)
    try { $gz.CopyTo($out) }
    finally { $out.Dispose(); $gz.Dispose(); $in.Dispose() }
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} KB' -f [Math]::Ceiling($Bytes / 1KB))
}

# mysql クライアントで SQL（またはクライアントコマンド）を実行し、stdout を返す。
function Invoke-Mysql {
    param([string]$Sql, [string[]]$ExtraArgs = @(), [string]$DatabaseArg = '')
    $cmdArgs = @("--defaults-extra-file=$script:cnfTemp", '--default-character-set=utf8mb4') + $ExtraArgs
    if ($DatabaseArg) { $cmdArgs += $DatabaseArg }
    $cmdArgs += @('-e', $Sql)
    $r = Invoke-Native -Exe $MysqlPath -Arguments $cmdArgs
    $err = ($r.StdErr -split "`r?`n" | Where-Object { $_ }) -join ' / '
    if ($r.ExitCode -ne 0) { throw "mysql が失敗しました (exit $($r.ExitCode)): $err" }
    if ($err) { Write-Warning "mysql: $err" }
    return $r.StdOut
}

# -B -N（タブ区切り・見出し無し）で実行し、行ごとに列の配列を返す。
function Invoke-MysqlRows {
    param([string]$Sql)
    $out = Invoke-Mysql -Sql $Sql -ExtraArgs @('-B', '-N')
    $rows = @($out -split "`r?`n" | Where-Object { $_ } | ForEach-Object { , ($_ -split "`t") })
    # 関数の戻り値は 1 段展開されるので、行が 1 つだけのときに「行の配列」が「列の配列」に崩れないよう包んで返す。
    return , $rows
}

# スキーマ内の BASE TABLE ごとの行数を返す。
function Get-TableCounts {
    param([string]$Db, [string[]]$Names)
    $result = @{}
    if ($Names.Count -eq 0) { return $result }
    $sql = ($Names | ForEach-Object { "SELECT '$_' AS t, COUNT(*) AS c FROM ``$Db``.``$_``" }) -join ' UNION ALL '
    foreach ($row in (Invoke-MysqlRows -Sql $sql)) { $result[$row[0]] = [long]$row[1] }
    return $result
}

# スキーマ内のテーブルごとの CHECKSUM TABLE の値を返す。
function Get-TableChecksums {
    param([string]$Db, [string[]]$Names)
    $result = @{}
    if ($Names.Count -eq 0) { return $result }
    $sql = 'CHECKSUM TABLE ' + (($Names | ForEach-Object { "``$Db``.``$_``" }) -join ', ')
    foreach ($row in (Invoke-MysqlRows -Sql $sql)) { $result[($row[0] -split '\.', 2)[1]] = $row[1] }
    return $result
}

# ---------------------------------------------------------------------------------------------------
# 本体
# ---------------------------------------------------------------------------------------------------

$script:cnfTemp = $null
$sqlTemp = $null
$createdTest = $false
$exitCode = 0

try {
    if (-not (Test-Path -LiteralPath $MysqlPath)) { throw "mysql が見つかりません: $MysqlPath" }

    # 復元するダンプを決める
    if (-not $DumpFile) {
        $latest = Get-ChildItem -LiteralPath $LocalDir -File -Filter '*.sql.gz' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $latest) { throw "ダンプがありません: $LocalDir" }
        $DumpFile = $latest.FullName
    }
    $DumpFile = (Resolve-Path -LiteralPath $DumpFile).Path

    if ($ToProduction) { $TargetDatabase = $CompareWith }
    elseif ($TargetDatabase -eq $CompareWith) { throw '検証用スキーマ名が本番と同じです。本番へ復元するなら -ToProduction を付けてください。' }
    if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$') { throw "スキーマ名に使えない文字があります: $TargetDatabase" }

    # root の資格情報を一時的なオプションファイルへ（終了時に消す）
    $conn = Read-ConnectionString -Path $AppConfigPath
    $script:cnfTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('precure-restore-{0}.cnf' -f [guid]::NewGuid().ToString('N'))
    $cnfText = "[client]`nuser=$(ConvertTo-CnfValue $conn.User)`npassword=$(ConvertTo-CnfValue $conn.Password)`nhost=$(ConvertTo-CnfValue $conn.Host)`nport=$($conn.Port)`n"
    [System.IO.File]::WriteAllText($script:cnfTemp, $cnfText, [Text.UTF8Encoding]::new($false))

    Write-Host ""
    Write-Host "=== 復元: $(Split-Path -Leaf $DumpFile) → $TargetDatabase ===" -ForegroundColor Cyan

    if ($ToProduction) {
        if (-not $SkipSafetyBackup) {
            Write-Host "[0/3] 直前の状態を退避（backup-db.ps1 -Label before-restore）" -ForegroundColor Yellow
            & (Join-Path $PSScriptRoot 'backup-db.ps1') -Label 'before-restore' -NoLocalFiles -LocalDir $LocalDir
            if ($LASTEXITCODE -ne 0) { throw '退避に失敗したので中止します' }
        }
        if (-not $Yes) {
            $typed = Read-Host "本番スキーマ $TargetDatabase を消して $(Split-Path -Leaf $DumpFile) の内容に置き換えます。続けるにはスキーマ名を入力"
            if ($typed -ne $TargetDatabase) { throw '中止しました（入力がスキーマ名と違います）' }
        }
    }

    # --- 1. 展開 ---
    Write-Host "[1/3] 展開" -ForegroundColor Yellow
    if ($DumpFile -like '*.gz') {
        $sqlTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('precure-restore-{0}.sql' -f [guid]::NewGuid().ToString('N'))
        Expand-GZipFile -Source $DumpFile -Target $sqlTemp
        $sqlPath = $sqlTemp
    }
    else { $sqlPath = $DumpFile }
    $head = Get-Content -LiteralPath $sqlPath -TotalCount 60 -Encoding utf8
    $serverLine = ($head | Where-Object { $_ -match '^-- Server version' } | Select-Object -First 1)
    $binlogLine = ($head | Select-String -Pattern "(?:SOURCE|MASTER)_LOG_FILE='([^']+)',\s*(?:SOURCE|MASTER)_LOG_POS=(\d+)" | Select-Object -First 1)
    $serverNote = if ($serverLine) { ', ' + $serverLine.TrimStart('- ') } else { '' }
    Write-Host ("  {0}  ({1}{2})" -f (Split-Path -Leaf $DumpFile), (Format-Size (Get-Item -LiteralPath $sqlPath).Length), $serverNote)
    if ($binlogLine) { Write-Host ("  ダンプ時点の binlog: {0}:{1}" -f $binlogLine.Matches[0].Groups[1].Value, $binlogLine.Matches[0].Groups[2].Value) }

    # 文字コードは本番スキーマに合わせる（無ければ utf8mb4 / utf8mb4_unicode_ci）
    $charset = 'utf8mb4'
    $collation = 'utf8mb4_unicode_ci'
    $rows = Invoke-MysqlRows -Sql "SELECT default_character_set_name, default_collation_name FROM information_schema.schemata WHERE schema_name='$CompareWith'"
    if ($rows.Count -gt 0) { $charset = $rows[0][0]; $collation = $rows[0][1] }

    # --- 2. 流し込み ---
    Write-Host "[2/3] 流し込み（$charset / $collation）" -ForegroundColor Yellow
    Invoke-Mysql -Sql "DROP DATABASE IF EXISTS ``$TargetDatabase``; CREATE DATABASE ``$TargetDatabase`` CHARACTER SET $charset COLLATE $collation;" | Out-Null
    $createdTest = -not $ToProduction
    $sourcePath = $sqlPath -replace '\\', '/'
    $extra = @()
    if (-not $ToProduction) { $extra += '--init-command=SET sql_log_bin=0' }   # 訓練の復元は binlog に残さない
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-Mysql -Sql "source $sourcePath" -ExtraArgs $extra -DatabaseArg $TargetDatabase | Out-Null
    $sw.Stop()
    Write-Host ("  {0:N1}s" -f $sw.Elapsed.TotalSeconds)

    if ($ToProduction) {
        Write-Host "[3/3] 本番へ復元しました。ダンプ以後の操作を足すには binlog を使います（README 参照）" -ForegroundColor Green
    }
    else {
        # --- 3. 突き合わせ ---
        Write-Host "[3/3] 本番と突き合わせ（行数・CHECKSUM TABLE）" -ForegroundColor Yellow
        $tables = Invoke-MysqlRows -Sql "SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema IN ('$CompareWith','$TargetDatabase') AND table_type='BASE TABLE' ORDER BY table_name"
        $src = @{}
        $dst = @{}
        foreach ($t in $tables) { if ($t[0] -eq $CompareWith) { $src[$t[1]] = $true } else { $dst[$t[1]] = $true } }
        $all = @(@($src.Keys) + @($dst.Keys) | Sort-Object -Unique)
        $common = @($all | Where-Object { $src[$_] -and $dst[$_] })

        $srcCounts = Get-TableCounts -Db $CompareWith -Names @($src.Keys)
        $dstCounts = Get-TableCounts -Db $TargetDatabase -Names @($dst.Keys)
        $srcSums = Get-TableChecksums -Db $CompareWith -Names $common
        $dstSums = Get-TableChecksums -Db $TargetDatabase -Names $common

        $mismatch = 0
        $report = foreach ($name in $all) {
            $inBoth = $src[$name] -and $dst[$name]
            $sumState = if (-not $inBoth) { '—' } elseif ($srcSums[$name] -eq $dstSums[$name]) { '一致' } else { '不一致' }
            $rowsSame = $inBoth -and ($srcCounts[$name] -eq $dstCounts[$name])
            if (-not ($inBoth -and $rowsSame -and $sumState -eq '一致')) { $mismatch++ }
            [pscustomobject]@{
                'テーブル'  = $name
                '本番 行数' = if ($src[$name]) { $srcCounts[$name] } else { '(無し)' }
                '復元 行数' = if ($dst[$name]) { $dstCounts[$name] } else { '(無し)' }
                'CHECKSUM'  = $sumState
            }
        }
        $report | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
        if ($mismatch -eq 0) {
            Write-Host "全 $($common.Count) テーブルが一致しました" -ForegroundColor Green
        }
        else {
            Write-Host "!!! $mismatch テーブルが不一致（ダンプ以後に本番が変わっていればその分は不一致になります）" -ForegroundColor Red
            $exitCode = 3
        }
    }
}
catch {
    Write-Host "!!! 復元失敗: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($createdTest -and -not $KeepTestDatabase) {
        try {
            Invoke-Mysql -Sql "DROP DATABASE IF EXISTS ``$TargetDatabase``" -ExtraArgs @('--init-command=SET sql_log_bin=0') | Out-Null
            Write-Host "  検証用スキーマ $TargetDatabase を消しました" -ForegroundColor DarkGray
        }
        catch { Write-Warning "検証用スキーマ $TargetDatabase を消せませんでした: $($_.Exception.Message)" }
    }
    if ($sqlTemp -and (Test-Path -LiteralPath $sqlTemp)) { Remove-Item -LiteralPath $sqlTemp -Force -ErrorAction SilentlyContinue }
    if ($script:cnfTemp -and (Test-Path -LiteralPath $script:cnfTemp)) { Remove-Item -LiteralPath $script:cnfTemp -Force -ErrorAction SilentlyContinue }
}

exit $exitCode
