namespace PrecureDataStars.Data.Models;

/// <summary>
/// petitcure_video_episodes テーブルに対応するエンティティモデル（PK: video_id + petitcure_episode_id）。
/// ぷちきゅあの動画と話のつなぎ。1 本に 2 話入る動画（「第1話&amp;第2話」）は 2 行になる。
/// </summary>
public sealed class PetitcureVideoEpisode
{
    /// <summary>YouTube の動画 ID（→ petitcure_videos.video_id）。</summary>
    public string VideoId { get; set; } = "";

    /// <summary>話の ID（→ petitcure_episodes.petitcure_episode_id）。</summary>
    public int PetitcureEpisodeId { get; set; }
}
