<#
.SYNOPSIS
  scheduled-shop-check.ps1（precure.tv の販売中の商品の定時の点検）を一定の間隔で動かすタスクを、
  タスクスケジューラに登録（または解除）する。

.DESCRIPTION
  現在のユーザーの「ログオン中だけ実行」のタスクとして登録する（管理者権限もパスワードの保存も要らない）。
  登録した時刻から、指定の間隔（既定 15 分）で動き続ける。その時刻に動けなかったとき（電源が切れていた・
  サインアウトしていた）は、次に使えるようになった時点で動く（StartWhenAvailable）。スリープからは起こさない。
  前の回がまだ動いているときは、その回を飛ばす（MultipleInstances IgnoreNew）。
  短い間隔で動くので、コンソールの窓が開かないよう conhost の --headless で動かす。
  結果は %LOCALAPPDATA%\precure-datastars\scheduled-shop-check.log に残る。同じ名前のタスクがあれば置き換える。

.PARAMETER TaskName
  タスク名。既定「precure-datastars shop check」。

.PARAMETER IntervalMinutes
  点検の間隔（分）。既定 15。

.PARAMETER Unregister
  タスクを解除する。

.PARAMETER RunNow
  登録したあと、すぐに 1 回動かす（状態が変わっていれば本番の枠のファイルが上げ直される）。

.EXAMPLE
  .\scripts\register-shop-check-task.ps1
.EXAMPLE
  .\scripts\register-shop-check-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'precure-datastars shop check',
    [ValidateRange(1, 1440)]
    [int]$IntervalMinutes = 15,
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
$script = Join-Path $PSScriptRoot 'scheduled-shop-check.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw "scheduled-shop-check.ps1 がありません: $script" }

# PowerShell 7（pwsh）があればそれを、無ければ Windows PowerShell を使う。
$pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
$shell = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell.exe).Source }

# conhost --headless は、コンソールの窓を作らずに中のコマンドを動かす（-WindowStyle Hidden は窓が一瞬開く）。
$conhost = Join-Path $env:SystemRoot 'System32\conhost.exe'
$action = New-ScheduledTaskAction -Execute $conhost `
    -Argument "--headless `"$shell`" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$script`"" `
    -WorkingDirectory $repoRoot
# 登録した時刻を起点に、間隔ごとに期限なく繰り返す。
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "precure.tv の販売中の商品の定時の点検。$IntervalMinutes 分おきに Amazon で売られているかを確かめ、変わっていたら枠のファイルだけを本番に上げ直す（scripts/scheduled-shop-check.ps1）" `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "登録しました: $TaskName" -ForegroundColor Green
Write-Host "  実行      : $shell -File `"$script`""
Write-Host "  間隔      : $IntervalMinutes 分おき（スリープからは起こさない。動けなかったときは次に使えるとき）"
Write-Host "  次回      : $($info.NextRunTime)"
Write-Host "  記録      : $(Join-Path $env:LOCALAPPDATA 'precure-datastars\scheduled-shop-check.log')"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "  いま 1 回動かしました。結果は上の記録を見てください。"
}
