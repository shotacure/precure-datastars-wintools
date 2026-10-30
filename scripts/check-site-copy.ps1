<#
.SYNOPSIS
  生成したサイトの文章を、サイトの文章の決まり（Skill site-copy）に照らして点検する。

.DESCRIPTION
  出力フォルダの HTML を読み、次を数えて知らせる。止めはしない（知らせるだけ）。
  - 冒頭文（<p class="lead">）の結びの重なり：一覧・入口（階層 2 段まで）で、最後の文の終わり（「〜確かめられます。」など）が
    -RepeatLimit 本以上の別々の文で同じなら知らせる。一覧・入口の冒頭文は OGP カードのリード文と同じ文なので、カードの点検も兼ねる
  - 冒頭文・meta description の書き方：問いかけ（？）、ダッシュ（——）、飾りの引用符（“”）、
    「〜してみてください」「〜かもしれません」「すぐ見つかります」などの煽り・誘い
  - 内部用語（本名義・表示名義・最新名義・系譜の代表）：本文・属性・配信する JS / CSS のどこにも出さない
  運営情報の 4 ページ（about・privacy・disclaimer・contact）と案内のページ（music/playback・404）は、問いかけ・誘いの点検から外す。

.PARAMETER SiteDir
  点検する出力フォルダ。省略時は SiteBuilder の App.config の SiteOutputDir（本番の出力先）。

.PARAMETER RepeatLimit
  同じ結びを何ページで使ったら知らせるか。既定 3。

.EXAMPLE
  pwsh -NoProfile -File scripts/check-site-copy.ps1
  pwsh -NoProfile -File scripts/check-site-copy.ps1 -SiteDir <テスト出力のフォルダ>
#>
[CmdletBinding()]
param(
    [string]$SiteDir = "",
    [int]$RepeatLimit = 3
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $SiteDir) {
    $configPath = Join-Path $repoRoot 'PrecureDataStars.SiteBuilder/App.config'
    if (-not (Test-Path $configPath)) { throw "App.config が見つかりません。-SiteDir を指定してください。" }
    [xml]$config = Get-Content -Raw -Encoding UTF8 $configPath
    $SiteDir = ($config.configuration.appSettings.add | Where-Object { $_.key -eq 'SiteOutputDir' }).value
    if (-not $SiteDir) { throw "App.config に SiteOutputDir がありません。-SiteDir を指定してください。" }
}
if (-not (Test-Path $SiteDir)) { throw "出力フォルダがありません: $SiteDir" }

# 問いかけ・誘いの点検から外すページ（運営情報と案内。問い合わせの文面などで自然に使う）
$exemptPages = @('about', 'privacy', 'disclaimer', 'contact', 'music/playback', '404.html', 'og-gallery')

# 冒頭文・meta description で使わない書き方（Skill site-copy）
$bannedPatterns = [ordered]@{
    '問いかけ（？）'         = '[？?](?=[」』]?\s*$|[」』]?\s)'
    'ダッシュ（——）'         = '——'
    '飾りの引用符（“”）'     = '[“”]'
    '誘い（〜てみてください）' = 'てみてください'
    '推量（かもしれません）'   = 'かもしれません'
    '煽り（すぐ見つかります・ひと目で分かります）' = 'すぐ見つかり|ひと目で分かり|大解剖|楽しめます'
}

# 外に出さない内部用語（本文・属性・JS / CSS のどこでも）
$internalTerms = @('本名義', '表示名義', '最新名義', '系譜の代表')

