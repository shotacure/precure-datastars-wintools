<#
.SYNOPSIS
  precure_datastars データベースと、git に入れていないローカル専用ファイルのバックアップを 1 回分とる。

.DESCRIPTION
  mysqldump で稼働中の DB から整合性のとれたダンプ（--single-transaction）を取り、gzip 圧縮して
  日時つきのファイル名（precure_datastars_YYYYMMDD-HHmm[_ラベル].sql.gz）で保存先（既定 D:\backup\precure-datastars）
  に置き、同じものをミラー先（既定 H:\マイドライブ\backup\precure-datastars ＝ Google ドライブ）へ写す。

  ダンプの先頭には、ダンプ時点の binlog の座標をコメントで記録し（--source-data=2）、同じ瞬間に binlog を
  切り替える（--flush-logs）。サーバ側の binlog（30 日保持）と組み合わせれば、ダンプ以後の任意の時点まで戻せる。

  あわせて、リポジトリに入れていないローカル専用ファイル（db/data-fixes/ の SQL、各プロジェクトの App.config、
  CLAUDE.md、docs/*.md、.claude/settings.local.json、Claude Code のメモリ）を 1 つの ZIP
  （local-files_YYYYMMDD-HHmm_内容ハッシュ.zip）にまとめる。内容（パス・サイズ・更新時刻）が前回と同じなら作らない。

  接続情報は MySQL のオプションファイル（既定 %APPDATA%\precure-datastars\backup.cnf）から読む。
  スクリプトにも引数にもパスワードを書かない。ファイルの形と backup_ro に要る権限は README
  「データベースのバックアップと復元」を参照。

  世代の間引きはしない（1 回 4 MB 前後。1 年ためても 1.5 GB に満たない）。
  結果は保存先の backup.log に 1 行ずつ追記する（タスクスケジューラからの実行は画面が無いので、これが記録になる）。

.PARAMETER Label
  ファイル名の末尾に添える印（例 before-hs12）。手動で取るときに「何の前のバックアップか」を残す。
  英数字・ハイフン・アンダースコアのみ。

.PARAMETER LocalDir
  バックアップの保存先。既定 D:\backup\precure-datastars。

.PARAMETER MirrorDir
  ミラー先。既定 H:\マイドライブ\backup\precure-datastars。ミラー先のドライブが無いときは警告だけ出して
  成功扱いにする（ローカルの保存が主）。写せなかった分は次回の実行でまとめて写す。

.PARAMETER NoMirror
  ミラー先へ写さない。

.PARAMETER NoLocalFiles
  ローカル専用ファイルの ZIP を作らない。

.PARAMETER NoBinlogCoordinates
  binlog の座標の記録と切り替えをしない（RELOAD / REPLICATION CLIENT 権限の無いユーザーで取るとき用）。

.PARAMETER Database
  ダンプするスキーマ名。既定 precure_datastars。

.PARAMETER CnfPath
  接続情報のオプションファイル。既定 %APPDATA%\precure-datastars\backup.cnf。

.PARAMETER MysqlDumpPath
  mysqldump.exe の場所。既定 C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe。

.PARAMETER ClaudeMemoryDir
  Claude Code のメモリのディレクトリ。無ければ飛ばす。

.EXAMPLE
  .\scripts\backup-db.ps1
  日次のバックアップ（タスクスケジューラが毎日呼ぶのと同じ）。

.EXAMPLE
  .\scripts\backup-db.ps1 -Label before-hs12
  クレジット投入などの DB 書き込みの前に、印をつけて取る。
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_-]*$')]
    [string]$Label = '',
    [string]$LocalDir = 'D:\backup\precure-datastars',
    [string]$MirrorDir = 'H:\マイドライブ\backup\precure-datastars',
    [switch]$NoMirror,
    [switch]$NoLocalFiles,
    [switch]$NoBinlogCoordinates,
    [string]$Database = 'precure_datastars',
    [string]$CnfPath = (Join-Path $env:APPDATA 'precure-datastars\backup.cnf'),
    [string]$MysqlDumpPath = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe',
    [string]$ClaudeMemoryDir = (Join-Path $env:USERPROFILE '.claude\projects\d--web-precure-datastars-precure-datastars-wintools\memory')
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# スクリプトの 1 つ上がリポジトリルート。
$repoRoot = Split-Path -Parent $PSScriptRoot
$startedAt = Get-Date
$stamp = $startedAt.ToString('yyyyMMdd-HHmm')
$suffix = if ($Label) { "_$Label" } else { '' }
$dumpBase = "${Database}_${stamp}${suffix}"
$logPath = Join-Path $LocalDir 'backup.log'

# ---------------------------------------------------------------------------------------------------
# 補助関数
# ---------------------------------------------------------------------------------------------------

# 実行ログ（保存先の backup.log）に 1 行追記する。
function Write-Log {
    param([string]$Status, [string]$Message)
    $line = '{0}  {1,-4}  {2}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Status, $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
}

# 外部コマンドを引数の配列で起動し、終了コードと stdout / stderr を返す。
# PowerShell の 2>&1 と $ErrorActionPreference = 'Stop' の組み合わせ（stderr の 1 行で止まる）を避けるため
# .NET の Process を直接使う。ArgumentList は要素ごとに正しく引用符を付ける。
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

# ダンプが最後まで書かれたか。mysqldump は正常終了時に末尾へ "-- Dump completed on ..." を書く。
function Test-DumpCompleted {
    param([string]$Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $len = [int][Math]::Min(256, $fs.Length)
        if ($len -eq 0) { return $false }
        $null = $fs.Seek(-$len, [System.IO.SeekOrigin]::End)
        $buf = [byte[]]::new($len)
        $null = $fs.Read($buf, 0, $len)
        return ([Text.Encoding]::UTF8.GetString($buf) -match '-- Dump completed on \d{4}-\d{2}-\d{2}')
    }
    finally { $fs.Dispose() }
}

# gzip 圧縮（.NET の GZipStream。外部ツールは要らない）。
function Compress-GZipFile {
    param([string]$Source, [string]$Target)
    $in  = [System.IO.File]::OpenRead($Source)
    $out = [System.IO.File]::Create($Target)
    $gz  = [System.IO.Compression.GZipStream]::new($out, [System.IO.Compression.CompressionLevel]::Optimal)
    try { $in.CopyTo($gz) }
    finally { $gz.Dispose(); $out.Dispose(); $in.Dispose() }
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} KB' -f [Math]::Ceiling($Bytes / 1KB))
}

# ローカル専用ファイル（git に入れていないもの）の一覧を、ZIP 内のエントリ名つきで返す。
# リポジトリ内のものは repo/<相対パス>、Claude Code のメモリは claude-memory/<相対パス>。
function Get-LocalOnlyFiles {
    $list = [System.Collections.Generic.List[object]]::new()
    $addRepoFile = {
        param([System.IO.FileInfo]$File)
        $rel = [System.IO.Path]::GetRelativePath($repoRoot, $File.FullName) -replace '\\', '/'
        $list.Add([pscustomobject]@{ File = $File.FullName; Entry = "repo/$rel"; Length = $File.Length; LastWrite = $File.LastWriteTimeUtc })
    }
    # データ修正 SQL（db/data-fixes/。.git/info/exclude で除外している）
    $dir = Join-Path $repoRoot 'db\data-fixes'
    if (Test-Path -LiteralPath $dir) {
        Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object { & $addRepoFile $_ }
    }
    # 接続文字列や API キーを持つ各プロジェクトの App.config（.gitignore で除外している）
    Get-ChildItem -LiteralPath $repoRoot -Directory -Filter 'PrecureDataStars.*' | ForEach-Object {
        $p = Join-Path $_.FullName 'App.config'
        if (Test-Path -LiteralPath $p) { & $addRepoFile (Get-Item -LiteralPath $p) }
    }
    # Claude 用の指示書・設計メモ・ローカル設定
    foreach ($name in @('CLAUDE.md', '.claude\settings.local.json')) {
        $p = Join-Path $repoRoot $name
        if (Test-Path -LiteralPath $p) { & $addRepoFile (Get-Item -LiteralPath $p) }
    }
    $docs = Join-Path $repoRoot 'docs'
    if (Test-Path -LiteralPath $docs) {
        Get-ChildItem -LiteralPath $docs -File -Filter '*.md' | ForEach-Object { & $addRepoFile $_ }
    }
    # Claude Code のメモリ（リポジトリの外）
    if ($ClaudeMemoryDir -and (Test-Path -LiteralPath $ClaudeMemoryDir)) {
        Get-ChildItem -LiteralPath $ClaudeMemoryDir -Recurse -File | ForEach-Object {
            $rel = [System.IO.Path]::GetRelativePath($ClaudeMemoryDir, $_.FullName) -replace '\\', '/'
            $list.Add([pscustomobject]@{ File = $_.FullName; Entry = "claude-memory/$rel"; Length = $_.Length; LastWrite = $_.LastWriteTimeUtc })
        }
    }
    return ($list | Sort-Object Entry)
}

# ---------------------------------------------------------------------------------------------------
# 本体
# ---------------------------------------------------------------------------------------------------

$sqlTmp = Join-Path $LocalDir "${dumpBase}.sql.tmp"
$gzTmp  = Join-Path $LocalDir "${dumpBase}.sql.gz.tmp"
$gzPath = Join-Path $LocalDir "${dumpBase}.sql.gz"
$exitCode = 0

try {
    New-Item -ItemType Directory -Force -Path $LocalDir | Out-Null
    if (-not (Test-Path -LiteralPath $CnfPath)) {
        throw "接続情報ファイルがありません: $CnfPath（README「データベースのバックアップと復元」の手順で作ってください）"
    }
    if (-not (Test-Path -LiteralPath $MysqlDumpPath)) { throw "mysqldump が見つかりません: $MysqlDumpPath" }

    Write-Host ""
    Write-Host "=== DB バックアップ: $Database  ($stamp$suffix) ===" -ForegroundColor Cyan

    # --- 1. ダンプ ---
    $dumpArgs = @(
        "--defaults-extra-file=$CnfPath",   # 先頭に置く決まり（mysqldump の仕様）
        '--single-transaction',             # 稼働中のまま、1 つのスナップショットとして整合性のとれた内容を取る
        '--routines', '--triggers', '--events',
        '--hex-blob',
        '--no-tablespaces',                 # PROCESS 権限を要らなくする
        '--set-gtid-purged=OFF',
        '--default-character-set=utf8mb4',
        "--result-file=$sqlTmp"             # stdout を経由しない（Windows の改行変換と文字化けを避ける）
    )
    if (-not $NoBinlogCoordinates) {
        # ダンプ時点の binlog の座標をコメントで記録し、同じ瞬間に binlog を切り替える。
        # ダンプ以後の操作を binlog から足すときは、この座標（＝切り替え後の新しいファイルの先頭）から読む。
        $dumpArgs += @('--source-data=2', '--flush-logs')
    }
    $dumpArgs += $Database

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Native -Exe $MysqlDumpPath -Arguments $dumpArgs
    $stderr = ($r.StdErr -split "`r?`n" | Where-Object { $_ }) -join ' / '
    if ($r.ExitCode -ne 0) { throw "mysqldump が失敗しました (exit $($r.ExitCode)): $stderr" }
    if (-not (Test-DumpCompleted -Path $sqlTmp)) { throw 'ダンプの末尾に "Dump completed" がありません（途中で切れています）' }
    if ($stderr) { Write-Warning "mysqldump: $stderr" }

    $binlogNote = ''
    if (-not $NoBinlogCoordinates) {
        $head = Get-Content -LiteralPath $sqlTmp -TotalCount 60 -Encoding utf8
        $m = $head | Select-String -Pattern "(?:SOURCE|MASTER)_LOG_FILE='([^']+)',\s*(?:SOURCE|MASTER)_LOG_POS=(\d+)" | Select-Object -First 1
        if ($m) { $binlogNote = '{0}:{1}' -f $m.Matches[0].Groups[1].Value, $m.Matches[0].Groups[2].Value }
    }
    $rawSize = (Get-Item -LiteralPath $sqlTmp).Length

    # --- 2. 圧縮 ---
    Compress-GZipFile -Source $sqlTmp -Target $gzTmp
    Remove-Item -LiteralPath $sqlTmp -Force
    Move-Item -LiteralPath $gzTmp -Destination $gzPath -Force
    $gzSize = (Get-Item -LiteralPath $gzPath).Length
    $sw.Stop()
    $binlogDisplay = if ($binlogNote) { ", binlog $binlogNote" } else { '' }
    Write-Host ("  dump   : {0}  ({1} → {2}, {3:N1}s{4})" -f (Split-Path -Leaf $gzPath), (Format-Size $rawSize), (Format-Size $gzSize), $sw.Elapsed.TotalSeconds, $binlogDisplay)

    # --- 3. ローカル専用ファイルの ZIP ---
    $filesNote = 'skip'
    if (-not $NoLocalFiles) {
        $files = @(Get-LocalOnlyFiles)
        if ($files.Count -gt 0) {
            # 内容の指紋（パス・サイズ・更新時刻）。前回と同じならファイルを増やさない。
            $manifest = ($files | ForEach-Object { '{0}|{1}|{2}' -f $_.Entry, $_.Length, $_.LastWrite.Ticks }) -join "`n"
            $sha = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifest))
            $hash8 = ([BitConverter]::ToString($sha) -replace '-', '').Substring(0, 8).ToLowerInvariant()
            $existing = Get-ChildItem -LiteralPath $LocalDir -File -Filter "local-files_*_${hash8}.zip" | Select-Object -First 1
            if ($existing) {
                $filesNote = "unchanged ($($existing.Name))"
                Write-Host "  files  : 変更なし（$($existing.Name) と同じ内容）"
            }
            else {
                $zipPath = Join-Path $LocalDir "local-files_${stamp}_${hash8}.zip"
                $zipTmp = "${zipPath}.tmp"
                $zip = [System.IO.Compression.ZipFile]::Open($zipTmp, [System.IO.Compression.ZipArchiveMode]::Create)
                try {
                    foreach ($f in $files) {
                        $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.File, $f.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
                    }
                }
                finally { $zip.Dispose() }
                Move-Item -LiteralPath $zipTmp -Destination $zipPath -Force
                $filesNote = "$(Split-Path -Leaf $zipPath) ($($files.Count) files)"
                Write-Host "  files  : $(Split-Path -Leaf $zipPath)  ($($files.Count) ファイル, $(Format-Size (Get-Item -LiteralPath $zipPath).Length))"
            }
        }
    }

    # --- 4. ミラー ---
    $mirrorNote = 'skip'
    if (-not $NoMirror) {
        $root = [System.IO.Path]::GetPathRoot($MirrorDir)
        if ($root -and (Test-Path -LiteralPath $root)) {
            New-Item -ItemType Directory -Force -Path $MirrorDir | Out-Null
            $copied = 0
            # 今回の成果物だけでなく、ミラー先に無い（またはサイズの違う）過去の成果物も写す。
            # ミラー先のドライブが使えなかった回の分を、次の実行で取り返す。
            foreach ($f in (Get-ChildItem -LiteralPath $LocalDir -File | Where-Object { $_.Extension -in @('.gz', '.zip') })) {
                $dest = Join-Path $MirrorDir $f.Name
                if ((Test-Path -LiteralPath $dest) -and ((Get-Item -LiteralPath $dest).Length -eq $f.Length)) { continue }
                Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
                $copied++
            }
            $mirrorNote = "OK ($copied copied)"
            Write-Host "  mirror : $MirrorDir  ($copied ファイルを写した)"
        }
        else {
            $mirrorNote = 'UNAVAILABLE'
            Write-Warning "ミラー先のドライブがありません: $MirrorDir（ローカルには保存済み。次回の実行で写します）"
        }
    }

    $binlogLog = if ($binlogNote) { "  binlog=$binlogNote" } else { '' }
    Write-Log -Status 'OK' -Message ('{0}  {1}{2}  files={3}  mirror={4}' -f (Split-Path -Leaf $gzPath), (Format-Size $gzSize), $binlogLog, $filesNote, $mirrorNote)
    Write-Host "完了" -ForegroundColor Green
}
catch {
    foreach ($t in @($sqlTmp, $gzTmp)) {
        if (Test-Path -LiteralPath $t) { Remove-Item -LiteralPath $t -Force -ErrorAction SilentlyContinue }
    }
    $msg = $_.Exception.Message
    try { Write-Log -Status 'FAIL' -Message $msg } catch { }
    Write-Host "!!! バックアップ失敗: $msg" -ForegroundColor Red
    $exitCode = 1
}

exit $exitCode
