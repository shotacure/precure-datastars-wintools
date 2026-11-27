using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// ショートアニメ「ぷちきゅあ」の 4 テーブル（petitcure_episodes・petitcure_videos・petitcure_video_episodes・
/// petitcure_video_staff）のリポジトリ。行は SQL で入れるので、読み出しだけを持つ。削除の印（is_deleted）の立った行は返さない。
/// </summary>
public sealed class PetitcureRepository : RepositoryBase
{
    /// <summary><see cref="PetitcureRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public PetitcureRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>話を、作品 → 話数の順に取得する。</summary>
    public async Task<IReadOnlyList<PetitcureEpisode>> GetEpisodesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              petitcure_episode_id AS PetitcureEpisodeId,
              series_id            AS SeriesId,
              ep_no                AS EpNo,
              title_decoration     AS TitleDecoration,
              notes                AS Notes
            FROM petitcure_episodes
            WHERE is_deleted = 0
            ORDER BY series_id, ep_no;
            """;
        return await QueryListAsync<PetitcureEpisode>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>動画を、公開の時刻 → 動画 ID の順に取得する（概要欄の全文は読まない）。</summary>
    public async Task<IReadOnlyList<PetitcureVideo>> GetVideosAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              video_id         AS VideoId,
              channel          AS Channel,
              video_kind       AS VideoKind,
              title_text       AS TitleText,
              published_at     AS PublishedAt,
              duration_seconds AS DurationSeconds
            FROM petitcure_videos
            WHERE is_deleted = 0
            ORDER BY published_at, video_id;
            """;
        return await QueryListAsync<PetitcureVideo>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>動画と話のつなぎを全件取得する。</summary>
    public async Task<IReadOnlyList<PetitcureVideoEpisode>> GetVideoEpisodesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              video_id             AS VideoId,
              petitcure_episode_id AS PetitcureEpisodeId
            FROM petitcure_video_episodes
            ORDER BY petitcure_episode_id, video_id;
            """;
        return await QueryListAsync<PetitcureVideoEpisode>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>概要欄のスタッフを、動画 → 並び順の順に取得する。</summary>
    public async Task<IReadOnlyList<PetitcureVideoStaff>> GetStaffAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              staff_id                     AS StaffId,
              video_id                     AS VideoId,
              display_order                AS DisplayOrder,
              role_label_text              AS RoleLabelText,
              name_text                    AS NameText,
              person_alias_id              AS PersonAliasId,
              company_alias_id             AS CompanyAliasId,
              affiliation_text             AS AffiliationText,
              affiliation_company_alias_id AS AffiliationCompanyAliasId
            FROM petitcure_video_staff
            WHERE is_deleted = 0
            ORDER BY video_id, display_order;
            """;
        return await QueryListAsync<PetitcureVideoStaff>(sql, ct: ct).ConfigureAwait(false);
    }
}
