namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// フォントのページの場面の画像（OP・ED の画面を模したもの）の材料。画面の大きさは放送の画面のまま
/// （<paramref name="Width"/>：16:9 なら 1920、4:3 なら 1440。高さはどちらも 1080）で、
/// 灰色の市松模様の地にクレジットと歌詞のテロップ（<paramref name="Pieces"/>）を測った位置・大きさで置く。
/// </summary>
public sealed record FontScene(int Width, IReadOnlyList<FontScenePiece> Pieces)
{
    /// <summary>画面の高さ（画素）。</summary>
    public const int Height = 1080;
}

/// <summary>
/// 場面の画像に置くテロップ 1 つ。<paramref name="SizePx"/> は字の大きさ（画面の高さ 1080 の画素）、
/// <paramref name="X"/>・<paramref name="Y"/> は置く位置（画面の画素）で、<paramref name="CenterX"/> なら行の中心、そうでなければ字の並びの左端。
/// <paramref name="Y"/> はベースライン。
/// </summary>
public sealed record FontScenePiece(string Html, string FontFamily, SubtitleTelopProfile Profile, float SizePx, float X, float Y, bool CenterX);
