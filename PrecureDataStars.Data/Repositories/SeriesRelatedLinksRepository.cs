using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_related_links テーブル（作品ごとの関連記事）のリポジトリ。行は SQL で入れるので、読み出しだけを持つ。
/// </summary>
public sealed class SeriesRelatedLinksRepository : RepositoryBase
{
    /// <summary><see cref="SeriesRelatedLinksRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesRelatedLinksRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>出す設定の行（is_enabled = 1）を、作品 → 並び順 → 行の ID の順に取得する。</summary>
    public async Task<IReadOnlyList<SeriesRelatedLink>> GetEnabledAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              link_id        AS LinkId,
              series_id      AS SeriesId,
              title          AS Title,
              url            AS Url,
              site_label     AS SiteLabel,
              published_on   AS PublishedOn,
              display_order  AS DisplayOrder,
              is_enabled     AS IsEnabled,
              created_at     AS CreatedAt,
              updated_at     AS UpdatedAt,
              created_by     AS CreatedBy,
              updated_by     AS UpdatedBy
            FROM series_related_links
            WHERE is_enabled = 1
            ORDER BY series_id, display_order, link_id;
            """;

        return await QueryListAsync<SeriesRelatedLink>(sql, ct: ct).ConfigureAwait(false);
    }
}
