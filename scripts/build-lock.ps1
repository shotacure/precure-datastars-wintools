<#
.SYNOPSIS
  定時のスクリプトのビルドを 1 つずつにする関数 Invoke-WithBuildLock を定める。ドットソースで読み込んで使う。

.DESCRIPTION
  定時のスクリプトはどれも始めに Release でビルドし、同じプロジェクト（PrecureDataStars.Data など）の obj を書く。
  同じ時刻に始まるタスクどうしでビルドが重なると、片方が DLL を開けずに失敗する。
  名前付きの mutex で、ビルドの間だけ鍵を掛ける。後から来たほうは先のビルドが終わるまで最大 10 分待つ。
  先の処理が鍵を返さずに落ちていた場合は、そのまま引き継ぐ。

.EXAMPLE
  . (Join-Path $PSScriptRoot 'build-lock.ps1')
  Invoke-WithBuildLock { & dotnet build $project -c Release --nologo -v q }
  if ($LASTEXITCODE -ne 0) { throw 'ビルドが失敗しました。' }
#>

function Invoke-WithBuildLock([scriptblock]$Action) {
    $lock = [System.Threading.Mutex]::new($false, 'Global\precure-datastars-build')
    try {
        try {
            if (-not $lock.WaitOne([TimeSpan]::FromMinutes(10))) {
                throw 'ほかのビルドが 10 分たっても終わりません。中止します。'
            }
        } catch [System.Threading.AbandonedMutexException] {
            # 前のビルドが鍵を返さずに終わっていた。鍵はこちらに移っているので、そのまま続ける。
        }
        try { & $Action }
        finally { $lock.ReleaseMutex() }
    }
    finally { $lock.Dispose() }
}
