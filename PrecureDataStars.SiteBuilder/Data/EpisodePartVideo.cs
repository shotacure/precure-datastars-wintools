namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>
/// 各話のパート（episode_parts の行）に結び付けた YouTube の動画 1 本（<c>episode_part_videos</c> の行）。
/// 毎話のコーナーを公式チャンネルが動画で上げているものを、エピソード詳細の特別予告の下に出すのに使う。
/// </summary>
public sealed class EpisodePartVideo
{
    public int EpisodeId { get; set; }
    /// <summary>動画を結ぶパートの順（episode_parts.episode_seq）。</summary>
    public int EpisodeSeq { get; set; }
    /// <summary>パートの中の動画の順（1＝本放送のもの、2 からは別の ver.）。</summary>
    public int VideoSeq { get; set; }
    /// <summary>YouTube の動画 ID。</summary>
    public string VideoId { get; set; } = "";
    /// <summary>YouTube の題（そのまま）。</summary>
    public string TitleText { get; set; } = "";
}
