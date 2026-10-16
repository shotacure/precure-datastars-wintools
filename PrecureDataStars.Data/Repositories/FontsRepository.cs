using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// fonts（作品で使われた書体のマスタ）と series_font_usages（作品のどこにどの書体を使ったか）の読み取りリポジトリ。
/// 値は SQL で直接入れる運用なので、取得だけを持つ。
/// </summary>
public sealed class FontsRepository : RepositoryBase
{
    /// <summary><see cref="FontsRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public FontsRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>全書体を書体 ID 順で取得する。</summary>
    public async Task<IReadOnlyList<FontFace>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              font_id          AS FontId,
              foundry          AS Foundry,
              family_name      AS FamilyName,
              weight           AS Weight,
              render_font_name AS RenderFontName,
              render_family_name AS RenderFamilyName,
              render_weight    AS RenderWeight,
              render_note      AS RenderNote,
              license_kind     AS LicenseKind,
              product_url      AS ProductUrl,
              archive_url      AS ArchiveUrl,
              notes            AS Notes
            FROM fonts
            ORDER BY font_id;
        """;

        return await QueryListAsync<FontFace>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>全作品の書体の使い場所を、作品・時期・本放送だけかどうか・最初の話の順で取得する。</summary>
    public async Task<IReadOnlyList<SeriesFontUsage>> GetAllUsagesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              usage_id          AS UsageId,
              series_id         AS SeriesId,
              usage_kind        AS UsageKind,
              period_no         AS PeriodNo,
              is_broadcast_only AS IsBroadcastOnly,
              first_episode_no  AS FirstEpisodeNo,
              last_episode_no   AS LastEpisodeNo,
              font_id           AS FontId,
              is_weight_unknown AS IsWeightUnknown,
              modification_note AS ModificationNote,
              size_px           AS SizePx,
              role_size_px      AS RoleSizePx,
              name_span_em      AS NameSpanEm,
              role_span_em      AS RoleSpanEm,
              role_name_gap_em  AS RoleNameGapEm,
              role_baseline_offset_em AS RoleBaselineOffsetEm,
              role_align        AS RoleAlign,
              display_note      AS DisplayNote,
              notes             AS Notes
            FROM series_font_usages
            ORDER BY series_id, period_no, is_broadcast_only, first_episode_no, usage_id;
        """;

        return await QueryListAsync<SeriesFontUsage>(sql, ct: ct).ConfigureAwait(false);
    }
}
