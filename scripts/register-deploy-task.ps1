<#
.SYNOPSIS
  scheduled-deploy.ps1（precure.tv の定時の本番デプロイ）を毎週水曜・土曜の 8:00 と 8:30 に動かすタスクをタスクスケジューラに登録（または解除）する。

.DESCRIPTION
  現在のユーザーの「ログオン中だけ実行」のタスクとして登録する（管理者権限もパスワードの保存も要らない）。
  スリープ中ならその時刻に起こして動かす（WakeToRun）。その時刻に動けなかったとき（電源が切れていた・
  サインアウトしていた）は、次に使えるようになった時点で動く（StartWhenAvailable）。
  画面には出さず（-WindowStyle Hidden）、結果は %LOCALAPPDATA%\precure-datastars\scheduled-deploy.log に残る。
  同じ名前のタスクがあれば置き換える。

  8:00 の回は下準備で、前回のデプロイからのコードの変更・DB の更新をあらかじめ本番に流しておく
  （コードを変えた後の最初のビルドは OGP カードを描き直すので数分かかる）。公開日時が 8:30 のコラムは
  まだ出ない。8:30 の回はその作り置きを使うので、差分がほぼコラムだけになり短時間で終わる。
  8:00 の回が安全ゲートなどで止まったときは通知が出るので、8:30 までに直すか判断できる。
  8:00 の回が 8:30 を過ぎても動いているときは、8:30 の回はそれが終わるのを待ってから動く（MultipleInstances Queue）。

.PARAMETER TaskName
  タスク名。既定「precure-datastars scheduled deploy」。

.PARAMETER DaysOfWeek
  実行する曜日。既定は水曜と土曜（コラムの公開日）。

.PARAMETER At
  コラムを公開する回の実行時刻。既定 08:30（コラムの公開時刻）。

.PARAMETER PrepareAt
  下準備の回の実行時刻。既定 08:00。空文字にすると下準備の回を登録しない。

.PARAMETER Unregister
  タスクを解除する。

.PARAMETER RunNow
  登録したあと、すぐに 1 回動かす（本番に公開されるので注意）。

.EXAMPLE
  .\scripts\register-deploy-task.ps1
.EXAMPLE
  .\scripts\register-deploy-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'precure-datastars scheduled deploy',
    [System.DayOfWeek[]]$DaysOfWeek = @([System.DayOfWeek]::Wednesday, [System.DayOfWeek]::Saturday),
    [string]$At = '08:30',
    [string]$PrepareAt = '08:00',
    [switch]$Unregister,
    [switch]$RunNow
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($Unregister) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "解除しました: $TaskName" -ForegroundColor Green
    return
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot 'scheduled-deploy.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw "scheduled-deploy.ps1 がありません: $script" }

# PowerShell 7（pwsh）があればそれを、無ければ Windows PowerShell を使う。
$pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
$shell = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell.exe).Source }

$action = New-ScheduledTaskAction -Execute $shell `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`"" `
    -WorkingDirectory $repoRoot
$triggers = @()
if ($PrepareAt) { $triggers += New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DaysOfWeek -At $PrepareAt }
$triggers += New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DaysOfWeek -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances Queue
# 表示用の曜日名（日本語）
$dayNames = @{ Sunday = '日曜'; Monday = '月曜'; Tuesday = '火曜'; Wednesday = '水曜'; Thursday = '木曜'; Friday = '金曜'; Saturday = '土曜' }
$daysLabel = ($DaysOfWeek | Sort-Object | ForEach-Object { $dayNames[$_.ToString()] }) -join '・'
$timesLabel = if ($PrepareAt) { "$PrepareAt（下準備）と $At" } else { $At }
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings -Principal $principal `
    -Description "precure.tv の定時の本番デプロイ。$daysLabel $timesLabel に動き、$At 公開のコラムをこれで出す（scripts/scheduled-deploy.ps1）" `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "登録しました: $TaskName" -ForegroundColor Green
Write-Host "  実行      : $shell -File `"$script`""
Write-Host "  時刻      : 毎週$daysLabel $timesLabel（スリープなら起こす。動けなかったときは次に使えるとき）"
Write-Host "  次回      : $($info.NextRunTime)"
Write-Host "  記録      : $(Join-Path $env:LOCALAPPDATA 'precure-datastars\scheduled-deploy.log')"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "  いま 1 回動かしました（本番に公開されます）。結果は上の記録を見てください。"
}
