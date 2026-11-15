<#
.SYNOPSIS
  scheduled-episode-links.ps1（放送後のリンクの自動取り込み）を動かすタスクをタスクスケジューラに登録（または解除）する。

.DESCRIPTION
  2 つのタスクを登録する。どちらも現在のユーザーの「ログオン中だけ実行」（管理者権限もパスワードの保存も要らない）で、
  スリープ中ならその時刻に起こして動かし（WakeToRun）、動けなかったときは次に使えるようになった時点で動く（StartWhenAvailable）。
  画面には出さず（-WindowStyle Hidden）、結果は %LOCALAPPDATA%\precure-datastars\episode-links.log に残る。
  同じ名前のタスクがあれば置き換える。

  - 「precure-datastars episode links watch」：毎週日曜 9:00。今日放送の回のあらすじのページと次の回の予告が出るまで
    5 秒おきに見続け、そろったら終わる（12:00 まで）。タスクスケジューラは 1 分より短い間隔で繰り返せないので、
    1 回起動したプロセスの中で見続ける。
  - 「precure-datastars episode links daily」：毎日 5:50。前後数週の回のあらすじ・ラインナップ・予告を 1 回見て回る
    （ラインナップは放送から数日遅れで出るため。6:00 の定時デプロイの前に入れておく）。

.PARAMETER WatchAt
  日曜の見続ける回の開始時刻。既定 09:00（あらすじのページと予告が出る時刻）。

.PARAMETER DailyAt
  毎日 1 回見て回る時刻。既定 05:50。

.PARAMETER Unregister
  2 つのタスクを解除する。

.EXAMPLE
  .\scripts\register-episode-links-task.ps1
.EXAMPLE
  .\scripts\register-episode-links-task.ps1 -Unregister
#>
[CmdletBinding()]
param(
    [string]$WatchAt = '09:00',
    [string]$DailyAt = '05:50',
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$watchTask = 'precure-datastars episode links watch'
$dailyTask = 'precure-datastars episode links daily'

if ($Unregister) {
    foreach ($name in @($watchTask, $dailyTask)) {
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "解除しました: $name" -ForegroundColor Green
    }
    return
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot 'scheduled-episode-links.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw "scheduled-episode-links.ps1 がありません: $script" }

# PowerShell 7（pwsh）があればそれを、無ければ Windows PowerShell を使う。
$pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
$shell = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell.exe).Source }
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited

function Register-LinkTask([string]$name, [string]$extraArgs, $trigger, [TimeSpan]$limit, [string]$description) {
    $action = New-ScheduledTaskAction -Execute $shell `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`" $extraArgs".TrimEnd() `
        -WorkingDirectory $repoRoot
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit $limit -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
        -Description $description -Force | Out-Null
    $info = Get-ScheduledTaskInfo -TaskName $name
    Write-Host "登録しました: $name（次回 $($info.NextRunTime)）" -ForegroundColor Green
}

Register-LinkTask $watchTask '-Watch' (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At $WatchAt) (New-TimeSpan -Hours 4) `
    "毎週日曜 $WatchAt に、今日放送の回のあらすじのページと次の回の予告が出るまで 5 秒おきに見続ける（scripts/scheduled-episode-links.ps1 -Watch）"
Register-LinkTask $dailyTask '' (New-ScheduledTaskTrigger -Daily -At $DailyAt) (New-TimeSpan -Minutes 30) `
    "毎日 $DailyAt に、前後数週の回のあらすじ・ラインナップ・予告を 1 回見て回る（scripts/scheduled-episode-links.ps1）"
Write-Host "  記録: $(Join-Path $env:LOCALAPPDATA 'precure-datastars\episode-links.log')"
