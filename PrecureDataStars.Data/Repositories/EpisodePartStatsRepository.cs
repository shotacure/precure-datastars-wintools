using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// エピソード尺・CM 入り時刻統計の集計クエリ群（v1.3.0 後半追加）。
/// <para>
/// SiteBuilder の <c>/stats/episodes/</c> 配下のページ群が使う読み取り専用の集計クエリを提供する。
/// 各メソッドは生 SQL を <see cref="Dapper"/> 経由で実行し、画面表示用の素朴な DTO を返す。
/// </para>
/// <para>
/// 集計の元データは <c>episode_parts</c>（パート種別ごとの OA 尺秒数を持つ）。
/// パート尺ランキングは PART_A / PART_B 等のパート種別を絞った合計、
/// CM 入り時刻ランキングは CM2 パートの開始までの累積秒数（番組内オフセット）から算出する。
/// </para>
/// </summary>
public sealed class EpisodePartStatsRepository
{
    private readonly IConnectionFactory _factory;

    public EpisodePartStatsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// 指定パート種別（PART_A / PART_B 等）の尺ランキング。
    /// <paramref name="ascending"/> = true で短い順、false で長い順。TOP <paramref name="limit"/>。同点同順。
    /// 出典 SQL：「歴代Aパート尺長さ.sql」「歴代Bパート尺長さ.sql」「歴代Bパート尺短さ.sql」
    /// </summary>
    public async Task<IReadOnlyList<EpisodePartLengthRow>> GetPartLengthRankingAsync(
        string partType, bool ascending, int limit, CancellationToken ct = default)
    {
        // ORDER BY 方向の切替は文字列補間で行う（パラメータでは方向の切替不可）。
        // partType / ascending / limit はすべて呼び出し側固定値なのでインジェクションリスクなし。
        string direction = ascending ? "ASC" : "DESC";
        string sql = $"""
            WITH part_sum AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                SUM(ep.oa_length) AS LengthSeconds
              FROM episodes AS e
              JOIN series AS s ON s.series_id = e.series_id
              JOIN episode_parts AS ep
                ON ep.episode_id = e.episode_id
               AND ep.part_type  = @partType
              WHERE e.is_deleted = 0
              GROUP BY e.episode_id, s.title, s.slug, e.series_ep_no, e.title_text
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY LengthSeconds {direction}) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, LengthSeconds
              FROM part_sum
              WHERE LengthSeconds IS NOT NULL
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, LengthSeconds
            FROM ranked
            ORDER BY `Rank` ASC, EpisodeId ASC
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodePartLengthRow>(
            new CommandDefinition(sql, new { partType, limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 中 CM（CM2 パート）入り時刻ランキング。
    /// <paramref name="ascending"/> = true で早い順、false で遅い順。TOP <paramref name="limit"/>。同点同順。
    /// オフセットは番組開始（08:30:00 起点）からの累積秒数で算出する。
    /// 出典 SQL：「歴代CM入り時刻早い順ランキング.sql」「歴代CM入り時刻遅い順ランキング.sql」
    /// </summary>
    public async Task<IReadOnlyList<CmTimeRow>> GetCmTimeRankingAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        string direction = ascending ? "ASC" : "DESC";
        // parts CTE は各パートの「自パート開始までに経過した累積秒数」を WINDOW で計算。
        // 自パート分は除外したいので ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING。
        string sql = $"""
            WITH parts AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                ep.part_type   AS PartType,
                ep.oa_length   AS OaLength,
                COALESCE(
                  SUM(ep.oa_length) OVER (
                    PARTITION BY e.episode_id
                    ORDER BY ep.episode_seq
                    ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                  ),
                  0
                ) AS SecondsBeforePart
              FROM episodes AS e
              JOIN series AS s ON s.series_id = e.series_id
              JOIN episode_parts AS ep ON ep.episode_id = e.episode_id
              WHERE e.is_deleted = 0
            ),
            cm2 AS (
              SELECT EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                     SecondsBeforePart AS Cm2OffsetSeconds
              FROM parts
              WHERE PartType = 'CM2'
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Cm2OffsetSeconds {direction}) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, Cm2OffsetSeconds
              FROM cm2
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, Cm2OffsetSeconds
            FROM ranked
            ORDER BY `Rank` ASC, EpisodeId ASC
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CmTimeRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ × パート別の平均/最短/最長尺。OA 尺がある行のみ集計。
    /// 出典 SQL：「OAシリーズごとパート平均尺.sql」
    /// </summary>
    public async Task<IReadOnlyList<SeriesPartAvgRow>> GetPartAveragesBySeriesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.series_id      AS SeriesId,
              s.title          AS SeriesTitle,
              s.slug           AS SeriesSlug,
              pt.part_type     AS PartType,
              pt.name_ja       AS PartLabel,
              pt.display_order AS PartDisplayOrder,
              COUNT(ep.oa_length)               AS OccurrenceCount,
              ROUND(AVG(ep.oa_length), 2)       AS AvgSeconds,
              ROUND(MIN(ep.oa_length))          AS MinSeconds,
              ROUND(MAX(ep.oa_length))          AS MaxSeconds
            FROM episode_parts ep
            JOIN part_types    pt ON pt.part_type   = ep.part_type
            JOIN episodes      e  ON e.episode_id   = ep.episode_id
            JOIN series        s  ON s.series_id    = e.series_id
            WHERE e.is_deleted = 0 AND ep.oa_length IS NOT NULL
            GROUP BY s.series_id, s.title, s.slug, pt.display_order, pt.part_type, pt.name_ja
            ORDER BY s.series_id, pt.display_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesPartAvgRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    // ──────────────────────────────────────────────────────
    // DTO 群
    // ──────────────────────────────────────────────────────

    /// <summary>パート尺ランキング 1 行。</summary>
    public sealed class EpisodePartLengthRow
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>パート尺合計（秒）。</summary>
        public double LengthSeconds { get; set; }
    }

    /// <summary>CM 入り時刻ランキング 1 行。</summary>
    public sealed class CmTimeRow
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>番組開始（08:30:00）から CM2 パート開始までの累積秒数。</summary>
        public double Cm2OffsetSeconds { get; set; }
    }

    /// <summary>シリーズ × パート別の集計値 1 行。</summary>
    public sealed class SeriesPartAvgRow
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public string PartType { get; set; } = "";
        public string PartLabel { get; set; } = "";
        public byte? PartDisplayOrder { get; set; }
        public long OccurrenceCount { get; set; }
        public double AvgSeconds { get; set; }
        public double MinSeconds { get; set; }
        public double MaxSeconds { get; set; }
    }
}
