<#
.SYNOPSIS
  precure.tv の定時の本番デプロイ。リポジトリの作業フォルダで deploy.ps1 を流す。

.DESCRIPTION
  毎日 6:00 と、毎週水曜・土曜の 8:30 にタスクスケジューラから動かす（登録は register-deploy-task.ps1）。
  6:00 の回は毎日、Amazon の画像 URL を取り直して公開し直す（画像のリンクの保存は最長 24 時間）。
  コラムは公開日時（原稿の date）を過ぎた後の本番ビルドで初めて出るので、8:30 の回で水曜・土曜 8:30 のコラムが公開される。
  6:00 の回でそれまでの変更を流しておくので、8:30 の回は差分がほぼコラムだけになる。
  サイト全体を作り直すため、その時点の DB の内容（その週に入れたクレジットなど）も一緒に公開される。

  手動のデプロイ（deploy.ps1 を作業フォルダで流す）とまったく同じことをする。コードはその時点の作業フォルダのもの
  （未コミットの変更も含む）、App.config・出力先・作り置き（OGP カード・テロップ画像）も手動のデプロイと同じものを使う。

  安全ゲート（大量の削除・ビルド警告）で止まったときや失敗したときは公開しない。
  経過はすべてログ（%LOCALAPPDATA%\precure-datastars\scheduled-deploy.log）に追記する。
  公開できなかったときは Windows の通知（トースト）を出す。通知は Windows PowerShell 5.1 の WinRT で出すので、
  PowerShell 7 から動かしても出る。通知を出せなかったときはログに書くだけで、デプロイの結果は変えない。

.PARAMETER DryRunOnly
  差分の確認（deploy.ps1 -DryRunOnly）だけを行い、本番には反映しない。仕組みの動作確認に使う。

.PARAMETER TestNotification
  デプロイはせず、公開できなかったときの通知だけを出す。通知が見えるかの確認に使う。

.EXAMPLE
  .\scripts\scheduled-deploy.ps1
.EXAMPLE
  .\scripts\scheduled-deploy.ps1 -DryRunOnly
  本番に出さずに、ビルドと差分の確認までを試す。
#>
[CmdletBinding()]
param(
    [switch]$DryRunOnly,
    [switch]$TestNotification
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

# 公開できなかったことを Windows の通知で知らせる。タスクは画面を出さずに動くので、ログだけでは気づけない。
# PowerShell 7 からは WinRT の型を直接使えないため、Windows PowerShell 5.1 に通知の組み立てを任せる
# （アプリ ID は Windows PowerShell のもの。通知センターにも残る）。
function Show-FailureNotification([string]$reason) {
    try {
        $esc = { param($t) [Security.SecurityElement]::Escape($t).Replace("'", "''") }
        $title = & $esc 'precure.tv の定時デプロイが公開できませんでした'
        $body = & $esc ("{0}　{1}" -f (Get-Date -Format 'M/d HH:mm'), $reason)
        $foot = & $esc ("記録: $logPath")
        $toastScript = @"
`$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
`$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
`$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
`$xml.LoadXml('<toast scenario="reminder"><visual><binding template="ToastGeneric"><text>$title</text><text>$body</text><text>$foot</text></binding></visual><actions><action content="閉じる" arguments="dismiss" activationType="system"/></actions></toast>')
`$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(`$appId).Show([Windows.UI.Notifications.ToastNotification]::new(`$xml))
"@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($toastScript))
        & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -EncodedCommand $encoded 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "powershell.exe の終了コード $LASTEXITCODE" }
    }
    catch {
        Write-Log "通知を出せませんでした：$($_.Exception.Message)"
    }
}

if ($TestNotification) {
    Show-FailureNotification '通知の確認です（デプロイはしていません）。'
    return
}

$succeeded = $false
$failureReason = ''
Write-Log "=== 定時デプロイを始めます ==="
try {
    $commit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
    $dirty = @(& git -C $repoRoot status --porcelain).Count
    Write-Log "作業フォルダ（コミット $commit、未コミットの変更 $dirty 件）でビルドします。"

    # deploy.ps1 の出力はすべてログにも残す（ゲートで止まった理由・差分の件数を後から読めるように）。
    $deployArgs = @{}
    if ($DryRunOnly) { $deployArgs['DryRunOnly'] = $true; Write-Log "差分の確認だけを行います（-DryRunOnly）。" }
    & (Join-Path $repoRoot 'scripts/deploy.ps1') @deployArgs *>&1 | ForEach-Object {
        Add-Content -LiteralPath $logPath -Value "    $_" -Encoding UTF8
        $_
    }
    $succeeded = $true
}
catch {
    $failureReason = $_.Exception.Message
    Write-Log "失敗しました：$failureReason"
}
finally {
    Write-Log ("=== 定時デプロイを終えました：{0} ===" -f ($(if ($succeeded) { '成功' } else { '公開していません' })))
}

if (-not $succeeded) {
    Show-FailureNotification $failureReason
    exit 1
}
