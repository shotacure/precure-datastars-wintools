using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// 割り当て済みの配信音源（YouTube アートトラック）を、YouTube Data API で確かめ直すサービス。
/// <para>
/// YouTube API Services Developer Policies III.E.4 は、保存した API データを 30 日以内に取り直すか消すか、
/// 動画が削除されていないかを 30 日ごとに確かめることを求めている。本サービスは割り当て済みの全動画を
/// <c>videos.list</c>（50 件 1 ユニット）で問い合わせ直し、次のように反映する。
/// </para>
/// <list type="bullet">
///   <item>応答にある動画：埋め込み可否と地域制限を取り直し、確認時刻を更新する。地域制限で日本では
///         再生できなくなっていれば <see cref="ArtTrackPlayability.Unplayable"/>、制限が外れていれば
///         未確認（null）に戻す。人が決めた会員限定かどうか（<see cref="ArtTrackPlayability.PremiumOnly"/> /
///         <see cref="ArtTrackPlayability.Ok"/>）には触れない。</item>
///   <item>応答に無い動画（削除・非公開）：動画 ID ごと消す。</item>
/// </list>
/// <para>
/// 動画が消えた・埋め込めなくなった・日本で再生できなくなった商品は、取り込み状態を
/// <c>AMBIGUOUS</c> に戻し、Catalog であとから人が取り込み直せるようにする。
/// </para>
/// <para>
/// API の不調などで大量の動画が「消えた」と見えたときに割り当てをまとめて失わないよう、
/// 消える件数が <see cref="MassRemovalThreshold"/> を超えたら書き込まずに止める。
/// </para>
/// </summary>
public sealed class ArtTrackRefreshService
{
    /// <summary>これを超える件数の動画が消えていたら、明示の許可が無い限り書き込まずに止める（件数・割合の大きい方）。</summary>
    private const int MassRemovalMinimum = 20;

    /// <summary>消えた動画がこの割合を超えたら止める。</summary>
    private const double MassRemovalRatio = 0.05;

    private readonly YouTubeDataApiClient _api;
    private readonly TracksRepository _tracksRepo;
    private readonly ProductsRepository _productsRepo;

    /// <summary><see cref="ArtTrackRefreshService"/> の新しいインスタンスを生成する。</summary>
    public ArtTrackRefreshService(
        YouTubeDataApiClient api, TracksRepository tracksRepo, ProductsRepository productsRepo)
    {
        _api = api;
        _tracksRepo = tracksRepo;
        _productsRepo = productsRepo;
    }

    /// <summary>消えた動画がこの件数を超えたら止める。</summary>
    /// <param name="totalVideos">確かめた動画の件数（重複を除く）。</param>
    public static int MassRemovalThreshold(int totalVideos)
        => Math.Max(MassRemovalMinimum, (int)Math.Ceiling(totalVideos * MassRemovalRatio));

