<#
.SYNOPSIS
  precure-datastars-wintools のリリースビルド一式を自動化するスクリプト。

.DESCRIPTION
  Directory.Build.props からバージョン番号を自動取得し、
  ソリューションを clean → restore した上で、配布対象の全 EXE プロジェクトを
  publish して ZIP にまとめ、DB スキーマ一式の ZIP も作成する。

  成果物はリポジトリルート配下の release/ フォルダに配置される。
  publish/ は中間生成物（各プロジェクトの publish 出力）置き場。

  デフォルトではフレームワーク依存（self-contained=false）で publish するため、
  配布先に .NET 9 Desktop Runtime が必要。-SelfContained を付けた場合は
  ランタイム同梱でビルドされ、配布先にランタイム不要だがサイズが数倍に膨らむ。

.PARAMETER Configuration
  ビルド構成（Debug/Release）。既定は Release。

.PARAMETER Runtime
  publish の対象 RID。既定は win-x64。

.PARAMETER SelfContained
  指定すると self-contained（ランタイム同梱）で publish する。

.PARAMETER SkipClean
  指定すると dotnet clean をスキップする。差分ビルドで高速化したい時に使う。

.EXAMPLE
  .\scripts\build-release.ps1

.EXAMPLE
  .\scripts\build-release.ps1 -SelfContained

.EXAMPLE
  .\scripts\build-release.ps1 -Configuration Release -SkipClean
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$SkipClean
)

# エラー発生時は即座に停止する（スクリプト全体で失敗ハンドリングを統一）
$ErrorActionPreference = 'Stop'

# スクリプトの 1 つ上がリポジトリルート
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# ソリューション名はリポジトリ直下の .sln を 1 つだけ前提で自動検出する
$sln = Get-ChildItem -Path $repoRoot -Filter *.sln -File | Select-Object -First 1
if (-not $sln) {
    throw "ソリューションファイル (.sln) がリポジトリルートに見つかりません: $repoRoot"
}

# Directory.Build.props から <Version> 要素を抜き出す。
# ここが単一のバージョンソースなので、csproj 個別に Version を書いていないことが前提。
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) {
    throw "Directory.Build.props が見つかりません: $propsPath"
}
[xml]$propsXml = Get-Content -Path $propsPath -Raw
$version = ($propsXml.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) {
    throw "Directory.Build.props 内に <Version> 要素が見つかりません。"
}
$version = $version.ToString().Trim()

Write-Host ""
Write-Host "=== precure-datastars-wintools Release Build ===" -ForegroundColor Cyan
Write-Host "  Solution      : $($sln.Name)"
Write-Host "  Version       : $version"
Write-Host "  Configuration : $Configuration"
Write-Host "  Runtime       : $Runtime"
Write-Host "  SelfContained : $([bool]$SelfContained)"
Write-Host ""

# 配布対象の EXE プロジェクト一覧。ライブラリ (*.Data / *.Catalog.Common 等) は含めない。
# Name: ZIP ファイル名および publish サブディレクトリ名として使う短い名前
# Project: 相対 csproj パス
$targets = @(
    @{ Name = 'Catalog';            Project = 'PrecureDataStars.Catalog\PrecureDataStars.Catalog.csproj' },
    @{ Name = 'CDAnalyzer';         Project = 'PrecureDataStars.CDAnalyzer\PrecureDataStars.CDAnalyzer.csproj' },
    @{ Name = 'BDAnalyzer';         Project = 'PrecureDataStars.BDAnalyzer\PrecureDataStars.BDAnalyzer.csproj' },
    @{ Name = 'LegacyImport';       Project = 'PrecureDataStars.LegacyImport\PrecureDataStars.LegacyImport.csproj' },
    @{ Name = 'Episodes';           Project = 'PrecureDataStars.Episodes\PrecureDataStars.Episodes.csproj' },
    @{ Name = 'TitleCharStatsJson'; Project = 'PrecureDataStars.TitleCharStatsJson\PrecureDataStars.TitleCharStatsJson.csproj' },
    @{ Name = 'YouTubeCrawler';     Project = 'PrecureDataStars.YouTubeCrawler\PrecureDataStars.YouTubeCrawler.csproj' }
)

$publishRoot = Join-Path $repoRoot 'publish'
$releaseRoot = Join-Path $repoRoot 'release'

