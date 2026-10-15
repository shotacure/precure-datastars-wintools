<#
.SYNOPSIS
  precure.tv の定時の本番デプロイ。コミット済みの develop を一時作業ツリーに取り出し、deploy.ps1 を流す。

.DESCRIPTION
  毎週土曜 8:30 にタスクスケジューラから動かす（登録は register-deploy-task.ps1）。
  コラムは公開日時（原稿の date）を過ぎた後の本番ビルドで初めて出るので、このデプロイで土曜 8:30 のコラムが公開される。
  サイト全体を作り直すため、その時点の DB の内容（その週に入れたクレジットなど）も一緒に公開される。

  作業中のファイル（未コミットの変更）を公開しないよう、リポジトリの作業ツリーではなく、
  コミット済みの develop を git worktree で一時フォルダに取り出してビルドする。
  App.config は git に入れていないので、リポジトリの SiteBuilder のものを写して使う。
  出力先・キャッシュは App.config の絶対パスなので、手動のデプロイと共用になる。

  安全ゲート（決まって起きる以外の削除・ビルド警告）で止まったときや失敗したときは公開しない。
  経過はすべてログ（%LOCALAPPDATA%\precure-datastars\scheduled-deploy.log）に追記する。

.PARAMETER Branch
  取り出すブランチ。既定 develop。

.PARAMETER DryRunOnly
  差分の確認（deploy.ps1 -DryRunOnly）だけを行い、本番には反映しない。仕組みの動作確認に使う。

.EXAMPLE
  .\scripts\scheduled-deploy.ps1
.EXAMPLE
  .\scripts\scheduled-deploy.ps1 -DryRunOnly
  本番に出さずに、一時作業ツリーでのビルドと差分の確認までを試す。
#>
[CmdletBinding()]
param(
    [string]$Branch = 'develop',
    [switch]$DryRunOnly
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repoRoot = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:LOCALAPPDATA 'precure-datastars'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir 'scheduled-deploy.log'

function Write-Log([string]$message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

$worktree = Join-Path ([IO.Path]::GetTempPath()) ("precure-tv-deploy-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$succeeded = $false
Write-Log "=== 定時デプロイを始めます（$Branch）==="
try {
    $commit = (& git -C $repoRoot rev-parse --short $Branch).Trim()
    if ($LASTEXITCODE -ne 0) { throw "ブランチ $Branch を読めません。" }
    Write-Log "コミット $commit を一時作業ツリーに取り出します。"

    & git -C $repoRoot worktree add --detach $worktree $Branch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "一時作業ツリーを作れませんでした。" }

    $appConfig = Join-Path $repoRoot 'PrecureDataStars.SiteBuilder/App.config'
    if (-not (Test-Path -LiteralPath $appConfig)) { throw "App.config がありません: PrecureDataStars.SiteBuilder/App.config" }
    Copy-Item -LiteralPath $appConfig -Destination (Join-Path $worktree 'PrecureDataStars.SiteBuilder/App.config')

    # deploy.ps1 の出力はすべてログにも残す（ゲートで止まった理由・差分の件数を後から読めるように）。
    $deployArgs = @{}
    if ($DryRunOnly) { $deployArgs['DryRunOnly'] = $true; Write-Log "差分の確認だけを行います（-DryRunOnly）。" }
    & (Join-Path $worktree 'scripts/deploy.ps1') @deployArgs *>&1 | ForEach-Object {
        Add-Content -LiteralPath $logPath -Value "    $_" -Encoding UTF8
        $_
    }
    $succeeded = $true
}
catch {
    Write-Log "失敗しました：$($_.Exception.Message)"
}
finally {
    if (Test-Path -LiteralPath $worktree) {
        & git -C $repoRoot worktree remove --force $worktree 2>&1 | Out-Null
        if (Test-Path -LiteralPath $worktree) { Remove-Item -LiteralPath $worktree -Recurse -Force -ErrorAction SilentlyContinue }
        & git -C $repoRoot worktree prune 2>&1 | Out-Null
    }
    Write-Log ("=== 定時デプロイを終えました：{0} ===" -f ($(if ($succeeded) { '成功' } else { '公開していません' })))
}

if (-not $succeeded) { exit 1 }
