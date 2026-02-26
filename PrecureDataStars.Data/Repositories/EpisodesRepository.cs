using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Data;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episodes テーブルの CRUD リポジトリ。
/// <para>
/// サブタイトル文字統計に関する分析クエリ（初出文字の検索、使用回数カウント、
/// 「○年ぶり」の復活統計）も提供する。
/// </para>
/// <remarks>
/// DateTime ⇔ DATETIME (タイムゾーンなし) を前提としている。
/// </remarks>
/// </summary>
public sealed class EpisodesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="EpisodesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public EpisodesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // ────────────────────────────────────────────────
    //  基本 CRUD
    // ────────────────────────────────────────────────

    /// <summary>
    /// 指定シリーズに紐づく有効なエピソード（is_deleted = 0）を series_ep_no 昇順で全件取得する。
    /// </summary>
    /// <param name="seriesId">シリーズ ID。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>エピソード一覧。</returns>
    public async Task<IReadOnlyList<Episode>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              episode_id   AS EpisodeId,
              series_id    AS SeriesId,
              series_ep_no AS SeriesEpNo,
              total_ep_no  AS TotalEpNo,
              total_oa_no  AS TotalOaNo,
              nitiasa_oa_no AS NitiasaOaNo,
              title_text   AS TitleText,
              title_rich_html AS TitleRichHtml,
              title_kana   AS TitleKana,
              title_char_stats AS TitleCharStats,
              on_air_at    AS OnAirAt,
              toei_anim_summary_url AS ToeiAnimSummaryUrl,
              toei_anim_lineup_url  AS ToeiAnimLineupUrl,
              youtube_trailer_url   AS YoutubeTrailerUrl,
              notes          AS Notes,
              created_by     AS CreatedBy,
              updated_by     AS UpdatedBy,
              is_deleted     AS IsDeleted
            FROM episodes
            WHERE is_deleted = 0 AND series_id = @seriesId
            ORDER BY series_ep_no;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Episode>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 新しいエピソードを INSERT し、自動採番された episode_id を返す。
    /// </summary>
    /// <param name="e">挿入対象のエピソード。SeriesId / TitleText は必須。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>新しい episode_id。</returns>
    /// <exception cref="ArgumentException">必須項目が未設定の場合。</exception>
    public async Task<int> InsertAsync(Episode e, CancellationToken ct = default)
    {
        if (e.SeriesId <= 0) throw new ArgumentException("SeriesId is required.", nameof(e));
        if (string.IsNullOrWhiteSpace(e.TitleText)) throw new ArgumentException("TitleText is required.", nameof(e));

        const string sql = """
            INSERT INTO episodes(
              series_id, series_ep_no, total_ep_no, total_oa_no, nitiasa_oa_no,
              title_text, title_rich_html, title_kana, title_char_stats, on_air_at,
              toei_anim_summary_url, toei_anim_lineup_url, youtube_trailer_url,
              notes, created_by, updated_by, is_deleted
            ) VALUES (
              @SeriesId, @SeriesEpNo, @TotalEpNo, @TotalOaNo, @NitiasaOaNo,
              @TitleText, @TitleRichHtml, @TitleKana, @TitleCharStats, @OnAirAt,
              @ToeiAnimSummaryUrl, @ToeiAnimLineupUrl, @YoutubeTrailerUrl,
              @Notes, @CreatedBy, @UpdatedBy, 0
            );
            SELECT LAST_INSERT_ID();
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, e, cancellationToken: ct));
        return id;
    }

    /// <summary>
    /// 既存のエピソードを UPDATE する。主キー (<see cref="Episode.EpisodeId"/>) が一致するレコードを更新する。
    /// series_id は外部キーのため更新対象外。
    /// </summary>
    /// <param name="e">更新対象のエピソード。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">EpisodeId が不正、または TitleText が空の場合。</exception>
    public async Task UpdateAsync(Episode e, CancellationToken ct = default)
    {
        if (e.EpisodeId <= 0) throw new ArgumentException("Invalid EpisodeId.", nameof(e));
        if (string.IsNullOrWhiteSpace(e.TitleText)) throw new ArgumentException("TitleText is required.", nameof(e));

        const string sql = """
            UPDATE episodes SET
              series_ep_no = @SeriesEpNo,
              total_ep_no  = @TotalEpNo,
              total_oa_no  = @TotalOaNo,
              nitiasa_oa_no = @NitiasaOaNo,
              title_text   = @TitleText,
              title_rich_html = @TitleRichHtml,
              title_kana   = @TitleKana,
              title_char_stats = @TitleCharStats,
              on_air_at    = @OnAirAt,
              toei_anim_summary_url = @ToeiAnimSummaryUrl,
              toei_anim_lineup_url  = @ToeiAnimLineupUrl,
              youtube_trailer_url   = @YoutubeTrailerUrl,
              notes          = @Notes,
              updated_by     = @UpdatedBy
            WHERE episode_id = @EpisodeId;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, e, cancellationToken: ct));
    }

    // ────────────────────────────────────────────────
    //  サブタイトル文字統計 — 初出・使用回数
    // ────────────────────────────────────────────────

    /// <summary>
    /// 指定した文字（title_char_stats.chars のキー）が初めて使われたエピソードを検索する。
    /// JSON_CONTAINS_PATH を使い、全シリーズ横断で on_air_at が最も古いものを返す。
    /// </summary>
    /// <param name="key">検索する文字（書記素単位のキー文字列）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>初出エピソードの ID と放送日時。該当なしの場合は (null, null)。</returns>
    public async Task<(int? FirstEpisodeId, DateTime? FirstOnAirAt)> GetFirstUseOfCharAsync(string key, CancellationToken ct = default)
    {
        const string sql = """
        SELECT episode_id, on_air_at
        FROM episodes
        WHERE is_deleted = 0
          AND title_char_stats IS NOT NULL
          AND JSON_CONTAINS_PATH(title_char_stats, 'one', CONCAT('$.chars."', REPLACE(REPLACE(@key, '\\', '\\\\'), '\"', '\\\"'), '"'))
        ORDER BY on_air_at, episode_id
        LIMIT 1;
    """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<(int episode_id, DateTime on_air_at)>(
            new CommandDefinition(sql, new { key }, cancellationToken: ct));
        return row == default ? (null, null) : (row.episode_id, row.on_air_at);
    }

    /// <summary>
    /// 指定した文字が使われているエピソード数（全シリーズ横断）をカウントする。
    /// </summary>
    /// <param name="key">検索する文字（書記素単位のキー文字列）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>使用エピソード数。</returns>
    public async Task<int> GetEpisodeUsageCountOfCharAsync(string key, CancellationToken ct = default)
    {
        const string sql = """
        SELECT COUNT(*)
        FROM episodes
        WHERE is_deleted = 0
          AND title_char_stats IS NOT NULL
          AND JSON_CONTAINS_PATH(title_char_stats, 'one', CONCAT('$.chars."', REPLACE(REPLACE(@key, '\\', '\\\\'), '\"', '\\\"'), '"'));
    """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { key }, cancellationToken: ct));
    }

    // ────────────────────────────────────────────────
    //  サブタイトル文字統計 — 「○年ぶり」復活分析
    // ────────────────────────────────────────────────

    /// <summary>
    /// サブタイトル文字の「復活」情報を保持する DTO。
    /// 1 年以上ぶりに使用された文字について、前回の使用情報と経過期間を提供する。
    /// </summary>
    public sealed class TitleCharRevivalStat
    {
        /// <summary>対象文字（書記素単位）。</summary>
        public required string Char { get; init; }

        /// <summary>前回使用からの経過年数。</summary>
        public required int Years { get; init; }

        /// <summary>前回使用からの経過月数（0–11、年の端数分）。</summary>
        public required int Months { get; init; }

        /// <summary>前回使用からの経過話数（通算話数の差分）。</summary>
        public required int EpisodesSince { get; init; }

        /// <summary>当該エピソードを含めた通算出現"話数"（n 回目）。</summary>
        public required int OccurrenceIndex { get; init; }

        /// <summary>前回使用エピソードの ID。</summary>
        public required int LastEpisodeId { get; init; }

        /// <summary>前回使用エピソードのシリーズタイトル。</summary>
        public required string LastSeriesTitle { get; init; }

        /// <summary>前回使用エピソードのシリーズ内話数。</summary>
        public required int LastSeriesEpNo { get; init; }

        /// <summary>前回使用エピソードのサブタイトル。</summary>
        public required string LastTitleText { get; init; }

        /// <summary>前回使用エピソードの放送日時。</summary>
        public required DateTime LastOnAirAt { get; init; }
    }

    /// <summary>
    /// 指定エピソードで使われた各文字について、1 年以上ぶりに出現した文字の情報を返す。
    /// <para>
    /// 処理の流れ:
    /// <list type="number">
    ///   <item>対象エピソードのサブタイトルから文字一覧を取得（JSON_KEYS）</item>
    ///   <item>各文字について過去の全出現履歴を構築</item>
    ///   <item>対象エピソードの直前の使用を特定し、経過期間を算出</item>
    ///   <item>1 年（12 か月）以上経過した文字のみを返却</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="episodeId">対象エピソードの ID。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>1 年以上ぶりの出現文字情報の一覧（文字コード順）。</returns>
    public async Task<IReadOnlyList<TitleCharRevivalStat>> GetTitleCharRevivalStatsAsync(
        int episodeId,
        CancellationToken ct = default)
    {
        const string sql = @"
WITH me AS (
  SELECT e.episode_id, e.on_air_at, e.total_ep_no
  FROM episodes e
  WHERE e.episode_id = @EpisodeId
    AND e.is_deleted = 0
    AND e.title_char_stats IS NOT NULL
    AND e.total_ep_no IS NOT NULL
),
me_chars AS (
  -- 当該話の使用文字キー（chars のキー配列）
  SELECT jt.ch
  FROM episodes e
  JOIN me ON me.episode_id = e.episode_id
  JOIN JSON_TABLE(
    JSON_KEYS(e.title_char_stats, '$.chars'),
    '$[*]' COLUMNS (ch VARCHAR(16) PATH '$')
  ) jt
),
history AS (
  -- 過去の出現（話単位で1回）
  SELECT e.episode_id, e.series_id, e.on_air_at, e.total_ep_no, e.series_ep_no, e.title_text, jt2.ch
  FROM episodes e
  JOIN JSON_TABLE(
    JSON_KEYS(e.title_char_stats, '$.chars'),
    '$[*]' COLUMNS (ch VARCHAR(16) PATH '$')
  ) jt2
  JOIN me_chars mc ON BINARY mc.ch = BINARY jt2.ch
  WHERE e.is_deleted = 0
    AND e.title_char_stats IS NOT NULL
    AND e.total_ep_no IS NOT NULL
),
last_prev AS (
  -- 対象話より前での最終出現
  SELECT h.ch, MAX(h.total_ep_no) AS last_total_no
  FROM history h
  JOIN me ON h.total_ep_no < me.total_ep_no
  GROUP BY h.ch
),
last_prev_join AS (
  SELECT lp.ch,
         h2.episode_id   AS last_episode_id,
         h2.series_id    AS last_series_id,
         h2.series_ep_no AS last_series_ep_no,
         h2.title_text   AS last_title_text,
         h2.on_air_at    AS last_on_air_at,
         lp.last_total_no
  FROM last_prev lp
  JOIN history h2 ON h2.total_ep_no = lp.last_total_no AND BINARY h2.ch = BINARY lp.ch
),
counts AS (
  -- 対象話までの出現“話数”カウント（n回目）
  SELECT h.ch, COUNT(DISTINCT h.episode_id) AS occ_idx
  FROM history h
  JOIN me ON h.total_ep_no <= me.total_ep_no
  GROUP BY h.ch
),
diffs AS (
  SELECT
    lpj.ch,
    lpj.last_episode_id,
    lpj.last_series_id,
    lpj.last_series_ep_no,
    lpj.last_title_text,
    lpj.last_on_air_at,
    (me.total_ep_no - lpj.last_total_no) AS episodes_since,
    -- 月数は日差→平均月長で四捨五入
    ROUND(TIMESTAMPDIFF(DAY, lpj.last_on_air_at, me.on_air_at) / 30.4375) AS months_rounded
  FROM last_prev_join lpj
  JOIN me
)
SELECT
  d.ch                                   AS `Char`,
  FLOOR(d.months_rounded / 12)           AS Years,
  (d.months_rounded % 12)                AS Months,
  d.episodes_since                       AS EpisodesSince,
  c.occ_idx                              AS OccurrenceIndex,
  d.last_episode_id                      AS LastEpisodeId,
  COALESCE(s.title, '')                  AS LastSeriesTitle,
  d.last_series_ep_no                    AS LastSeriesEpNo,
  d.last_title_text                      AS LastTitleText,
  d.last_on_air_at                       AS LastOnAirAt
FROM diffs d
JOIN counts c ON c.ch = d.ch
LEFT JOIN series s ON s.series_id = d.last_series_id
WHERE d.months_rounded >= 12
ORDER BY d.ch;";

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<TitleCharRevivalStat>(
            new CommandDefinition(sql, new { EpisodeId = episodeId }, cancellationToken: ct));
        return rows.ToList();
    }
}