# release/ は毎回空にしてから作り直す（過去バージョンの ZIP が残らないように）。
# publish/ は -SkipClean 指定時は残す（差分 publish で高速化）。
if (-not $SkipClean) {
    if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot }
}
if (Test-Path $releaseRoot) { Remove-Item -Recurse -Force $releaseRoot }
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# dotnet コマンドが $LASTEXITCODE で失敗を通知するため、都度チェックしてスローする共通関数。
# パラメータ名は $CliArgs としている。$Args は PowerShell の関数自動変数（未束縛の残り引数が
# 自動的に入る）と衝突し、[string[]]$Args で宣言しても渡された配列が失われることがあるため。
function Invoke-Dotnet {
    param([string[]]$CliArgs)
    & dotnet @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($CliArgs -join ' ') が失敗しました (exit $LASTEXITCODE)"
    }
}

if (-not $SkipClean) {
    Write-Host "[1/6] Clean" -ForegroundColor Yellow
    Invoke-Dotnet @('clean', $sln.FullName, '-c', $Configuration, '--nologo', '-v', 'minimal')
} else {
    Write-Host "[1/6] Clean ... skipped" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "[2/6] Restore" -ForegroundColor Yellow
Invoke-Dotnet @('restore', $sln.FullName, '--nologo', '-v', 'minimal')

Write-Host ""
Write-Host "[3/6] Publish $($targets.Count) projects" -ForegroundColor Yellow

# self-contained オプションは dotnet CLI 引数に入れる（true/false どちらも明示）
$scArg = if ($SelfContained) { 'true' } else { 'false' }

foreach ($t in $targets) {
    $outDir = Join-Path $publishRoot $t.Name
    Write-Host "  - $($t.Name) -> $outDir" -ForegroundColor Gray
    Invoke-Dotnet @(
        'publish', $t.Project,
        '-c', $Configuration,
        '-r', $Runtime,
        "--self-contained=$scArg",
        '-o', $outDir,
        '--nologo',
        '-v', 'minimal'
    )
}

Write-Host ""
Write-Host "[4/6] Sanitize publish output (remove *.config, stage <AssemblyName>.dll.config.sample)" -ForegroundColor Yellow

# publish 出力には「ローカルで接続情報を書き込んだ App.config」が自動コピーされてしまうため、
# 配布 ZIP に混入させないよう明示的に除去する。.NET の SDK スタイルプロジェクトでは
# publish 時に App.config が下記 2 種類の名前で配置されるため、両方を削除対象にする:
#   - <AssemblyName>.dll.config  （現代的な形、.NET 5 以降のメイン形式）
#   - <AssemblyName>.exe.config  （旧来互換の形、並行生成される場合あり）
# さらに appsettings 系（将来使う可能性を見越して）も除去する。
#
# 削除後、リポジトリ側の App.config.sample を publish ディレクトリへコピーするが、
# 配布先の人が「.sample を外す」だけで .NET が読み込める正しい名前にするため、
# コピー先のファイル名を <AssemblyName>.dll.config.sample に変換する。
# ※ App.config という名前は VS のビルド時ソース名で、実行時には読まれない（publish で
#   <AssemblyName>.dll.config にリネームコピーされたものが読まれる）。
foreach ($t in $targets) {
    $outDir = Join-Path $publishRoot $t.Name
    if (-not (Test-Path $outDir)) { continue }

    # 実値が入っているかもしれない設定ファイルを削除
    $toRemove = @(
        Join-Path $outDir 'App.config'
        (Get-ChildItem -Path $outDir -Filter '*.exe.config'     -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
        (Get-ChildItem -Path $outDir -Filter '*.dll.config'     -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
        (Get-ChildItem -Path $outDir -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($f in $toRemove) {
        # 削除前に相対パス文字列を作る。Resolve-Path -Relative は存在するパスしか扱えないため
        # [System.IO.Path]::GetRelativePath（文字列計算のみ）を使う。
        $rel = [System.IO.Path]::GetRelativePath($repoRoot, $f)
        Remove-Item -Path $f -Force -ErrorAction SilentlyContinue
        Write-Host "  - removed: $rel" -ForegroundColor DarkGray
    }

    # publish ディレクトリから「このプロジェクトの AssemblyName」を特定する。
    # 優先順: .exe（WinForms/Console）→ .runtimeconfig.json に対応する .dll（純粋な dll 実行形式）。
    # これにより csproj の AssemblyName 変更があっても自動追従する。
    $exeFile = Get-ChildItem -Path $outDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $exeFile) {
        $exeFile = Get-ChildItem -Path $outDir -Filter '*.dll' -File -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $outDir "$($_.BaseName).runtimeconfig.json") } |
            Select-Object -First 1
    }
    if (-not $exeFile) {
        throw "publish ディレクトリ '$outDir' からアセンブリ名を特定できませんでした（.exe / runtimeconfig.json が見つからない）。"
    }
    $assemblyName = $exeFile.BaseName

    # リポジトリ側の App.config.sample を <AssemblyName>.dll.config.sample としてコピー。
    # 配布先は .sample を外すだけで .NET が読み込む正しい名前 (<AssemblyName>.dll.config) になる。
    $projectDir = Split-Path -Parent (Join-Path $repoRoot $t.Project)
    $sample = Join-Path $projectDir 'App.config.sample'
    if (Test-Path $sample) {
        $destPath = Join-Path $outDir "$assemblyName.dll.config.sample"
        Copy-Item -Path $sample -Destination $destPath -Force
        $rel = [System.IO.Path]::GetRelativePath($repoRoot, $destPath)
        Write-Host "  + added:   $rel" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "[5/6] Scan publish output for leaked credentials" -ForegroundColor Yellow

# ZIP 化前の最終ガード: publish ディレクトリ内のテキスト系ファイルを走査し、
# 接続文字列や認証情報を思わせる実値が残っていないかチェックする。
# プレースホルダ (YOUR_PASSWORD 等) はマッチさせない正規表現で、
# 意図せず残った実値だけを検出してビルドを停止させる。
$leakPattern = '(?i)(Uid|Pwd|Password|User\s*ID|PWD)\s*=\s*(?!YOUR_|your_|"YOUR|''YOUR|$|")[^;"''<>\s][^;"''<>]*'
$leakFound = @()
Get-ChildItem -Path $publishRoot -Recurse -File -Include *.config,*.xml,*.json,*.txt,*.sample -ErrorAction SilentlyContinue | ForEach-Object {
    # .sample ファイルはプレースホルダ運用が前提で配布に含めたいため、検査対象から外す
    if ($_.Name -like '*.sample') { return }
    try {
        $content = Get-Content -Path $_.FullName -Raw -ErrorAction Stop
    } catch {
        return
    }
    if ($content -and ($content -match $leakPattern)) {
        $leakFound += $_.FullName
    }
}
if ($leakFound.Count -gt 0) {
    Write-Host ""
    Write-Host "!!! 機密情報と思われる値が publish 成果物に残っています。ZIP 化を中止します。" -ForegroundColor Red
    Write-Host "    該当ファイルを確認し、必要なら build-release.ps1 の sanitize 処理を拡張してください。" -ForegroundColor Red
    $leakFound | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Aborted due to potential credential leakage in publish artifacts."
}
Write-Host "  (no credentials detected)" -ForegroundColor DarkGray

Write-Host ""
Write-Host "[6/6] Create ZIP archives" -ForegroundColor Yellow

# 配布ファイル名に self-contained 版を示すサフィックスを付ける（通常ビルドは素のまま）
$suffix = if ($SelfContained) { 'sc-' } else { '' }

foreach ($t in $targets) {
    $srcDir = Join-Path $publishRoot $t.Name
    $zipName = "PrecureDataStars.$($t.Name)-v$version-$suffix$Runtime.zip"
    $zipPath = Join-Path $releaseRoot $zipName
    Write-Host "  - $zipName" -ForegroundColor Gray
    # Compress-Archive はパス区切り正規化の都合でワイルドカード展開してから渡す
    Compress-Archive -Path (Join-Path $srcDir '*') -DestinationPath $zipPath -Force
}

# DB スキーマと migration を 1 つの ZIP にまとめる（App とは別枠でインストール先に必要）
$dbZipName = "precure-datastars-db-v$version.zip"
$dbZipPath = Join-Path $releaseRoot $dbZipName
Write-Host "  - $dbZipName" -ForegroundColor Gray
$dbSchema = Join-Path $repoRoot 'db\schema.sql'
$dbMigrations = Join-Path $repoRoot 'db\migrations'
if (-not (Test-Path $dbSchema)) { throw "db\schema.sql が見つかりません" }
if (-not (Test-Path $dbMigrations)) { throw "db\migrations フォルダが見つかりません" }
Compress-Archive -Path $dbSchema, (Join-Path $dbMigrations '*') -DestinationPath $dbZipPath -Force

Write-Host ""
Write-Host "Done" -ForegroundColor Green
Write-Host ""
Write-Host "Artifacts:" -ForegroundColor Cyan
Get-ChildItem -Path $releaseRoot -Filter *.zip | ForEach-Object {
    # 表示用にバイト数を MB で丸める（小数 2 桁）
    $sizeMb = [math]::Round($_.Length / 1MB, 2)
    Write-Host ("  {0,-60} {1,8} MB" -f $_.Name, $sizeMb)
}
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. git tag -a v$version -m `"Release v$version`""
Write-Host "  2. git push origin --tags"
Write-Host "  3. GitHub Releases から release\ 配下の ZIP をアップロード"
Write-Host ""