function Get-PagePath([string]$file) {
    $rel = [System.IO.Path]::GetRelativePath($SiteDir, $file).Replace('\', '/')
    return ($rel -replace '/?index\.html$', '')
}

function ConvertFrom-HtmlText([string]$html) {
    $t = [regex]::Replace($html, '<[^>]+>', '')
    $t = [System.Net.WebUtility]::HtmlDecode($t)
    return ([regex]::Replace($t, '\s+', ' ')).Trim()
}

Write-Host "=== サイトの文章の点検: $SiteDir ===" -ForegroundColor Cyan

$leadRegex = [regex]'(?s)<p class="lead">(.*?)</p>'
$metaRegex = [regex]'<meta name="description" content="([^"]*)"'
$endings = @{}
$problems = New-Object System.Collections.Generic.List[string]

# 冒頭文を持つページ（一覧・入口・統計）だけを読む。個別ページの文は型から作るので、結びの重なりは数えない。
$leadFiles = Get-ChildItem -Path $SiteDir -Recurse -Filter 'index.html' -File |
    Where-Object { $_.FullName -notmatch '[\\/](og|assets)[\\/]' } |
    Select-String -Pattern '<p class="lead">' -List |
    Select-Object -ExpandProperty Path

foreach ($file in $leadFiles) {
    $html = Get-Content -Raw -Encoding UTF8 $file
    $page = Get-PagePath $file
    $exempt = $exemptPages | Where-Object { $page -eq $_ -or $page.StartsWith("$_/") }

    $texts = @()
    foreach ($m in $leadRegex.Matches($html)) {
        $lead = ConvertFrom-HtmlText $m.Groups[1].Value
        if (-not $lead) { continue }
        $texts += ,@('冒頭文', $lead)
        # 結び：最後の文の、最後の読点より後ろ（「〜確かめられます。」）。一覧・入口（階層 2 段まで）の「内容＋使い道」の 2 文だけを数え、
        # 同じ型から作る文（記念日の 366 日など）は 1 つに数えるため、ページではなく別々の文の数で数える。
        $sentences = $lead -split '(?<=。)' | Where-Object { $_.Trim() }
        $depth = if ($page) { ($page -split '/').Count } else { 0 }
        if ($sentences.Count -ge 2 -and $depth -le 2) {
            $last = $sentences[-1].Trim()
            $tail = ($last -split '、')[-1]
            $key = if ($tail.Length -gt 12) { $tail.Substring($tail.Length - 12) } else { $tail }
            if (-not $endings.ContainsKey($key)) { $endings[$key] = @{} }
            if (-not $endings[$key].ContainsKey($lead)) { $endings[$key][$lead] = "/$page/" }
        }
    }
    $meta = $metaRegex.Match($html)
    if ($meta.Success) { $texts += ,@('meta', [System.Net.WebUtility]::HtmlDecode($meta.Groups[1].Value)) }

    if (-not $exempt) {
        foreach ($t in $texts) {
            foreach ($name in $bannedPatterns.Keys) {
                if ($t[1] -match $bannedPatterns[$name]) {
                    $problems.Add("$name  /$page/ の$($t[0])：$($t[1].Substring(0, [Math]::Min(60, $t[1].Length)))…")
                }
            }
        }
    }
}

# 結びの重なり
$repeats = $endings.GetEnumerator() | Where-Object { $_.Value.Count -ge $RepeatLimit } | Sort-Object { $_.Value.Count } -Descending
foreach ($r in $repeats) {
    $problems.Add("同じ結び「…$($r.Key)」が $($r.Value.Count) 文：$(($r.Value.Values | Select-Object -First 6) -join '、')")
}

# 内部用語（HTML・JS・CSS 全部）
$internalHits = Get-ChildItem -Path $SiteDir -Recurse -File -Include '*.html', '*.js', '*.css' |
    Where-Object { $_.FullName -notmatch '[\\/]og-gallery[\\/]' } |
    Select-String -Pattern ($internalTerms -join '|') -List
foreach ($h in $internalHits) {
    $problems.Add("内部用語「$($h.Matches[0].Value)」  /$(Get-PagePath $h.Path)")
}

Write-Host "  冒頭文のあるページ: $($leadFiles.Count)"
if ($problems.Count -eq 0) {
    Write-Host "  指摘なし" -ForegroundColor Green
} else {
    Write-Host "  指摘 $($problems.Count) 件" -ForegroundColor Yellow
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Yellow }
}
Write-Output ("CopyCheck: {0}" -f $problems.Count)
