namespace PrecureDataStars.Data.Models;

/// <summary>
/// petitcure_videos テーブルに対応するエンティティモデル（PK: video_id）。
/// ぷちきゅあの YouTube の動画。公開の時刻は日本時間。概要欄の全文はスタッフの出典として持つ（サイトには出さない）。
/// </summary>
public sealed class PetitcureVideo
{
    /// <summary>YouTube の動画 ID（PK。大文字小文字を区別する）。</summary>
    public string VideoId { get; set; } = "";

    /// <summary>投稿したチャンネル（PETITCURE / PRECURE_OFFICIAL / TOEI_ANIMATION）。</summary>
    public string Channel { get; set; } = "";

    /// <summary>MAIN＝通常の動画 / SHORT＝ショート動画。</summary>
    public string VideoKind { get; set; } = "";

    /// <summary>YouTube の題（そのまま）。</summary>
    public string TitleText { get; set; } = "";

    /// <summary>公開の時刻（日本時間）。</summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>長さ（秒）。分からなければ null。</summary>
    public uint? DurationSeconds { get; set; }
}
