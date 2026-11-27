namespace PrecureDataStars.Data.Models;

/// <summary>
/// petitcure_episodes テーブルに対応するエンティティモデル（PK: petitcure_episode_id）。
/// ショートアニメ「ぷちきゅあ」の話。サブタイトルは無く、話数と YouTube の題に付く飾りの言葉だけを持つ。
/// 放送の話（<see cref="Episode"/>）とは別の系統で、通算・集計の対象にならない。行は SQL で入れる。
/// </summary>
public sealed class PetitcureEpisode
{
    /// <summary>話の ID（PK）。</summary>
    public int PetitcureEpisodeId { get; set; }

    /// <summary>作品 ID（→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>作品の中の話数（YouTube の題の「第N話」「＃N」）。</summary>
    public ushort EpNo { get; set; }

    /// <summary>YouTube の題に付く飾りの言葉（「【おやすみ用】眠れない夜に…」など）。無ければ null。</summary>
    public string? TitleDecoration { get; set; }

    /// <summary>メモ（サイトには出さない）。</summary>
    public string? Notes { get; set; }
}
