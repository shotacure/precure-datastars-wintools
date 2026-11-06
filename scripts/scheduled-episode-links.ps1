<#
.SYNOPSIS
  放送後のリンクの自動取り込み。PrecureDataStars.EpisodeLinkSync を Release でビルドして動かす。

.DESCRIPTION
  タスクスケジューラから動かす（登録は register-episode-links-task.ps1）。
  -Watch を付けると、今日放送の TV の回のあらすじのページと次の回の予告が出るまで 5 秒おきに見続ける（日曜 9:00 の回。12:00 まで）。
  付けなければ、前後数週の回のあらすじ・ラインナップ・予告を 1 回だけ見て回る（毎日 5:50 の回。6:00 の定時デプロイの前に入れておく）。
  見つけたものは episodes の空の列にだけ書く。サイトに出るのは次の本番デプロイ。

  経過はすべてログ（%LOCALAPPDATA%\precure-datastars\episode-links.log）に追記する。
  知らせること（予告のサブタイトルが DB と違う・その回が DB に無い・12:00 までに見つからなかった など）があったときと、
  失敗したときは Windows の通知（トースト）を出す。

.PARAMETER Watch
  今日の回のあらすじと次の回の予告が出るまで見続ける。

.PARAMETER DryRun
  DB に書かずに、見つけたものをログに出すだけにする。

.EXAMPLE
  .\scripts\scheduled-episode-links.ps1 -Watch
.EXAMPLE
  .\scripts\scheduled-episode-links.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$Watch,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repoRoot = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:LOCALAPPDATA 'precure-datastars'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir 'episode-links.log'
. (Join-Path $PSScriptRoot 'show-toast.ps1')
. (Join-Path $PSScriptRoot 'build-lock.ps1')

function Write-Log([string]$message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Show-Notice([string]$title, [string]$body) {
    try { Show-Toast -Title $title -Body $body -Foot "記録: $logPath" }
    catch { Write-Log "通知を出せませんでした：$($_.Exception.Message)" }
}

$mode = if ($Watch) { '見続ける' } else { '1 回' }
Write-Log "=== 放送後のリンクの取り込み（$mode）を始めます ==="
$exitCode = 1
$output = @()
try {
    $project = Join-Path $repoRoot 'PrecureDataStars.EpisodeLinkSync/PrecureDataStars.EpisodeLinkSync.csproj'
    # 同じ時刻に始まるほかの定時のタスクとビルドが重ならないよう、ビルドの間だけ鍵を掛ける。
    Invoke-WithBuildLock {
        & dotnet build $project -c Release --nologo -v q *>&1 | ForEach-Object { Add-Content -LiteralPath $logPath -Value "    $_" -Encoding UTF8 }
    }
    if ($LASTEXITCODE -ne 0) { throw "EpisodeLinkSync のビルドが失敗しました (exit $LASTEXITCODE)。" }
    $exe = Get-ChildItem -Path (Join-Path $repoRoot 'PrecureDataStars.EpisodeLinkSync/bin/Release') -Filter 'PrecureDataStars.EpisodeLinkSync.exe' -Recurse -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { throw 'EpisodeLinkSync の exe が見つかりません。' }

    $toolArgs = @()
    if ($Watch) { $toolArgs += '--watch' }
    if ($DryRun) { $toolArgs += '--dry-run' }
    Push-Location (Split-Path $exe.FullName)
    try {
        & $exe.FullName @toolArgs *>&1 | ForEach-Object {
            $output += "$_"
            Add-Content -LiteralPath $logPath -Value "    $_" -Encoding UTF8
            $_
        }
        $exitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
catch {
    $output += $_.Exception.Message
    Write-Log "失敗しました：$($_.Exception.Message)"
}
Write-Log "=== 放送後のリンクの取り込みを終えました（終了コード $exitCode） ==="

if ($exitCode -eq 3) {
    # 知らせることの一覧（「  - 」で始まる行）を通知の本文にする。
    $notices = @($output | Where-Object { $_ -like '  - *' } | ForEach-Object { $_.Substring(4) })
    Show-Notice "放送後のリンクの取り込み：知らせることが $($notices.Count) 件" ($notices -join "`n")
}
elseif ($exitCode -ne 0) {
    $last = ($output | Select-Object -Last 1)
    Show-Notice '放送後のリンクの取り込みが失敗しました' ("{0}　{1}" -f (Get-Date -Format 'M/d HH:mm'), $last)
    exit 1
}
