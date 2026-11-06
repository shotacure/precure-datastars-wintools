namespace PrecureDataStars.Data.Models;

/// <summary>
/// エピソードの外部リンクの列のうち、放送後に公式から出るもの（<see cref="Repositories.EpisodesRepository.FillLinkIfEmptyAsync"/> で書く列）。
/// </summary>
public enum EpisodeLinkColumn
{
    /// <summary>東映アニメーション公式のあらすじのページ（episodes.toei_anim_summary_url）。</summary>
    ToeiAnimSummary,

    /// <summary>東映アニメーションの作品ラインナップの各話ページ（episodes.toei_anim_lineup_url）。</summary>
    ToeiAnimLineup,

    /// <summary>公式 YouTube チャンネルの予告（episodes.youtube_trailer_url）。</summary>
    YoutubeTrailer
}
