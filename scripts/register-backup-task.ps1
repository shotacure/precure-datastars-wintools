<#
.SYNOPSIS
  backup-db.ps1 を毎日 1 回動かすタスクをタスクスケジューラに登録（または解除）する。

.DESCRIPTION
  現在のユーザーの「ログオン中だけ実行」のタスクとして登録する（管理者権限もパスワードの保存も要らない）。
  その時刻に PC が起きていない・サインアウトしていたときは、次に使えるようになった時点で動く（StartWhenAvailable）。
  画面には出さず（-WindowStyle Hidden）、結果は保存先の backup.log に残る。
  同じ名前のタスクがあれば置き換える。

.PARAMETER TaskName
  タスク名。既定「precure-datastars DB backup」。

.PARAMETER At
  毎日の実行時刻。既定 04:00。

.PARAMETER Unregister
  タスクを解除する。

.PARAMETER RunNow
  登録したあと、すぐに 1 回動かす。

.EXAMPLE
  .\scripts\register-backup-task.ps1
.EXAMPLE
  .\scripts\register-backup-task.ps1 -At 03:30 -RunNow
.EXAMPLE
  .\scripts\register-backup-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'precure-datastars DB backup',
    [string]$At = '04:00',
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
$script = Join-Path $PSScriptRoot 'backup-db.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw "backup-db.ps1 がありません: $script" }

# PowerShell 7（pwsh）があればそれを、無ければ Windows PowerShell を使う。
$pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
$shell = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell.exe).Source }

$action = New-ScheduledTaskAction -Execute $shell `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`"" `
    -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'precure_datastars の mysqldump とローカル専用ファイルを D:\backup\precure-datastars に取り、Google ドライブへ写す（scripts/backup-db.ps1）' `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "登録しました: $TaskName" -ForegroundColor Green
Write-Host "  実行      : $shell -File `"$script`""
Write-Host "  時刻      : 毎日 $At（起きていなければ次に使えるとき）"
Write-Host "  次回      : $($info.NextRunTime)"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "  いま 1 回動かしました。結果は D:\backup\precure-datastars\backup.log を見てください。"
}
