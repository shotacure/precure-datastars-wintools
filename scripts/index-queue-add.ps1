<#
.SYNOPSIS
  検索エンジンへ手で送る URL の順番待ちの一覧（index-queue.json）に URL を足す。

.DESCRIPTION
  precure.tv の本番デプロイ（SiteBuilder の Deploy.IndexQueue）が足すのと同じ一覧に、ほかの手順（precure.news の公開など）から URL を足す。
  - 送っていない同じ URL が一覧にあれば足さない。
  - 並びは SiteBuilder と同じ：送っていないものを「コラム・ニュース → シリーズ → エピソード → 人物・団体・キャラ → 役職 → 盤・曲 → そのほか」の段ごとに出た順、
    送ったもの（sentAt あり）は後ろに送った日時の新しい順。
  - 書くときは鍵のファイル（index-queue.lock）で順番を取り、別の名前に書いてから入れ替える。
  URL は後ろに並べる・-File に 1 行 1 本で書く・-Stdin を付けて標準入力に 1 行 1 本で流す、のどれでも渡せる（まとめて渡してよい）。
  -MarkSent を付けると、足す代わりに「送った」の印を付ける：渡した URL の送っていない項目の sentAt に今の時刻（-SentAt で日時を指定できる）を書く。
  一覧に送っていない項目が無い URL は、足してから印を付ける。一覧を手で書き換えると本番デプロイと書き合いになるので、印はこの道具で付ける。

.EXAMPLE
  pwsh -NoProfile -File scripts/index-queue-add.ps1 -Kind ニュース https://precure.news/2026/11/28/foo/ https://precure.news/2026/11/28/bar/
.EXAMPLE
  printf '%s\n' "$url1" "$url2" | pwsh -NoProfile -File scripts/index-queue-add.ps1 -Stdin -Kind ニュース -Reason 新規
.EXAMPLE
  pwsh -NoProfile -File scripts/index-queue-add.ps1 -File new-urls.txt
.EXAMPLE
  pwsh -NoProfile -File scripts/index-queue-add.ps1 -MarkSent -SentAt 2026-11-29T10:00:00+09:00 https://precure.tv/series/2026tv/43/
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    # 1 行 1 本の URL を書いたファイル。
    [string]$File = '',
    # 種類（空なら URL から決める：precure.news はニュース、precure.tv はパスから）。
    [string]$Kind = '',
    # 理由。
    [string]$Reason = '新規',
    # 一覧の置き場。
    [string]$QueueDirectory = (Join-Path $env:LOCALAPPDATA 'precure-datastars'),
    # 標準入力から 1 行 1 本の URL を読む（付けたときだけ読む。付けないと標準入力は見ない）。
    [switch]$Stdin,
    # 足す代わりに、渡した URL に「送った」の印（sentAt）を付ける。
    [switch]$MarkSent,
    # -MarkSent で書く送った日時（例 2026-11-29T10:00:00+09:00）。空なら今の時刻。
    [string]$SentAt = '',
    # 足す URL（絶対 URL）。名前を付けずに後ろへ空白区切りで並べる（pwsh -File で呼ぶときはカンマで並べない。1 本の文字として渡る）。
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Url = @()
)

$ErrorActionPreference = 'Stop'
# bash などから呼んだときに日本語の出力が化けないよう、出力を UTF-8 にする。
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# --- 渡された URL を集める（引数・ファイル・標準入力） ---
$urls = New-Object System.Collections.Generic.List[string]
foreach ($u in $Url) { if ("$u".Trim()) { $urls.Add("$u".Trim()) } }
if ($File) { foreach ($line in Get-Content -LiteralPath $File -Encoding UTF8) { if ("$line".Trim()) { $urls.Add("$line".Trim()) } } }
if ($Stdin) {
    while ($null -ne ($line = [Console]::In.ReadLine())) { if ($line.Trim()) { $urls.Add($line.Trim()) } }
}
if ($urls.Count -eq 0) { Write-Host 'Index queue      : 足す URL がありません'; exit 0 }
$sentAtText = (Get-Date).ToString("yyyy-MM-dd'T'HH:mm:sszzz")
if ($SentAt) {
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($SentAt, [ref]$parsed)) { throw "-SentAt の日時が読めません：$SentAt" }
    $sentAtText = $parsed.ToString("yyyy-MM-dd'T'HH:mm:sszzz")
}

