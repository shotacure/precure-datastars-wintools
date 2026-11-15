using System.Security.Cryptography;
using PrecureDataStars.SiteBuilder.Utilities;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 画素まで描いた OGP カード 1 枚（<see cref="OgCardRenderer.Draw"/> の結果）。PNG にするのは <see cref="SavePng"/> を呼んだときだけ。
/// 作り置きは <see cref="PixelHash"/> が前と同じなら PNG を書き直さない。使い終えたら Dispose する。
/// </summary>
public sealed class OgCardDrawing : IDisposable
{
    private readonly SKSurface _surface;
    private readonly SKImage _image;

    internal OgCardDrawing(SKSurface surface, SKImage image, string? warning)
    {
        _surface = surface;
        _image = image;
        Warning = warning;
    }

    /// <summary>
    /// 描いたときの知らせ。指定の書体がこの PC に無く既定の書体で描いた警告（<see cref="OgCardRenderer.IsTypefaceWarning"/>）か、
    /// ワードマークにブランド書体で描けなかった文字。どちらも無ければ null。
    /// </summary>
    public string? Warning { get; }

    /// <summary>描いた画素（RGBA・乗算済みアルファ）のハッシュ（16 進 32 桁）。同じ絵なら同じ値になる。</summary>
    public string PixelHash()
    {
        using var pixmap = _image.PeekPixels()
            ?? throw new InvalidOperationException("カードの画素を読めませんでした。");
        return Convert.ToHexString(SHA256.HashData(pixmap.GetPixelSpan()))[..32].ToLowerInvariant();
    }

    /// <summary>PNG にして書き出す。</summary>
    /// <param name="outputFilePath">書き出し先の絶対パス。</param>
    public void SavePng(string outputFilePath)
    {
        PathUtil.EnsureParentDirectory(outputFilePath);
        using var data = _image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputFilePath);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        _image.Dispose();
        _surface.Dispose();
    }
}
