namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 録音バリエーションの表示タイトルを組み立てる共通ヘルパ。
/// <para>
/// <c>songs.title</c>（曲名の基底）に <c>song_recordings.variant_label</c>（版・エディション等の接尾辞）が
/// 非空なら半角スペースを挟んで連結する。variant_label は接尾辞のみを保持する前提で、
/// 表示は常に「曲名 + 半角SP + 接尾辞」の形に統一する。
/// </para>
/// </summary>
public static class SongDisplayTitle
{
    /// <summary>曲名と variant_label（版接尾辞）から表示タイトルを組み立てる。</summary>
    /// <param name="title">親曲名（<c>songs.title</c>）。</param>
    /// <param name="variantLabel">録音の版接尾辞（<c>song_recordings.variant_label</c>）。空 / NULL なら曲名のみ。</param>
    public static string Build(string title, string? variantLabel)
        => string.IsNullOrEmpty(variantLabel) ? title : $"{title} {variantLabel}";
}