# --- 段と種類（SiteBuilder の Deploy.IndexQueue.Classify と同じ表） ---
function Get-QueueClass([string]$absoluteUrl) {
    $uri = $null
    if (-not [Uri]::TryCreate($absoluteUrl, [UriKind]::Absolute, [ref]$uri)) { return @{ Tier = 6; Kind = 'そのほか' } }
    if ($uri.Host -like '*precure.news') { return @{ Tier = 0; Kind = 'ニュース' } }
    $p = $uri.AbsolutePath
    if ($p.StartsWith('/articles/')) { return @{ Tier = 0; Kind = 'コラム' } }
    if ($p -match '^/series/[^/]+/\d+/$') { return @{ Tier = 2; Kind = 'エピソード' } }
    if ($p -match '^/series/[^/]+/$') { return @{ Tier = 1; Kind = 'シリーズ' } }
    if ($p.StartsWith('/people/')) { return @{ Tier = 3; Kind = '人物' } }
    if ($p.StartsWith('/companies/')) { return @{ Tier = 3; Kind = '団体' } }
    if ($p.StartsWith('/units/')) { return @{ Tier = 3; Kind = 'ユニット' } }
    if ($p.StartsWith('/characters/')) { return @{ Tier = 3; Kind = 'キャラ' } }
    if ($p.StartsWith('/creators/roles/')) { return @{ Tier = 4; Kind = '役職' } }
    if ($p.StartsWith('/products/')) { return @{ Tier = 5; Kind = '盤' } }
    if ($p.StartsWith('/songs/')) { return @{ Tier = 5; Kind = '曲' } }
    return @{ Tier = 6; Kind = 'そのほか' }
}

New-Item -ItemType Directory -Force -Path $QueueDirectory | Out-Null
$queuePath = Join-Path $QueueDirectory 'index-queue.json'
$lockPath = Join-Path $QueueDirectory 'index-queue.lock'

# --- 鍵のファイルで順番を取る（最大 30 秒待つ） ---
$lock = $null
for ($i = 0; $i -lt 60 -and -not $lock; $i++) {
    try {
        $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch [System.IO.IOException] {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $lock) { throw '一覧の鍵を取れませんでした（ほかの手順が書いている途中です）。' }
try {
    $items = New-Object System.Collections.Generic.List[object]
    if (Test-Path -LiteralPath $queuePath) {
        $doc = Get-Content -LiteralPath $queuePath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($it in @($doc.items)) { if ($it) { $items.Add($it) } }
    }
    $unsent = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($it in $items) { if (-not $it.sentAt) { [void]$unsent.Add($it.url) } }

    $foundAt = (Get-Date).ToString("yyyy-MM-dd'T'HH:mm:sszzz")
    $added = 0
    $marked = 0
    foreach ($u in $urls) {
        if ($MarkSent) {
            # 送っていない項目に印を付ける。無ければ、足してから印を付ける。
            $target = $items | Where-Object { $_.url -eq $u -and -not $_.sentAt } | Select-Object -First 1
            if ($target) { $target.sentAt = $sentAtText; $marked++; continue }
        } elseif (-not $unsent.Add($u)) { continue }
        $class = Get-QueueClass $u
        $items.Add([pscustomobject][ordered]@{
            url = $u
            kind = $(if ($Kind) { $Kind } else { $class.Kind })
            reason = $Reason
            foundAt = $foundAt
            sentAt = $(if ($MarkSent) { $sentAtText } else { $null })
        })
        if ($MarkSent) { $marked++ } else { $added++ }
    }

    # 送った後 90 日を過ぎたものは消し、並べ直す。
    $keepFrom = (Get-Date).AddDays(-90)
    # 送った日時が読めないものは消さずに残す。
    $kept = $items | Where-Object {
        $sent = [DateTimeOffset]::MinValue
        -not $_.sentAt -or -not [DateTimeOffset]::TryParse("$($_.sentAt)", [ref]$sent) -or $sent -ge $keepFrom
    }
    $unsentItems = @($kept | Where-Object { -not $_.sentAt } | Sort-Object @{ Expression = { (Get-QueueClass $_.url).Tier } }, @{ Expression = { $_.foundAt } }, @{ Expression = { $_.url } })
    $sentItems = @($kept | Where-Object { $_.sentAt } | Sort-Object @{ Expression = { $_.sentAt }; Descending = $true }, @{ Expression = { $_.url } })
    $json = [pscustomobject]@{ items = @($unsentItems + $sentItems) } | ConvertTo-Json -Depth 5

    $temp = "$queuePath.tmp"
    [System.IO.File]::WriteAllText($temp, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temp -Destination $queuePath -Force
    if ($MarkSent) {
        Write-Host ("Index queue      : {0} 件に送った印（{1}）を付けた（未送信 {2} 件）" -f $marked, $sentAtText, $unsentItems.Count)
    } else {
        Write-Host ("Index queue      : {0} 件を足した（渡した {1} 件のうち、送っていない同じ URL を除く。未送信 {2} 件）" -f $added, $urls.Count, $unsentItems.Count)
    }
} finally {
    $lock.Dispose()
    Remove-Item -LiteralPath $lockPath -ErrorAction SilentlyContinue
}
