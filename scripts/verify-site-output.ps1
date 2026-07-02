<#
.SYNOPSIS
  リファクタリング前後で SiteBuilder の生成サイトがバイト同一であることを検証するスクリプト。

.DESCRIPTION
  「挙動を変えないはずの変更」（共通化・整理・依存更新など）の回帰ゲートとして、
  サイト出力の全ファイルをハッシュ比較する。使い方は 2 段階：

    1. 変更前に -Mode baseline で基準出力を生成する。
    2. 変更後に -Mode compare で現在出力を生成し、基準と全件比較する。

  いずれのモードも「ソリューションを Release ビルド → bin の dll.config の SiteOutputDir を
  一時ディレクトリへ差し替え → SiteBuilder を本番モード（--production、--deploy なし）で実行」
  という同一手順で出力を作る。--deploy を付けないため S3 / CloudFront には一切触れない。
  sitemap.xml の lastmod マニフェストも一時ディレクトリの兄弟に作られるため、本番運用の
  マニフェストを汚さない。

  比較は全ファイルの MD5 一致（バイト同一）。ただし sitemap.xml のみ <lastmod> がビルド時刻
  依存のため、値を正規化してから比較する。差分ゼロなら IDENTICAL、差分があれば
  REMOVED / ADDED / CHANGED の一覧を出力して exit 1。

  注意：
    - 出力にはビルド当日の日付が入るページがある（ホームの「YYYY年M月D日現在」等）。
      baseline と compare は必ず同じ日（ローカル日付）に実行すること。
    - baseline と compare の間に DB の内容を変更しないこと（出力データが変わる）。
    - dll.config へのパッチは終了時に App.config の内容で復元する（成功・失敗とも）。

.PARAMETER Mode
  baseline = 基準出力の生成のみ / compare = 現在出力を生成して基準と比較。

.PARAMETER WorkRoot
  基準出力・現在出力を置く作業ディレクトリ。既定は %TEMP%\pds-site-verify。

.EXAMPLE
  .\scripts\verify-site-output.ps1 -Mode baseline
  変更前の基準出力を生成する。

.EXAMPLE
  .\scripts\verify-site-output.ps1 -Mode compare
  変更後の出力を生成し、基準とバイト同一であることを検証する。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('baseline', 'compare')]
    [string]$Mode,

    [string]$WorkRoot = (Join-Path $env:TEMP 'pds-site-verify')
)

# cmdlet の失敗は即停止。native（dotnet / SiteBuilder.exe）の失敗は $LASTEXITCODE で個別判定する。
$ErrorActionPreference = 'Stop'

# スクリプトの 1 つ上がリポジトリルート。どこから呼んでもルート基準で動かす。
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$binDir        = Join-Path $repoRoot 'PrecureDataStars.SiteBuilder\bin\Release\net9.0'
$exePath       = Join-Path $binDir 'PrecureDataStars.SiteBuilder.exe'
$cfgPath       = Join-Path $binDir 'PrecureDataStars.SiteBuilder.dll.config'
$appConfigPath = Join-Path $repoRoot 'PrecureDataStars.SiteBuilder\App.config'
$baselineDir   = Join-Path $WorkRoot 'site-baseline'
$currentDir    = Join-Path $WorkRoot 'site-current'
$targetDir     = if ($Mode -eq 'baseline') { $baselineDir } else { $currentDir }

Write-Host ""
Write-Host "=== サイト出力同一性検証 [$Mode] ===" -ForegroundColor Cyan
Write-Host ""

# --- 1) Release ビルド ---
Write-Host "[1/3] Release ビルド" -ForegroundColor Yellow
& dotnet build (Join-Path $repoRoot 'precure-datastars-wintools.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw "Release ビルドが失敗しました (exit $LASTEXITCODE)。" }

try {
    # --- 2) bin 側 dll.config の SiteOutputDir を一時ディレクトリへ差し替え ---
    # ソースの App.config には触れない。dotnet build が dll.config を再コピーする場合が
    # あるため、パッチは常に「ビルドの後」に当てる。
    Write-Host "[2/3] 出力先を一時ディレクトリへ切り替えて SiteBuilder を実行" -ForegroundColor Yellow
    if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force -Confirm:$false }

    [xml]$cfg = Get-Content $cfgPath -Raw
    $outNode = $cfg.configuration.appSettings.add | Where-Object { $_.key -eq 'SiteOutputDir' }
    if (-not $outNode) { throw "dll.config に appSettings/SiteOutputDir が見つかりません: $cfgPath" }
    $outNode.value = $targetDir
    $cfg.Save($cfgPath)

    # --- 3) フルビルド実行（本番モード・デプロイなし＝ローカル出力のみ） ---
    & $exePath --production
    if ($LASTEXITCODE -ne 0) { throw "SiteBuilder の実行が失敗しました (exit $LASTEXITCODE)。" }
}
finally {
    # dll.config を App.config の内容へ復元する。パッチ残骸が残ると、次に手動で
    # SiteBuilder を実行したとき出力先が一時ディレクトリのままになるため。
    Copy-Item $appConfigPath $cfgPath -Force
}

if ($Mode -eq 'baseline') {
    $count = (Get-ChildItem $baselineDir -Recurse -File | Measure-Object).Count
    Write-Host ""
    Write-Host "基準出力を生成しました（$count ファイル）: $baselineDir" -ForegroundColor Green
    return
}

# --- 4) 比較（sitemap.xml のみ <lastmod> を正規化、他は生バイト MD5） ---
Write-Host "[3/3] 基準出力と比較" -ForegroundColor Yellow
if (-not (Test-Path $baselineDir)) {
    throw "基準出力がありません。先に -Mode baseline を実行してください: $baselineDir"
}

function Get-TreeHashes([string]$root) {
    $map = @{}
    Get-ChildItem $root -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).TrimStart('\')
        if ($rel -ieq 'sitemap.xml') {
            # lastmod はビルド時刻・マニフェスト由来で毎回変わり得るため値を固定してから比較する
            $txt = [IO.File]::ReadAllText($_.FullName)
            $txt = [regex]::Replace($txt, '<lastmod>[^<]*</lastmod>', '<lastmod>N</lastmod>')
            $ms = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($txt))
            $map[$rel] = (Get-FileHash -InputStream $ms -Algorithm MD5).Hash
        }
        else {
            $map[$rel] = (Get-FileHash $_.FullName -Algorithm MD5).Hash
        }
    }
    return $map
}

$b = Get-TreeHashes $baselineDir
$c = Get-TreeHashes $currentDir

$removed = @($b.Keys | Where-Object { -not $c.ContainsKey($_) } | Sort-Object)
$added   = @($c.Keys | Where-Object { -not $b.ContainsKey($_) } | Sort-Object)
$changed = @($b.Keys | Where-Object { $c.ContainsKey($_) -and $c[$_] -ne $b[$_] } | Sort-Object)

Write-Host ""
if ($removed.Count -or $added.Count -or $changed.Count) {
    $removed | ForEach-Object { Write-Host "REMOVED: $_" -ForegroundColor Red }
    $added   | ForEach-Object { Write-Host "ADDED:   $_" -ForegroundColor Red }
    $changed | ForEach-Object { Write-Host "CHANGED: $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host ("出力が一致しません（removed {0} / added {1} / changed {2}）" -f $removed.Count, $added.Count, $changed.Count) -ForegroundColor Red
    exit 1
}

Write-Host ("IDENTICAL（{0} ファイル、バイト同一）" -f $b.Count) -ForegroundColor Green
