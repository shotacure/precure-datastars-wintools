<#
.SYNOPSIS
  Windows の通知（トースト）を出す関数 Show-Toast を定める。定時のスクリプトがドットソースで読み込んで使う。

.DESCRIPTION
  タスクスケジューラのタスクは画面を出さずに動くので、ログだけでは失敗や知らせることに気づけない。
  PowerShell 7 からは WinRT の型を直接使えないため、Windows PowerShell 5.1 に通知の組み立てを任せる
  （アプリ ID は Windows PowerShell のもの。通知センターにも残る）。
  通知を出せなかったときは例外を投げる（呼ぶ側でログに書く）。

.EXAMPLE
  . (Join-Path $PSScriptRoot 'show-toast.ps1')
  Show-Toast -Title 'タイトル' -Body '本文' -Foot '記録: ...'
#>

function Show-Toast([string]$Title, [string]$Body, [string]$Foot) {
    $esc = { param($t) [Security.SecurityElement]::Escape($t).Replace("'", "''") }
    $t = & $esc $Title
    $b = & $esc $Body
    $f = & $esc $Foot
    $toastScript = @"
`$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
`$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
`$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
`$xml.LoadXml('<toast scenario="reminder"><visual><binding template="ToastGeneric"><text>$t</text><text>$b</text><text>$f</text></binding></visual><actions><action content="閉じる" arguments="dismiss" activationType="system"/></actions></toast>')
`$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(`$appId).Show([Windows.UI.Notifications.ToastNotification]::new(`$xml))
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($toastScript))
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -EncodedCommand $encoded 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "powershell.exe の終了コード $LASTEXITCODE" }
}
