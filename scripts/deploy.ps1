<#
.SYNOPSIS
  precure.tv 本番サイトの「ドライラン → 安全確認 → 反映」を 1 コマンドで実行するデプロイスクリプト。

.DESCRIPTION
  PrecureDataStars.SiteBuilder を本番モード（--production --deploy）でビルドし、生成物を S3 へ差分同期
  したうえで CloudFront を invalidation する。実行は 2 段階：

    1. まず --dry-run で差分プラン（upload / delete / unchanged）とビルド警告数を取得し、安全ゲートを通す。
         - delete（既存 S3 オブジェクトの消去）が 1 件以上 … 既定では中止する（意図的な orphan 掃除なら -Force）。
         - ビルド警告（Warnings）が 1 件以上 … 品質ゲートとして既定で中止する（-Force で続行）。
    2. ゲートを通過（delete=0 かつ Warnings=0、または -Force）した場合のみ、本番反映（--yes）を実行する。

  非対話実行のため本番反映は常に --yes（削除確認の y/N 省略）で走る。安全性は事前ドライランの
  「delete=0」ゲートで担保する。バケット名・Distribution ID・AWS プロファイル等の実値は App.config
  （gitignore 済）から読まれる。

.PARAMETER DryRunOnly
  ドライラン（差分プレビュー）のみ実行し、本番反映は行わない。

.PARAMETER Force
  安全ゲート（delete>0 / Warnings>0）を無視して反映を強行する。既存オブジェクトの削除を伴う
  変更（orphan 掃除など、ドライランで内容を確認済みの場合）を意図的に流すときだけ使う。

.EXAMPLE
  .\scripts\deploy.ps1
  ドライランで差分を確認し、安全（削除0・警告0）なら本番反映まで実行する。

.EXAMPLE
  .\scripts\deploy.ps1 -DryRunOnly
  差分プレビューだけ見る（反映しない）。

.EXAMPLE
  .\scripts\deploy.ps1 -Force
  削除を伴う差分でも反映する（内容を確認済みの orphan 掃除など）。
#>
[CmdletBinding()]
param(
    [switch]$DryRunOnly,
    [switch]$Force
)

# cmdlet（Test-Path 等）の失敗は即停止。native（dotnet）の失敗は $LASTEXITCODE で個別に判定する。
$ErrorActionPreference = 'Stop'

# スクリプトの 1 つ上がリポジトリルート。どこから呼んでもルート基準で動かす。
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$project = 'PrecureDataStars.SiteBuilder'

Write-Host ""
Write-Host "=== precure.tv 本番デプロイ ===" -ForegroundColor Cyan
Write-Host ""

# --- フェーズ 1: ドライラン（差分プレビュー） ---
# stdout を Tee-Object で変数にも取り込みつつコンソールへ流す（2>&1 は使わない＝
# native の stderr を success ストリームへ混ぜると ErrorActionPreference=Stop で
# 途中停止する PowerShell の落とし穴を避けるため。Plan/Warnings 行は stdout に出る）。
Write-Host "[1/2] ドライラン（差分プレビュー）" -ForegroundColor Yellow
& dotnet run --project $project -- --production --deploy --dry-run | Tee-Object -Variable dryOutput
if ($LASTEXITCODE -ne 0) {
    throw "ドライラン（ビルド）が失敗しました (exit $LASTEXITCODE)。上のログを確認してください。"
}
$dryText = ($dryOutput | Out-String)

# 差分プラン行を解析する。例: "  Plan             : upload 46 / delete 0 / unchanged 3079"
$planMatch = [regex]::Match($dryText, 'Plan\s*:\s*upload\s+(\d+)\s*/\s*delete\s+(\d+)\s*/\s*unchanged\s+(\d+)')
if (-not $planMatch.Success) {
    throw "ドライランの出力から差分プラン（Plan: upload .. / delete .. / unchanged ..）を読み取れませんでした。BaseUrl 未設定などでデプロイがスキップされた可能性があります。"
}
$uploadCount    = [int]$planMatch.Groups[1].Value
$deleteCount    = [int]$planMatch.Groups[2].Value
$unchangedCount = [int]$planMatch.Groups[3].Value

# ビルド警告数を解析する。例: "  Warnings         : 0"
$warnMatch = [regex]::Match($dryText, '(?m)^\s*Warnings\s*:\s*(\d+)\s*$')
$warnCount = if ($warnMatch.Success) { [int]$warnMatch.Groups[1].Value } else { -1 }
$warnDisplay = if ($warnCount -lt 0) { '不明' } else { "$warnCount" }

Write-Host ""
Write-Host ("  差分: upload {0} / delete {1} / unchanged {2} / 警告 {3}" -f $uploadCount, $deleteCount, $unchangedCount, $warnDisplay) -ForegroundColor Cyan

if ($DryRunOnly) {
    Write-Host ""
    Write-Host "  -DryRunOnly のため、反映は行いません。" -ForegroundColor DarkGray
    return
}

# 反映する変更が無ければ 2 度目のビルドを省いて終了。
if ($uploadCount -eq 0 -and $deleteCount -eq 0) {
    Write-Host ""
    Write-Host "  差分なし（upload 0 / delete 0）。反映する変更はありません。" -ForegroundColor Green
    return
}

# --- 安全ゲート ---
$blockReasons = @()
if ($deleteCount -gt 0) { $blockReasons += "削除 $deleteCount 件（既存オブジェクトの消去）" }
if ($warnCount -gt 0)   { $blockReasons += "ビルド警告 $warnCount 件" }

if ($blockReasons.Count -gt 0 -and -not $Force) {
    Write-Host ""
    Write-Host "!!! 安全ゲートにより反映を中止しました:" -ForegroundColor Red
    $blockReasons | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    Write-Host "    内容を確認し、意図的なら -Force を付けて再実行してください。" -ForegroundColor Red
    throw "Deploy aborted by safety gate（削除/警告あり）。"
}
if ($blockReasons.Count -gt 0 -and $Force) {
    Write-Host ""
    Write-Host "  -Force 指定: 安全ゲート（$($blockReasons -join ' / ')）を無視して反映します。" -ForegroundColor Magenta
}

# --- フェーズ 2: 本番反映（--yes） ---
Write-Host ""
Write-Host "[2/2] 本番反映（--production --deploy --yes）" -ForegroundColor Yellow
& dotnet run --project $project -- --production --deploy --yes
if ($LASTEXITCODE -ne 0) {
    throw "本番反映が失敗しました (exit $LASTEXITCODE)。"
}

Write-Host ""
Write-Host "Done — precure.tv に反映しました。" -ForegroundColor Green
Write-Host ""
