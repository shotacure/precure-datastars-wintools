namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 中身を変えずに流通元を替えて再発売された盤と、その初回盤の対応。
/// 2004 年にバップ流通で出た初回盤 2 点（主題歌シングル・サントラ1）は、同じ年の 9 月にジェネオン流通で
/// 同じ中身のまま再発売された。
/// 歌・劇伴の詳細の収録盤一覧では、両方を行にすると同じ中身が二重に並ぶので、初回盤を行にして、
/// 再発売盤の同じトラックは行にせず、初回盤の行の下に本行と同じ書き方で添え、再発売盤の該当トラックへリンクする。
/// 商品詳細・商品一覧には初回盤も再発売盤も出す。
/// </summary>
public static class Reissues
{
    /// <summary>再発売盤の商品品番 → 初回盤の商品品番。</summary>
    private static readonly Dictionary<string, string> FirstPressByReissue = new(StringComparer.Ordinal)
    {
        ["MJCD-23001"] = "MJCG-83027",
        ["MJCD-20011"] = "MJCG-80146",
    };

    /// <summary>再発売盤なら初回盤の商品品番、そうでなければ null。</summary>
    public static string? FirstPressOf(string productCatalogNo)
        => FirstPressByReissue.TryGetValue(productCatalogNo, out var firstPress) ? firstPress : null;
}
