<#
.SYNOPSIS
  precure.tv 本番サイトの「ドライラン → 安全確認 → 反映」を 1 コマンドで実行するデプロイスクリプト。

.DESCRIPTION
  PrecureDataStars.SiteBuilder を本番モード（--production --deploy）でビルドし、生成物を S3 へ差分同期
  したうえで CloudFront を invalidation する。

  ビルドの前に PrecureDataStars.ArtTrackRefresh を流し、割り当て済みの配信音源（YouTube アートトラック）を
  YouTube Data API で確かめ直す（YouTube API Services Developer Policies III.E.4 の 30 日以内の取り直し）。
  サイトは確認から 30 日を過ぎた音源に再生ボタンを出さず、ビルド警告にもなるため、ここで毎回新しくしておく。
  再確認に失敗したとき（API キー・接続の不備、消えた動画が多すぎて止めたときなど）はデプロイしない。
  -DryRunOnly でも再確認は流す（ドライランの差分を、本番反映と同じ状態の DB で見るため）。

  続けて PrecureDataStars.AmazonSync（--target all）を流し、取得から 20 時間を過ぎた商品・書籍の画像 URL を
  Creators API で取り直す（Amazon アソシエイトの Creators API 使用要件で、画像のリンクの保存は最長 24 時間）。
  失敗したとき（キー・接続文字列の不備など）はデプロイしない。-DryRunOnly でも流す。

  実行は 2 段階：

    1. まず --dry-run で差分プラン（upload / delete / unchanged）とビルド警告数を取得し、安全ゲートを通す。
         - delete（既存 S3 オブジェクトの消去）は、DB で消した・改名したものに伴う削除として通し、件数と中身をログに出す。
           止めるのは、OGP カード（og/ の下）を除いた削除が 100 件（$MassDeleteLimit）を超えたときだけ（DB の読み込みの空振りや
           生成の不具合で、ページがまとめて消えた出力を本番に流さないため）。意図した大量の削除なら -Force。
         - ビルド警告（Warnings）が 1 件以上 … 品質ゲートとして既定で中止する（-Force で続行）。
    2. ゲートを通過（大量の削除が無く Warnings=0、または -Force）した場合のみ、本番反映（--yes）を実行する。

  非対話実行のため本番反映は常に --yes（削除確認の y/N 省略）で走る。安全性は事前ドライランの
  ゲートで担保する。バケット名・Distribution ID・AWS プロファイル等の実値は App.config
  （gitignore 済）から読まれる。

.PARAMETER DryRunOnly
  ドライラン（差分プレビュー）のみ実行し、本番反映は行わない。

.PARAMETER Force
  安全ゲート（大量の削除 / Warnings>0）を無視して反映を強行する。ドライランで内容を確認済みの
  大量の削除（orphan のまとめての掃除など）を意図的に流すときだけ使う。

.EXAMPLE
  .\scripts\deploy.ps1
  ドライランで差分を確認し、安全（大量の削除なし・警告0）なら本番反映まで実行する。

.EXAMPLE
  .\scripts\deploy.ps1 -DryRunOnly
  差分プレビューだけ見る（反映しない）。

.EXAMPLE
  .\scripts\deploy.ps1 -Force
  大量の削除を伴う差分でも反映する（内容を確認済みの orphan のまとめての掃除など）。
#>
[CmdletBinding()]
param(
    [switch]$DryRunOnly,
    [switch]$Force
)

# cmdlet（Test-Path 等）の失敗は即停止。native（dotnet）の失敗は $LASTEXITCODE で個別に判定する。
$ErrorActionPreference = 'Stop'

# SiteBuilder（と dotnet CLI）は UTF-8 で出力するので、パイプで受けるときの読み取りも UTF-8 にそろえる。
# 既定の CP932 のままだと、Tee-Object で流す行と変数に取り込んだ行（Plan / Warnings の解析対象）が化ける。
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# スクリプトの 1 つ上がリポジトリルート。どこから呼んでもルート基準で動かす。
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$project = 'PrecureDataStars.SiteBuilder'

# OGP カードを除いた削除がこの件数を超えたら、生成の不具合を疑って反映を止める。
$MassDeleteLimit = 100

