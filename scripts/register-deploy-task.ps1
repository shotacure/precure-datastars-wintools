<#
.SYNOPSIS
  scheduled-deploy.ps1（precure.tv の定時の本番デプロイ）を毎週土曜 8:30 に動かすタスクをタスクスケジューラに登録（または解除）する。

.DESCRIPTION
  現在のユーザーの「ログオン中だけ実行」のタスクとして登録する（管理者権限もパスワードの保存も要らない）。
  スリープ中ならその時刻に起こして動かす（WakeToRun）。その時刻に動けなかったとき（電源が切れていた・
  サインアウトしていた）は、次に使えるようになった時点で動く（StartWhenAvailable）。
  画面には出さず（-WindowStyle Hidden）、結果は %LOCALAPPDATA%\precure-datastars\scheduled-deploy.log に残る。
  同じ名前のタスクがあれば置き換える。

.PARAMETER TaskName
  タスク名。既定「precure-datastars scheduled deploy」。

.PARAMETER At
  毎週土曜の実行時刻。既定 08:30（コラムの公開時刻）。

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
    [string]$At = '08:30',
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
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Saturday -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'precure.tv の定時の本番デプロイ。土曜 8:30 公開のコラムをこれで出す（scripts/scheduled-deploy.ps1）' `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "登録しました: $TaskName" -ForegroundColor Green
Write-Host "  実行      : $shell -File `"$script`""
Write-Host "  時刻      : 毎週土曜 $At（スリープなら起こす。動けなかったときは次に使えるとき）"
Write-Host "  次回      : $($info.NextRunTime)"
Write-Host "  記録      : $(Join-Path $env:LOCALAPPDATA 'precure-datastars\scheduled-deploy.log')"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "  いま 1 回動かしました（本番に公開されます）。結果は上の記録を見てください。"
}
