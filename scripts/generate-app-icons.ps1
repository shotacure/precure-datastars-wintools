<#
.SYNOPSIS
  配布対象 6 アプリの ApplicationIcon (.ico) をプログラムで生成するスクリプト。

.DESCRIPTION
  各アプリに 2 文字略字 + 背景色を組み合わせた角丸正方形アイコンを生成して、
  対応するプロジェクトディレクトリ直下に `app.ico` として保存する。
  マルチサイズ (16/32/48/64/128/256 px) の PNG-embedded ICO で、
  各サイズで Segoe UI Bold の白文字を中央配置する。

  CLAUDE.md の方針：このスクリプト自体はリポジトリに含めるが、
  生成済み app.ico もコミット対象とする（再生成は色やフォントの調整時のみ）。

.EXAMPLE
  .\scripts\generate-app-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 配布対象 6 アプリの (略字, 背景色, csproj ディレクトリ) 一覧。
$apps = @(
    @{ Letters = 'EP'; Color = '#FF8C00'; Project = 'PrecureDataStars.Episodes'    }
    @{ Letters = 'SB'; Color = '#1E88E5'; Project = 'PrecureDataStars.SiteBuilder' }
    @{ Letters = 'CL'; Color = '#43A047'; Project = 'PrecureDataStars.Catalog'     }
    @{ Letters = 'CD'; Color = '#8E24AA'; Project = 'PrecureDataStars.CDAnalyzer'  }
    @{ Letters = 'BD'; Color = '#E53935'; Project = 'PrecureDataStars.BDAnalyzer'  }
    @{ Letters = 'AS'; Color = '#00897B'; Project = 'PrecureDataStars.AmazonSync'  }
)

# ICO に格納するサイズ（小さい順）。Win11 タスクバー / エクスプローラ / 高 DPI 環境まで網羅。
$sizes = @(16, 32, 48, 64, 128, 256)

function New-IconBitmap {
    param(
        [int]$Size,
        [string]$Letters,
        [string]$ColorHex
    )
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    # 透過背景で初期化
    $g.Clear([System.Drawing.Color]::Transparent)

    # 角丸正方形パス（半径は辺長の 18%）
    $radius = [Math]::Max(2, [int]($Size * 0.18))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($rect.X,                         $rect.Y,                          $diameter, $diameter, 180, 90)
    $path.AddArc($rect.X + $rect.Width - $diameter, $rect.Y,                        $diameter, $diameter, 270, 90)
    $path.AddArc($rect.X + $rect.Width - $diameter, $rect.Y + $rect.Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X,                           $rect.Y + $rect.Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    # 背景塗りつぶし
    $bgColor = [System.Drawing.ColorTranslator]::FromHtml($ColorHex)
    $bgBrush = New-Object System.Drawing.SolidBrush($bgColor)
    $g.FillPath($bgBrush, $path)

    # 文字描画（フォントサイズはアイコンサイズの 45%）
    # 16px 等の極小サイズではフォントレンダリングが破綻しやすいので、最低サイズの場合は
    # GraphicsUnit.Pixel で直接ピクセル指定して安定させる。
    $fontSizePx = [Math]::Max(6, [int]($Size * 0.45))
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSizePx, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($Letters, $font, $textBrush, $rect, $format)

    $bgBrush.Dispose()
    $textBrush.Dispose()
    $font.Dispose()
    $format.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-MultiSizeIco {
    param(
        [string]$OutputPath,
        [string]$Letters,
        [string]$ColorHex,
        [int[]]$Sizes
    )

    # 各サイズの PNG バイト列を準備
    $pngBuffers = @()
    foreach ($s in $Sizes) {
        $bmp = New-IconBitmap -Size $s -Letters $Letters -ColorHex $ColorHex
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngBuffers += ,$ms.ToArray()
    }

    # ICO バイナリフォーマット書き出し
    # ICONDIR (6 bytes): Reserved=0, Type=1, Count=$Sizes.Count
    # ICONDIRENTRY (16 bytes each): width, height, colorCount=0, reserved=0, planes=1, bitsPerPixel=32, sizeInBytes, dataOffset
    # 各エントリの後ろに PNG データ
    $fs = [System.IO.File]::Create($OutputPath)
    try {
        $bw = New-Object System.IO.BinaryWriter($fs)
        try {
            $bw.Write([UInt16]0)                  # Reserved
            $bw.Write([UInt16]1)                  # Type = 1 (icon)
            $bw.Write([UInt16]$Sizes.Count)       # Count

            $offset = 6 + (16 * $Sizes.Count)
            for ($i = 0; $i -lt $Sizes.Count; $i++) {
                $sz = $Sizes[$i]
                $len = $pngBuffers[$i].Length
                # 256 以上は width/height とも 0 を書く仕様
                $w = if ($sz -ge 256) { 0 } else { $sz }
                $h = if ($sz -ge 256) { 0 } else { $sz }
                $bw.Write([byte]$w)
                $bw.Write([byte]$h)
                $bw.Write([byte]0)                # ColorCount
                $bw.Write([byte]0)                # Reserved
                $bw.Write([UInt16]1)              # Planes
                $bw.Write([UInt16]32)             # BitsPerPixel
                $bw.Write([UInt32]$len)           # Bytes in resource
                $bw.Write([UInt32]$offset)        # Image data offset
                $offset += $len
            }
            for ($i = 0; $i -lt $Sizes.Count; $i++) {
                $bw.Write($pngBuffers[$i])
            }
        }
        finally {
            $bw.Flush()
            # BinaryWriter Dispose は内部の Stream も閉じるので、ここでは Stream を別途閉じない。
            $bw.Dispose()
        }
    }
    finally {
        # BinaryWriter で既に閉じられているが、念のため。
        if ($fs.CanWrite) { $fs.Dispose() }
    }
}

foreach ($app in $apps) {
    $output = Join-Path $repoRoot ($app.Project + '\app.ico')
    Save-MultiSizeIco -OutputPath $output -Letters $app.Letters -ColorHex $app.Color -Sizes $sizes
    Write-Host ("Generated: {0,-40} {1}  {2}" -f $output, $app.Letters, $app.Color)
}

Write-Host ''
Write-Host 'All app icons generated.' -ForegroundColor Green
