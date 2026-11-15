<#
.SYNOPSIS
  precure.tv の「販売中の商品」の定時の点検。Amazon で売られているかを確かめ、変わっていたら枠のファイルだけを本番に上げ直す。

.DESCRIPTION
  15 分おきにタスクスケジューラから動かす（登録は register-shop-check-task.ps1）。次の 2 つを順に行う。

    1. PrecureDataStars.AmazonSync --shop-check
       series_shop_items の出す設定の行を Creators API で確かめ、売られているか・在庫の種別・画像の URL を行に書き戻す。
    2. PrecureDataStars.SiteBuilder --publish-shop-items
       DB の行から枠のファイル（shop-items.json）を組み立て、本番のファイルと中身が違うときだけ S3 に上げ直して
       CloudFront のそのパスのキャッシュを消す。同じなら何もしない。

  サイトは作り直さない。売り切れ・絶版になった商品は次の点検で枠から外れ、また売られ始めたら次の点検で戻る。
  道具は Release でビルド済みの exe をそのまま使う（ここではビルドしない。exe が無いときだけ 1 度ビルドする）。

  経過はログ（%LOCALAPPDATA%\precure-datastars\scheduled-shop-check.log）に追記する。変わりが無い回は 1 行だけ、
  上げ直した回と失敗した回は道具の出力もすべて残す。失敗したときは Windows の通知（トースト）を出す。
  失敗が続いている間は、通知は最初の 1 回だけにする。

.PARAMETER DryRun
  Amazon には確かめるが、DB にも本番にも書かない。仕組みの動作確認に使う。

.EXAMPLE
  .\scripts\scheduled-shop-check.ps1
.EXAMPLE
  .\scripts\scheduled-shop-check.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repoRoot = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:LOCALAPPDATA 'precure-datastars'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir 'scheduled-shop-check.log'
# 前の回が失敗していたかの印。失敗が続いている間、通知を最初の 1 回だけにするために使う。
$failedMarkPath = Join-Path $logDir 'scheduled-shop-check.failed'

function Write-Log([string]$message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

. (Join-Path $PSScriptRoot 'show-toast.ps1')

function Get-ToolExe([string]$name) {
    return Get-ChildItem -Path (Join-Path $repoRoot "$name/bin/Release") -Filter "$name.exe" -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

# 道具を 1 つ動かし、出力の行（標準出力と標準エラー）と終了コードを返す。
function Invoke-Tool([string]$exe, [string[]]$toolArgs) {
    $lines = & $exe @toolArgs 2>&1 | ForEach-Object { "$_" }
    return @{ Lines = @($lines); ExitCode = $LASTEXITCODE }
}

$output = @()
try {
    $amazonSync = Get-ToolExe 'PrecureDataStars.AmazonSync'
    $siteBuilder = Get-ToolExe 'PrecureDataStars.SiteBuilder'
    if (-not $amazonSync -or -not $siteBuilder) {
        & dotnet build (Join-Path $PSScriptRoot 'deploy-tools.slnf') -c Release --nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "道具のビルドが失敗しました (exit $LASTEXITCODE)。" }
        $amazonSync = Get-ToolExe 'PrecureDataStars.AmazonSync'
        $siteBuilder = Get-ToolExe 'PrecureDataStars.SiteBuilder'
        if (-not $amazonSync -or -not $siteBuilder) { throw "道具の exe が見つかりません。" }
    }

    $checkArgs = @('--shop-check')
    $publishArgs = @('--publish-shop-items')
    if ($DryRun) { $checkArgs += '--dry-run'; $publishArgs += '--dry-run' }

    $check = Invoke-Tool $amazonSync.FullName $checkArgs
    $output += $check.Lines
    if ($check.ExitCode -ne 0) { throw "Amazon への点検が失敗しました (exit $($check.ExitCode))。" }

    $publish = Invoke-Tool $siteBuilder.FullName $publishArgs
    $output += $publish.Lines
    if ($publish.ExitCode -ne 0) { throw "枠のファイルの上げ直しが失敗しました (exit $($publish.ExitCode))。" }

    # 変わりが無い回は 1 行だけ残す。状態が変わった回・上げ直した回は道具の出力もすべて残す。
    $changed = @($output | Where-Object { $_ -match '← 変わった|上げ直しました|本番のファイルと違います' }).Count -gt 0
    if ($changed) {
        Write-Log "販売中の商品が変わりました$(if ($DryRun) { '（-DryRun のため書いていません）' })。"
        foreach ($line in $output) { Add-Content -LiteralPath $logPath -Value "    $line" -Encoding UTF8 }
    }
    else {
        Write-Log "変わりなし。"
    }
    Remove-Item -LiteralPath $failedMarkPath -ErrorAction SilentlyContinue
}
catch {
    $reason = $_.Exception.Message
    Write-Log "失敗しました：$reason"
    foreach ($line in $output) { Add-Content -LiteralPath $logPath -Value "    $line" -Encoding UTF8 }
    if (-not (Test-Path -LiteralPath $failedMarkPath)) {
        try {
            Show-Toast -Title 'precure.tv の販売中の商品の点検が失敗しました' `
                -Body ("{0}　{1}" -f (Get-Date -Format 'M/d HH:mm'), $reason) `
                -Foot "記録: $logPath"
        }
        catch {
            Write-Log "通知を出せませんでした：$($_.Exception.Message)"
        }
        Set-Content -LiteralPath $failedMarkPath -Value (Get-Date -Format 'o') -Encoding UTF8
    }
    exit 1
}