    /// <summary>割り当て済みの全トラックを確かめ直す。</summary>
    /// <param name="dryRun">true なら DB へは書かず、結果だけを返す。</param>
    /// <param name="allowMassRemoval">true なら、消えた動画が多くても止めずに書き込む。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<ArtTrackRefreshResult> RefreshAsync(
        bool dryRun, bool allowMassRemoval, CancellationToken ct = default)
    {
        var rows = await _tracksRepo.GetArtTrackRefreshRowsAsync(ct).ConfigureAwait(false);
        var videoIds = rows.Select(r => r.YoutubeArtTrackId).Distinct(StringComparer.Ordinal).ToList();
        var details = await _api.GetVideoDetailsAsync(videoIds, ct).ConfigureAwait(false);

        var result = new ArtTrackRefreshResult { TrackCount = rows.Count, VideoCount = videoIds.Count };
        var assignments = new List<ArtTrackAssignment>(rows.Count);
        var productsToReview = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in rows)
        {
            if (!details.TryGetValue(r.YoutubeArtTrackId, out var detail))
            {
                result.Changes.Add(new ArtTrackRefreshChange(r, ArtTrackRefreshChangeKind.Removed));
                productsToReview.Add(r.ProductCatalogNo);
                assignments.Add(Assignment(r, null, null, null));
                continue;
            }

            bool embeddable = ArtTrackImportService.IsEmbeddable(detail);
            bool wasBlocked = r.YoutubePlayability == ArtTrackPlayability.Unplayable;
            string? playability = detail.BlockedInJapan ? ArtTrackPlayability.Unplayable
                                : wasBlocked ? null
                                : r.YoutubePlayability;

            if (r.YoutubeEmbeddable == true && !embeddable)
            {
                result.Changes.Add(new ArtTrackRefreshChange(r, ArtTrackRefreshChangeKind.NoLongerEmbeddable));
                productsToReview.Add(r.ProductCatalogNo);
            }
            else if (r.YoutubeEmbeddable != true && embeddable)
            {
                result.Changes.Add(new ArtTrackRefreshChange(r, ArtTrackRefreshChangeKind.NowEmbeddable));
            }

            if (!wasBlocked && detail.BlockedInJapan)
            {
                result.Changes.Add(new ArtTrackRefreshChange(r, ArtTrackRefreshChangeKind.BlockedInJapan));
                productsToReview.Add(r.ProductCatalogNo);
            }
            else if (wasBlocked && !detail.BlockedInJapan)
            {
                result.Changes.Add(new ArtTrackRefreshChange(r, ArtTrackRefreshChangeKind.Unblocked));
            }

            assignments.Add(Assignment(r, r.YoutubeArtTrackId, embeddable, playability));
        }

        result.ProductsMarkedAmbiguous = productsToReview.OrderBy(p => p, StringComparer.Ordinal).ToList();

        int removedVideos = result.Changes
            .Where(c => c.Kind == ArtTrackRefreshChangeKind.Removed)
            .Select(c => c.VideoId).Distinct(StringComparer.Ordinal).Count();
        if (removedVideos > MassRemovalThreshold(videoIds.Count) && !allowMassRemoval)
        {
            result.StoppedForMassRemoval = true;
            return result;
        }

        if (!dryRun)
        {
            await _tracksRepo.UpdateArtTrackAssignmentsAsync(assignments, DateTime.Now, ct).ConfigureAwait(false);
            await _productsRepo.MarkArtTrackAmbiguousAsync(productsToReview, ct).ConfigureAwait(false);
            result.Written = true;
        }

        return result;
    }

    private static ArtTrackAssignment Assignment(
        ArtTrackRefreshRow r, string? videoId, bool? embeddable, string? playability)
        => new()
        {
            CatalogNo = r.CatalogNo,
            TrackNo = r.TrackNo,
            SubOrder = r.SubOrder,
            YoutubeArtTrackId = videoId,
            YoutubeEmbeddable = embeddable,
            YoutubePlayability = playability
        };
}

/// <summary>再確認で起きた変化の種類。</summary>
public enum ArtTrackRefreshChangeKind
{
    /// <summary>動画が消えた（削除・非公開）。動画 ID を消す。</summary>
    Removed,
    /// <summary>埋め込みが許可されなくなった。</summary>
    NoLongerEmbeddable,
    /// <summary>埋め込みが許可されるようになった。</summary>
    NowEmbeddable,
    /// <summary>地域制限で日本では再生できなくなった。</summary>
    BlockedInJapan,
    /// <summary>日本での地域制限が外れた。</summary>
    Unblocked
}

/// <summary>再確認で変化があったトラック 1 件。</summary>
public sealed record ArtTrackRefreshChange(
    string ProductCatalogNo, string CatalogNo, byte TrackNo, byte SubOrder, string VideoId,
    ArtTrackRefreshChangeKind Kind)
{
    /// <summary>対象行と変化の種類から生成する。</summary>
    public ArtTrackRefreshChange(ArtTrackRefreshRow r, ArtTrackRefreshChangeKind kind)
        : this(r.ProductCatalogNo, r.CatalogNo, r.TrackNo, r.SubOrder, r.YoutubeArtTrackId, kind) { }
}

/// <summary>再確認の結果。</summary>
public sealed class ArtTrackRefreshResult
{
    /// <summary>確かめたトラック数。</summary>
    public int TrackCount { get; set; }

    /// <summary>確かめた動画数（同じ動画を複数のトラックが指すことがあるため、トラック数以下）。</summary>
    public int VideoCount { get; set; }

    /// <summary>変化があったトラック。</summary>
    public List<ArtTrackRefreshChange> Changes { get; } = new();

    /// <summary>取り込み状態を <c>AMBIGUOUS</c> に戻す（戻した）商品。</summary>
    public IReadOnlyList<string> ProductsMarkedAmbiguous { get; set; } = Array.Empty<string>();

    /// <summary>消えた動画が多すぎたため、書き込まずに止めたか。</summary>
    public bool StoppedForMassRemoval { get; set; }

    /// <summary>DB へ書き込んだか。</summary>
    public bool Written { get; set; }
}
