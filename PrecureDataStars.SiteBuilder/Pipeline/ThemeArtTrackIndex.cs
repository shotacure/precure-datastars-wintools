using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// シリーズ詳細・エピソード詳細の主題歌・挿入歌に置く再生ボタン用に、録音ごとに鳴らす配信音源
/// （YouTube アートトラック）を 1 つ選んだ索引。SiteDataLoader が起動時に 1 度だけ組み立て、以降は読み取り専用。
/// <para>選び方：</para>
/// <list type="bullet">
///   <item><description>歌入り（パート未設定・<c>VOCAL</c>・<c>_ANY</c>）のトラックだけを候補にする。カラオケしか無い録音にはボタンを出さない。
///     次回予告（サイズ <c>NEXT</c>）も候補にしない。</description></item>
///   <item><description>本編で流れるサイズ（TV サイズ系・映画サイズ）を優先し、無ければ他のサイズ（フルサイズなど）を使う。</description></item>
///   <item><description>同じ優先度の中では発売の早い盤（初出）を採る。それが YouTube Music Premium 会員限定なら、
///     同じサイズで誰でも再生できる盤の音源を代わりとして併せ持つ（どちらを鳴らすかは閲覧者の設定でページ側が決める。歌の詳細と同じ）。</description></item>
///   <item><description>埋め込み可と確認済み（<c>youtube_embeddable = 1</c>）の音源だけを使う。</description></item>
/// </list>
/// </summary>
public sealed class ThemeArtTrackIndex
{
    public static readonly ThemeArtTrackIndex Empty = new(
        new Dictionary<string, IReadOnlyList<Track>>(), Array.Empty<Disc>(), Array.Empty<Product>());

    /// <summary>song_recording_id → 鳴らす配信音源。</summary>
    public IReadOnlyDictionary<int, ThemeArtTrack> ByRecording { get; }

    public ThemeArtTrackIndex(
        IReadOnlyDictionary<string, IReadOnlyList<Track>> tracksByCatalogNo,
        IReadOnlyList<Disc> discs,
        IReadOnlyList<Product> products)
    {
        var discByCatalogNo = discs.GroupBy(d => d.CatalogNo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var productByCatalogNo = products.GroupBy(p => p.ProductCatalogNo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var candidates = new List<(int RecordingId, int Priority, DateTime Release, string CatalogNo, byte TrackNo, string Size, Track Track, Disc Disc, Product Product)>();
        foreach (var t in tracksByCatalogNo.Values.SelectMany(x => x))
        {
            if (t.SongRecordingId is not int recId) continue;
            if (t.YoutubeEmbeddable != true || string.IsNullOrEmpty(t.YoutubeArtTrackId)) continue;
            if (!IsVocalPart(t.SongPartVariantCode)) continue;
            string size = t.SongSizeVariantCode ?? "";
            if (size == "NEXT") continue;
            if (!discByCatalogNo.TryGetValue(t.CatalogNo, out var disc)) continue;
            if (!productByCatalogNo.TryGetValue(disc.ProductCatalogNo, out var product)) continue;
            int priority = IsBroadcastSize(size) ? 0 : 1;
            candidates.Add((recId, priority, product.ReleaseDate, t.CatalogNo, t.TrackNo, size, t, disc, product));
        }

        var byRecording = new Dictionary<int, ThemeArtTrack>();
        foreach (var g in candidates.GroupBy(c => c.RecordingId))
        {
            var ordered = g.OrderBy(c => c.Priority).ThenBy(c => c.Release).ThenBy(c => c.CatalogNo, StringComparer.Ordinal).ThenBy(c => c.TrackNo).ToList();
            var primary = ordered[0];
            bool premium = IsPremiumOnly(primary.Track);
            var alt = premium
                ? ordered.FirstOrDefault(c => c.Size == primary.Size && !IsPremiumOnly(c.Track) && c.Track.YoutubeArtTrackId != primary.Track.YoutubeArtTrackId)
                : default;
            byRecording[g.Key] = new ThemeArtTrack(
                ArtTrackId: primary.Track.YoutubeArtTrackId!,
                PremiumOnly: premium,
                SourceAlbum: AlbumLabel(primary.Product, primary.Disc),
                AltArtTrackId: alt.Track?.YoutubeArtTrackId ?? "",
                AltSourceAlbum: alt.Track is null ? "" : AlbumLabel(alt.Product, alt.Disc));
        }
        ByRecording = byRecording;
    }

    /// <summary>歌が入っているパート区分か。未設定（空）と <c>_ANY</c> は区分を持たない録音物なので歌入り扱い（歌の詳細と同じ判定）。</summary>
    private static bool IsVocalPart(string? partVariantCode)
        => string.IsNullOrEmpty(partVariantCode) || partVariantCode == "VOCAL" || partVariantCode == "_ANY";

    /// <summary>本編で流れるサイズか（TV サイズ系 <c>TV</c> / <c>TV_TYPE_*</c> / <c>TV_V*</c> と映画サイズ <c>MOVIE</c>）。</summary>
    private static bool IsBroadcastSize(string sizeVariantCode)
        => sizeVariantCode == "MOVIE" || sizeVariantCode == "TV" || sizeVariantCode.StartsWith("TV_", StringComparison.Ordinal);

    private static bool IsPremiumOnly(Track t) => string.Equals(t.YoutubePlayability, "PREMIUM_ONLY", StringComparison.Ordinal);

    /// <summary>
    /// プレイヤーに出す「いま鳴っている音源が入っているアルバム」の表記（歌の詳細と同じ規則）。
    /// 盤の名前（<c>discs.title</c>）があればそれを、無ければ商品タイトル（複数枚組なら盤番号つき）。
    /// </summary>
    private static string AlbumLabel(Product product, Disc disc)
    {
        if (!string.IsNullOrWhiteSpace(disc.Title)) return disc.Title!;
        return disc.DiscNoInSet.HasValue ? $"{product.Title} Disc{disc.DiscNoInSet.Value}" : product.Title;
    }
}

/// <summary>主題歌の再生ボタン 1 つ分の配信音源（<see cref="ThemeArtTrackIndex"/> が選んだもの）。</summary>
/// <param name="ArtTrackId">鳴らす YouTube 動画 ID。</param>
/// <param name="PremiumOnly">YouTube Music Premium 会員限定か。</param>
/// <param name="SourceAlbum">音源が入っている盤の表記。</param>
/// <param name="AltArtTrackId">会員限定のときの代わり（誰でも再生できる同じサイズの音源）。無ければ空文字。</param>
/// <param name="AltSourceAlbum">代わりの音源が入っている盤の表記。</param>
public sealed record ThemeArtTrack(string ArtTrackId, bool PremiumOnly, string SourceAlbum, string AltArtTrackId, string AltSourceAlbum);