Write-Host ""
Write-Host "=== precure.tv 本番デプロイ ===" -ForegroundColor Cyan
Write-Host ""

# --- 同時実行の鍵 ---
# 手動のデプロイと定時のデプロイ（scheduled-deploy.ps1）が同時に走らないよう、名前付きの mutex で 1 つずつにする。
# 後から来たほうは、先のデプロイが終わるまで最大 2 時間待つ。先の処理が鍵を返さずに落ちていた場合は、そのまま引き継ぐ。
$deployLock = [System.Threading.Mutex]::new($false, 'Global\precure-tv-deploy')
try {
    if (-not $deployLock.WaitOne(0)) {
        Write-Host "  ほかのデプロイが動いています。終わるのを待ちます…" -ForegroundColor DarkYellow
        if (-not $deployLock.WaitOne([TimeSpan]::FromHours(2))) { throw "ほかのデプロイが 2 時間たっても終わりません。中止します。" }
    }
} catch [System.Threading.AbandonedMutexException] {
    Write-Host "  前のデプロイが鍵を返さずに終わっていました。引き継いで続けます。" -ForegroundColor DarkYellow
}

try {

    # --- 準備: 配信音源の再確認 ---
    # 出力はそのままコンソールへ流す（変化のあった音源・AMBIGUOUS に戻した商品を後から読めるように）。
    Write-Host "[準備] 配信音源の再確認" -ForegroundColor Yellow
    & dotnet run --project 'PrecureDataStars.ArtTrackRefresh'
    if ($LASTEXITCODE -ne 0) {
        throw "配信音源の再確認が失敗しました (exit $LASTEXITCODE)。上のログを確認してください。"
    }
    Write-Host ""

    # --- 準備: Amazon の画像の取り直し ---
    # Creators API 使用要件は画像のリンクの保存を最長 24 時間に限るので、取得から 20 時間を過ぎた
    # 商品・書籍の画像 URL を取り直してからビルドする（毎日の定時デプロイで全件が入れ替わる）。
    Write-Host "[準備] Amazon の画像の取り直し" -ForegroundColor Yellow
    & dotnet run --project 'PrecureDataStars.AmazonSync' -- --target all
    if ($LASTEXITCODE -ne 0) {
        throw "Amazon の画像の取り直しが失敗しました (exit $LASTEXITCODE)。上のログを確認してください。"
    }
    Write-Host ""

    # --- フェーズ 1: ドライラン（差分プレビュー） ---
    # stdout を Tee-Object で変数にも取り込みつつコンソールへ流す（2>&1 は使わない＝
    # native の stderr を success ストリームへ混ぜると ErrorActionPreference=Stop で
    # 途中停止する PowerShell の落とし穴を避けるため。Plan/Warnings 行は stdout に出る）。
    Write-Host "[1/2] ドライラン（差分プレビュー）" -ForegroundColor Yellow
    & dotnet run --project $project -- --production --deploy --dry-run | Tee-Object -Variable dryOutput
    if ($LASTEXITCODE -ne 0) {
        throw "ドライラン（ビルド）が失敗しました (exit $LASTEXITCODE)。上のログを確認してください。"
    }
    $dryText = ($dryOutput | Out-String)

    # 差分プラン行を解析する。例: "  Plan             : upload 46 / delete 0 / unchanged 3079"
    $planMatch = [regex]::Match($dryText, 'Plan\s*:\s*upload\s+(\d+)\s*/\s*delete\s+(\d+)\s*/\s*unchanged\s+(\d+)')
    if (-not $planMatch.Success) {
        throw "ドライランの出力から差分プラン（Plan: upload .. / delete .. / unchanged ..）を読み取れませんでした。BaseUrl 未設定などでデプロイがスキップされた可能性があります。"
    }
    $uploadCount    = [int]$planMatch.Groups[1].Value
    $deleteCount    = [int]$planMatch.Groups[2].Value
    $unchangedCount = [int]$planMatch.Groups[3].Value

    # ビルド警告数を解析する。例: "  Warnings         : 0"
    $warnMatch = [regex]::Match($dryText, '(?m)^\s*Warnings\s*:\s*(\d+)\s*$')
    $warnCount = if ($warnMatch.Success) { [int]$warnMatch.Groups[1].Value } else { -1 }
    $warnDisplay = if ($warnCount -lt 0) { '不明' } else { "$warnCount" }

    Write-Host ""
    Write-Host ("  差分: upload {0} / delete {1} / unchanged {2} / 警告 {3}" -f $uploadCount, $deleteCount, $unchangedCount, $warnDisplay) -ForegroundColor Cyan

    # --- 削除の数え上げ ---
    # ドライランの「Delete (orphan) targets:」に続く「  - {S3 キー}」の行を読み、OGP カード（og/ の下。ページに
    # 伴って消える）を除いた件数を、大量の削除のゲートに使う。
    $deleteKeys = @()
    $inDeleteList = $false
    foreach ($line in $dryOutput) {
        $text = "$line"
        if ($text -match 'Delete \(orphan\) targets:') { $inDeleteList = $true; continue }
        if ($inDeleteList) {
            if ($text -match '^\s*-\s+(.+?)\s*$') { $deleteKeys += $Matches[1] } else { $inDeleteList = $false }
        }
    }

    $pageDeleteCount = @($deleteKeys | Where-Object { $_ -notlike 'og/*' }).Count
    # 一覧の行数が Plan の件数と合わないとき（出力の形が変わったなど）は、OGP カードを除かず Plan の件数で数える。
    if ($deleteKeys.Count -ne $deleteCount) {
        Write-Host "  削除の一覧を読み取れませんでした（Plan $deleteCount 件 / 一覧 $($deleteKeys.Count) 件）。Plan の件数でゲートを判定します。" -ForegroundColor DarkYellow
        $pageDeleteCount = $deleteCount
    }
    if ($deleteCount -gt 0) {
        Write-Host ""
        Write-Host "  削除 $deleteCount 件（うち OGP カード以外 $pageDeleteCount 件。$MassDeleteLimit 件までは止めない）" -ForegroundColor DarkCyan
    }

    if ($DryRunOnly) {
        Write-Host ""
        Write-Host "  -DryRunOnly のため、反映は行いません。" -ForegroundColor DarkGray
        return
    }

    # 反映する変更が無ければ 2 度目のビルドを省いて終了。
    if ($uploadCount -eq 0 -and $deleteCount -eq 0) {
        Write-Host ""
        Write-Host "  差分なし（upload 0 / delete 0）。反映する変更はありません。" -ForegroundColor Green
        return
    }

    # --- 安全ゲート ---
    $blockReasons = @()
    if ($pageDeleteCount -gt $MassDeleteLimit) {
        $blockReasons += "大量の削除 $pageDeleteCount 件（OGP カード以外。上限 $MassDeleteLimit 件。生成の不具合でページがまとめて消えていないか確認）"
    }
    if ($warnCount -gt 0)   { $blockReasons += "ビルド警告 $warnCount 件" }

    if ($blockReasons.Count -gt 0 -and -not $Force) {
        Write-Host ""
        Write-Host "!!! 安全ゲートにより反映を中止しました:" -ForegroundColor Red
        $blockReasons | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
        Write-Host "    内容を確認し、意図的なら -Force を付けて再実行してください。" -ForegroundColor Red
        throw "Deploy aborted by safety gate（削除/警告あり）。"
    }
    if ($blockReasons.Count -gt 0 -and $Force) {
        Write-Host ""
        Write-Host "  -Force 指定: 安全ゲート（$($blockReasons -join ' / ')）を無視して反映します。" -ForegroundColor Magenta
    }

    # --- フェーズ 2: 本番反映（--yes） ---
    Write-Host ""
    Write-Host "[2/2] 本番反映（--production --deploy --yes）" -ForegroundColor Yellow
    & dotnet run --project $project -- --production --deploy --yes
    if ($LASTEXITCODE -ne 0) {
        throw "本番反映が失敗しました (exit $LASTEXITCODE)。"
    }

    Write-Host ""
    Write-Host "Done — precure.tv に反映しました。" -ForegroundColor Green
    Write-Host ""
} finally {
    $deployLock.ReleaseMutex()
    $deployLock.Dispose()
}
