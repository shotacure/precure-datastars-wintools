using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// ショートアニメ「ぷちきゅあ」の話・動画・概要欄のスタッフ（petitcure_* の 4 テーブル）の索引。
/// SiteDataLoader が起動時に 1 度だけ組み立て、以降は読み取り専用（並列レンダリングから引いても安全）。
/// <para>
/// ぷちきゅあは集計の対象にしない。この索引は本編クレジットの関与索引（<see cref="CreditInvolvementIndex"/>）にも
/// 音楽クレジットの索引（<see cref="MusicCreditIndex"/>）にも一切混ぜず、作品詳細と、人物・企業の詳細の「ぷちきゅあ」の節だけが引く。
/// そのため、担当量の札・OGP カードの数・初参加・見出しの名義・年表・クリエイターの一覧・役職詳細のどれにも入らない。
/// </para>
/// </summary>
public sealed class PetitcureIndex
{
    /// <summary>行の無い空の索引（テストや DB の無い環境用）。</summary>
    public static readonly PetitcureIndex Empty = new(
        Array.Empty<PetitcureEpisode>(), Array.Empty<PetitcureVideo>(),
        Array.Empty<PetitcureVideoEpisode>(), Array.Empty<PetitcureVideoStaff>());

    /// <summary>作品 ID → 話（話数の順）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureEpisode>> EpisodesBySeries { get; }

    /// <summary>動画 ID → 動画。</summary>
    public IReadOnlyDictionary<string, PetitcureVideo> VideoById { get; }

    /// <summary>話の ID → その話の動画（公開の時刻の順）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideo>> VideosByEpisode { get; }

    /// <summary>動画 ID → その動画に入っている話（話数の順）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PetitcureEpisode>> EpisodesByVideo { get; }

    /// <summary>動画 ID → 概要欄のスタッフ（並び順の順）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PetitcureVideoStaff>> StaffByVideo { get; }

    /// <summary>人物の名義 → その名義のスタッフの行。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideoStaff>> StaffByPersonAlias { get; }

    /// <summary>企業/団体の名義 → その名義のスタッフの行（名前が企業の行）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideoStaff>> StaffByCompanyAlias { get; }

    /// <summary>企業/団体の名義 → その名義を所属として添えたスタッフの行。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideoStaff>> StaffByAffiliationCompanyAlias { get; }

    /// <summary>作品 ID → 話を持つ動画の概要欄のスタッフ（動画の公開の時刻 → 並び順の順）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideoStaff>> StaffBySeries { get; }

    /// <param name="episodes">話（作品 → 話数の順）。</param>
    /// <param name="videos">動画（公開の時刻の順）。</param>
    /// <param name="videoEpisodes">動画と話のつなぎ。</param>
    /// <param name="staff">概要欄のスタッフ（動画 → 並び順の順）。</param>
    public PetitcureIndex(
        IReadOnlyList<PetitcureEpisode> episodes,
        IReadOnlyList<PetitcureVideo> videos,
        IReadOnlyList<PetitcureVideoEpisode> videoEpisodes,
        IReadOnlyList<PetitcureVideoStaff> staff)
    {
        var episodeById = episodes.ToDictionary(e => e.PetitcureEpisodeId);
        VideoById = videos.ToDictionary(v => v.VideoId, StringComparer.Ordinal);

        EpisodesBySeries = episodes
            .GroupBy(e => e.SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureEpisode>)g.OrderBy(e => e.EpNo).ToList());

        // つなぎの先が欠けている行（削除の印の立った話・動画）は捨てる。
        var links = videoEpisodes
            .Where(l => episodeById.ContainsKey(l.PetitcureEpisodeId) && VideoById.ContainsKey(l.VideoId))
            .ToList();
        VideosByEpisode = links
            .GroupBy(l => l.PetitcureEpisodeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureVideo>)g
                .Select(l => VideoById[l.VideoId]).OrderBy(v => v.PublishedAt).ThenBy(v => v.VideoId, StringComparer.Ordinal).ToList());
        EpisodesByVideo = links
            .GroupBy(l => l.VideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureEpisode>)g
                .Select(l => episodeById[l.PetitcureEpisodeId]).OrderBy(e => e.EpNo).ToList(), StringComparer.Ordinal);

        // 話につながった動画のスタッフだけを使う（どの話か分からない行は出しようがない）。
        var linkedStaff = staff.Where(s => EpisodesByVideo.ContainsKey(s.VideoId)).ToList();
        StaffByVideo = linkedStaff
            .GroupBy(s => s.VideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureVideoStaff>)g.OrderBy(s => s.DisplayOrder).ToList(), StringComparer.Ordinal);
        StaffByPersonAlias = GroupBy(linkedStaff, s => s.PersonAliasId);
        StaffByCompanyAlias = GroupBy(linkedStaff, s => s.CompanyAliasId);
        StaffByAffiliationCompanyAlias = GroupBy(linkedStaff, s => s.AffiliationCompanyAliasId);
        StaffBySeries = linkedStaff
            .GroupBy(s => EpisodesByVideo[s.VideoId][0].SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureVideoStaff>)g
                .OrderBy(s => VideoById[s.VideoId].PublishedAt).ThenBy(s => s.VideoId, StringComparer.Ordinal).ThenBy(s => s.DisplayOrder).ToList());
    }

    /// <summary>スタッフの行が担当した話数（その行の動画に入っている話の話数）。</summary>
    public IEnumerable<int> EpisodeNosOf(PetitcureVideoStaff row)
        => EpisodesByVideo.TryGetValue(row.VideoId, out var eps) ? eps.Select(e => (int)e.EpNo) : Enumerable.Empty<int>();

    /// <summary>スタッフの行の作品 ID。話につながっていなければ null。</summary>
    public int? SeriesIdOf(PetitcureVideoStaff row)
        => EpisodesByVideo.TryGetValue(row.VideoId, out var eps) && eps.Count > 0 ? eps[0].SeriesId : null;

    private static IReadOnlyDictionary<int, IReadOnlyList<PetitcureVideoStaff>> GroupBy(
        IEnumerable<PetitcureVideoStaff> rows, Func<PetitcureVideoStaff, int?> key)
        => rows
            .Where(s => key(s).HasValue)
            .GroupBy(s => key(s)!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PetitcureVideoStaff>)g.ToList());
}
