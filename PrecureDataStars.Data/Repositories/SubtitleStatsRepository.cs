
using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// サブタイトル文字統計の集計クエリ群（v1.3.0 後半追加）。
/// <para>
/// SiteBuilder の <c>/stats/subtitles/</c> 配下のページ群が使う読み取り専用の集計クエリを提供する。
/// 各メソッドは生 SQL を <see cref="Dapper"/> 経由で実行し、画面表示用の素朴な DTO を返す。
/// </para>
/// <para>
/// クエリは <c>episodes.title_char_stats</c>（JSON 列）に保存されている文字種別カウンタを集計するため、
/// MySQL 8 の JSON 関数（JSON_TABLE / JSON_KEYS / JSON_EXTRACT）と Unicode プロパティ正規表現
/// （<c>\p{Han}</c>）を活用する。MySQL 8.0+ 専用。
/// </para>
/// <para>
/// TOP N 仕様（v1.3.0 ブラッシュアップ続編で改訂）：limit パラメータは「Wimbledon 順位の上限」として
/// 解釈する。すなわち <c>WHERE `Rank` &lt;= @limit</c> でフィルタするので、limit=100 のとき
/// 同点 99 位が 3 件あれば 3 件すべて、同点 100 位が 5 件あれば 5 件すべてが返り、
/// 結果件数は limit を超えうる（同点最終位の取りこぼしを防ぐ）。
/// </para>
/// </summary>
public sealed class SubtitleStatsRepository
{
    private readonly IConnectionFactory _factory;

    public SubtitleStatsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// 文字ランキング（全文字種、出現回数の降順）TOP <paramref name="limit"/>。
    /// 同点同順（Wimbledon ランキング）。
    /// 出典 SQL：「使用文字一覧_出現数順.sql」
    /// </summary>
    public async Task<IReadOnlyList<CharRankingRow>> GetCharRankingAllAsync(int limit, CancellationToken ct = default)
    {
        // per_episode_chars は title_char_stats の chars キー（文字 → 出現回数の辞書）を JSON_TABLE で
        // フラット化したサブクエリ。ORDER BY のタイブレーカに最初の出現エピソード ID を使い、
        // 同点なら早く出現した文字を優先する（DENSE_RANK ではなく RANK にして「同点で順位が飛ぶ」運用）。
        const string sql = """
            WITH per_episode_chars AS (
              SELECT
                e.episode_id,
                k.ch                                            AS `char`,
                CONVERT(k.ch USING utf8mb4) COLLATE utf8mb4_bin AS char_bin,
                CAST(
                  JSON_UNQUOTE(
                    JSON_EXTRACT(
                      e.title_char_stats,
                      CONCAT('$.chars."', REPLACE(k.ch, '"', '\\"'), '"')
                    )
                  ) AS UNSIGNED
                ) AS cnt
              FROM episodes AS e
              JOIN JSON_TABLE(
                     JSON_KEYS(e.title_char_stats, '$.chars'),
                     '$[*]' COLUMNS (ch VARCHAR(191) PATH '$')
                   ) AS k
              WHERE e.is_deleted = 0
            ),
            grouped AS (
              SELECT
                MIN(`char` COLLATE utf8mb4_bin) AS `Char`,
                SUM(cnt)                        AS TotalCount,
                MIN(episode_id)                 AS FirstEpisodeId
              FROM per_episode_chars
              GROUP BY char_bin
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY TotalCount DESC) AS `Rank`,
                `Char`, TotalCount, FirstEpisodeId
              FROM grouped
            )
            SELECT `Rank`, `Char`, TotalCount, FirstEpisodeId
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, FirstEpisodeId ASC, `Char` ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharRankingRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 漢字限定ランキング（漢字＋繰り返し記号「々」）TOP <paramref name="limit"/>。同点同順。
    /// 出典 SQL：「歴代サブタイトル漢字ランキング.sql」
    /// </summary>
    public async Task<IReadOnlyList<CharRankingRow>> GetCharRankingKanjiAsync(int limit, CancellationToken ct = default)
    {
        const string sql = """
            WITH per_char AS (
              SELECT
                jt.ch AS ch,
                CAST(
                  JSON_EXTRACT(
                    e.title_char_stats,
                    CONCAT('$.chars."', REPLACE(jt.ch, '"', '\\"'), '"')
                  ) AS UNSIGNED
                ) AS cnt,
                e.episode_id
              FROM episodes e
              JOIN JSON_TABLE(
                     JSON_KEYS(e.title_char_stats, '$.chars'),
                     '$[*]' COLUMNS (ch VARCHAR(64) PATH '$')
                   ) jt
              WHERE e.is_deleted = 0 AND jt.ch REGEXP '\\p{Han}|[々]'
            ),
            grouped AS (
              SELECT ch AS `Char`, SUM(cnt) AS TotalCount, MIN(episode_id) AS FirstEpisodeId
              FROM per_char
              GROUP BY ch
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY TotalCount DESC) AS `Rank`,
                `Char`, TotalCount, FirstEpisodeId
              FROM grouped
            )
            SELECT `Rank`, `Char`, TotalCount, FirstEpisodeId
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, FirstEpisodeId ASC, `Char` ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharRankingRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// サブタイトル文字数ランキング（空白除く）。<paramref name="ascending"/> = true で短い順、false で長い順。TOP <paramref name="limit"/>。同点同順。
    /// 出典 SQL：「歴代サブタイトル文字数多い順/少ない順ランキング.sql」
    /// </summary>
    public async Task<IReadOnlyList<EpisodeStatRow>> GetTitleLengthRankingAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        // ORDER BY 方向を文字列補間で切り替え（パラメータでは ORDER BY 方向を切り替えられないため）。
        // ascending は呼び出し側でしか制御できない bool なので SQL インジェクションリスクなし。
        string direction = ascending ? "ASC" : "DESC";
        string sql = $"""
            WITH lengths AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                CHAR_LENGTH(REPLACE(REPLACE(e.title_text, ' ', ''), '　', '')) AS Value
              FROM episodes e
              LEFT JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Value {direction}) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, Value
              FROM lengths
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, Value
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, EpisodeId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeStatRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// サブタイトル漢字率ランキング（降順、TOP <paramref name="limit"/>）。同点同順。
    /// 漢字率＝（漢字＋々の文字数）÷（空白除く全文字数）。総文字数 0 のエピソードは除外。
    /// 出典 SQL：「歴代サブタイトル漢字率.sql」
    /// </summary>
    public async Task<IReadOnlyList<EpisodeRatioRow>> GetKanjiRatioRankingAsync(int limit, CancellationToken ct = default)
    {
        const string sql = """
            WITH base AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                CHAR_LENGTH(REGEXP_REPLACE(COALESCE(e.title_text, ''),'[^\\p{Han}々]','')) AS KanjiCount,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', ''))  AS TotalCount
              FROM episodes e
              LEFT JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
            ),
            valid AS (
              SELECT EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                     KanjiCount, TotalCount,
                     KanjiCount / TotalCount AS Ratio
              FROM base
              WHERE TotalCount > 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Ratio DESC) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                KanjiCount, TotalCount, Ratio
              FROM valid
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                   KanjiCount, TotalCount, Ratio
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, EpisodeId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeRatioRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ別の文字種別比率（漢字 / ひらがな / カタカナ / 英字 / 数字）。
    /// 出典 SQL：「歴代シリーズ・サブタイトル漢字率.sql」
    /// </summary>
    public async Task<IReadOnlyList<SeriesCharTypeRow>> GetCharTypeBreakdownBySeriesAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH cats AS (
              SELECT
                s.series_id AS SeriesId,
                s.title     AS SeriesTitle,
                s.slug      AS SeriesSlug,
                jt.k        AS Cat,
                CAST(JSON_EXTRACT(e.title_char_stats, CONCAT('$.categories."', jt.k, '"')) AS UNSIGNED) AS Cnt
              FROM episodes e
              JOIN series   s ON s.series_id = e.series_id
              JOIN JSON_TABLE(JSON_KEYS(e.title_char_stats, '$.categories'),
                              '$[*]' COLUMNS (k VARCHAR(20) PATH '$')) AS jt
              WHERE e.is_deleted = 0 AND jt.k IN ('Kanji','Hiragana','Katakana','Latin','Digits')
            )
            SELECT
              SeriesId, SeriesTitle, SeriesSlug,
              SUM(IF(Cat='Kanji',    Cnt, 0)) AS Kanji,
              SUM(IF(Cat='Hiragana', Cnt, 0)) AS Hiragana,
              SUM(IF(Cat='Katakana', Cnt, 0)) AS Katakana,
              SUM(IF(Cat='Latin',    Cnt, 0)) AS Latin,
              SUM(IF(Cat='Digits',   Cnt, 0)) AS Digits,
              SUM(Cnt) AS TotalCount
            FROM cats
            GROUP BY SeriesId, SeriesTitle, SeriesSlug
            ORDER BY SeriesId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesCharTypeRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ別の記号出現回数（16 種固定）。
    /// 出典 SQL：「歴代シリーズ使用文字記号シリーズごと.sql」
    /// </summary>
    public async Task<IReadOnlyList<SeriesSymbolRow>> GetSymbolCountsBySeriesAsync(CancellationToken ct = default)
    {
        // 「！」は半角 ! として title_char_stats に記録されている前提（Episodes エディタの正規化方針による）。
        // 同様に「？」は半角 ?、「（）」は半角 ()。これは元 SQL の仕様をそのまま踏襲。
        const string sql = """
            SELECT
              s.series_id AS SeriesId,
              s.title     AS SeriesTitle,
              s.slug      AS SeriesSlug,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."!"')  AS UNSIGNED)) AS Exclamation,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."?"')  AS UNSIGNED)) AS Question,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."・"') AS UNSIGNED)) AS MiddleDot,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."〜"') AS UNSIGNED)) AS Tilde,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."&"')  AS UNSIGNED)) AS Ampersand,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."("')  AS UNSIGNED)) AS ParenOpen,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars.")"')  AS UNSIGNED)) AS ParenClose,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."…"') AS UNSIGNED)) AS Ellipsis,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."、"') AS UNSIGNED)) AS Comma,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."♪"') AS UNSIGNED)) AS Note,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."☆"') AS UNSIGNED)) AS Star,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."♡"') AS UNSIGNED)) AS HeartOutline,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."。"') AS UNSIGNED)) AS Period,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."♥"') AS UNSIGNED)) AS HeartFilled,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."「"') AS UNSIGNED)) AS BracketOpen,
              SUM(CAST(JSON_EXTRACT(e.title_char_stats, '$.chars."」"') AS UNSIGNED)) AS BracketClose
            FROM episodes e
            JOIN series   s ON s.series_id = e.series_id
            WHERE e.is_deleted = 0
            GROUP BY s.series_id, s.title, s.slug
            ORDER BY s.series_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesSymbolRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ別の文字 TOP5 ランキング（DENSE_RANK で「同点同順、次は連番」、各シリーズ TOP5）。
    /// 出典 SQL：「歴代シリーズ使用文字ランキング.sql」
    /// </summary>
    public async Task<IReadOnlyList<SeriesCharRankRow>> GetTopCharsBySeriesAsync(int topN, CancellationToken ct = default)
    {
        const string sql = """
            WITH per_char AS (
              SELECT
                s.series_id AS SeriesId,
                s.title     AS SeriesTitle,
                s.slug      AS SeriesSlug,
                jt.ch       AS `Char`,
                CAST(
                  JSON_EXTRACT(e.title_char_stats,
                               CONCAT('$.chars."', REPLACE(jt.ch, '"', '\\"'), '"'))
                  AS UNSIGNED
                ) AS Cnt
              FROM episodes e
              JOIN series   s ON s.series_id = e.series_id
              JOIN JSON_TABLE(JSON_KEYS(e.title_char_stats, '$.chars'),
                              '$[*]' COLUMNS (ch VARCHAR(64) PATH '$')) jt
              WHERE e.is_deleted = 0
            ),
            ranked AS (
              SELECT
                SeriesId, SeriesTitle, SeriesSlug, `Char`,
                SUM(Cnt) AS Total,
                DENSE_RANK() OVER (PARTITION BY SeriesId ORDER BY SUM(Cnt) DESC) AS `Rank`
              FROM per_char
              GROUP BY SeriesId, SeriesTitle, SeriesSlug, `Char`
            )
            SELECT SeriesId, SeriesTitle, SeriesSlug, `Char`, Total, `Rank`
            FROM ranked
            WHERE `Rank` <= @topN
            ORDER BY SeriesId ASC, `Rank` ASC, `Char` ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesCharRankRow>(
            new CommandDefinition(sql, new { topN }, cancellationToken: ct));
        return rows.ToList();
    }

    // ──────────────────────────────────────────────────────
    // DTO 群（テンプレに渡す前に Generator 側で Url など解決した派生 DTO に変換する想定）
    // ──────────────────────────────────────────────────────

    /// <summary>文字単位ランキング 1 行。</summary>
    public sealed class CharRankingRow
    {
        public int Rank { get; set; }
        public string Char { get; set; } = "";
        public long TotalCount { get; set; }
        /// <summary>その文字が初めて登場したエピソードの ID（同点時のタイブレーク表示用）。</summary>
        public int FirstEpisodeId { get; set; }
    }

    /// <summary>エピソード単位の整数値ランキング 1 行（文字数・尺など）。</summary>
    public sealed class EpisodeStatRow
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public long Value { get; set; }
    }

    /// <summary>エピソード単位の比率値ランキング 1 行（漢字率など）。</summary>
    public sealed class EpisodeRatioRow
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public long KanjiCount { get; set; }
        public long TotalCount { get; set; }
        public double Ratio { get; set; }
    }

    /// <summary>シリーズ別文字種別カウント 1 行。</summary>
    public sealed class SeriesCharTypeRow
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public long Kanji { get; set; }
        public long Hiragana { get; set; }
        public long Katakana { get; set; }
        public long Latin { get; set; }
        public long Digits { get; set; }
        public long TotalCount { get; set; }
    }

    /// <summary>シリーズ別記号カウント 1 行（16 種固定）。</summary>
    public sealed class SeriesSymbolRow
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public long Exclamation { get; set; }   // !
        public long Question { get; set; }       // ?
        public long MiddleDot { get; set; }      // ・
        public long Tilde { get; set; }          // 〜
        public long Ampersand { get; set; }      // &
        public long ParenOpen { get; set; }      // (
        public long ParenClose { get; set; }     // )
        public long Ellipsis { get; set; }       // …
        public long Comma { get; set; }          // 、
        public long Note { get; set; }           // ♪
        public long Star { get; set; }           // ☆
        public long HeartOutline { get; set; }   // ♡
        public long Period { get; set; }         // 。
        public long HeartFilled { get; set; }    // ♥
        public long BracketOpen { get; set; }    // 「
        public long BracketClose { get; set; }   // 」
    }

    /// <summary>シリーズ別文字 TOP-N ランキング 1 行。</summary>
    public sealed class SeriesCharRankRow
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public string Char { get; set; } = "";
        public long Total { get; set; }
        public int Rank { get; set; }
    }

    // ──────────────────────────────────────────────────────
    // v1.3.0 ブラッシュアップ続編：Q-1 サブタイトル統計の 17 ページ化に対応する追加クエリ群。
    // 1 ページ 1 ランキング厳守の方針に合わせて、asc/desc を別メソッドに分離せず単一の
    // bool パラメータで切り替えられるものは流用、新たに必要になったシリーズ単位集計や
    // 記号率・記号初出現順は新メソッドとして追加する。
    // シリーズ単位の集計は TV のみ対象（series.kind_code = 'TV'）。スピンオフ・映画は除外。
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// サブタイトル文字数ランキング（既存 <see cref="GetTitleLengthRankingAsync"/>）の薄いラッパ：
    /// 多い順（降順）専用エイリアス。Generator から呼び出すときの可読性向上のため。
    /// </summary>
    public Task<IReadOnlyList<EpisodeStatRow>> GetTitleLengthDescAsync(int limit, CancellationToken ct = default)
        => GetTitleLengthRankingAsync(ascending: false, limit, ct);

    /// <summary>サブタイトル文字数ランキング（少ない順）。</summary>
    public Task<IReadOnlyList<EpisodeStatRow>> GetTitleLengthAscAsync(int limit, CancellationToken ct = default)
        => GetTitleLengthRankingAsync(ascending: true, limit, ct);

    /// <summary>
    /// シリーズ単位 平均文字数ランキング（TV のみ対象）。
    /// 平均文字数 = シリーズ内エピソードの空白除く文字数の単純平均。
    /// 同点同順（Wimbledon 形式）。
    /// </summary>
    /// <param name="ascending">true で少ない順、false で多い順。</param>
    public async Task<IReadOnlyList<SeriesAverageRow>> GetSeriesAverageLengthAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        // ORDER BY 方向を文字列補間で切替（パラメータ化不可）。bool なので SQL インジェクションリスクなし。
        string direction = ascending ? "ASC" : "DESC";
        string sql = $"""
            WITH per_episode AS (
              SELECT
                e.series_id,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', '')) AS Len
              FROM episodes e
              JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
                AND s.kind_code = 'TV'
            ),
            per_series AS (
              SELECT
                s.series_id   AS SeriesId,
                s.title       AS SeriesTitle,
                s.slug        AS SeriesSlug,
                AVG(p.Len)    AS Average,
                COUNT(*)      AS EpisodeCount
              FROM per_episode p
              JOIN series s ON s.series_id = p.series_id
              GROUP BY s.series_id, s.title, s.slug
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Average {direction}) AS `Rank`,
                SeriesId, SeriesTitle, SeriesSlug, Average, EpisodeCount
              FROM per_series
            )
            SELECT `Rank`, SeriesId, SeriesTitle, SeriesSlug, Average, EpisodeCount
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, SeriesId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesAverageRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// サブタイトル漢字率ランキング（既存 <see cref="GetKanjiRatioRankingAsync"/>）の昇降切替版。
    /// 漢字率＝（漢字＋々の文字数）÷（空白除く全文字数）。総文字数 0 のエピソードは除外。
    /// 同点同順（Wimbledon 形式）。
    /// </summary>
    /// <param name="ascending">true で低い順、false で高い順。</param>
    public async Task<IReadOnlyList<EpisodeRatioRow>> GetKanjiRateEpisodeAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        string direction = ascending ? "ASC" : "DESC";
        string sql = $$"""
            WITH base AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                CHAR_LENGTH(REGEXP_REPLACE(COALESCE(e.title_text, ''),'[^\\p{Han}々]','')) AS KanjiCount,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', ''))  AS TotalCount
              FROM episodes e
              LEFT JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
            ),
            valid AS (
              SELECT EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                     KanjiCount, TotalCount,
                     KanjiCount / TotalCount AS Ratio
              FROM base
              WHERE TotalCount > 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Ratio {{direction}}) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, KanjiCount, TotalCount, Ratio
              FROM valid
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, KanjiCount, TotalCount, Ratio
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, EpisodeId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeRatioRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ単位 漢字率ランキング（TV のみ対象）。
    /// シリーズ漢字率＝シリーズ内エピソードの (漢字＋々) 合計 ÷ 空白除く全文字 合計。総文字数 0 のシリーズは除外。
    /// 同点同順。
    /// </summary>
    /// <param name="ascending">true で低い順、false で高い順。</param>
    public async Task<IReadOnlyList<SeriesRatioRow>> GetKanjiRateSeriesAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        string direction = ascending ? "ASC" : "DESC";
        string sql = $$"""
            WITH per_episode AS (
              SELECT
                e.series_id,
                CHAR_LENGTH(REGEXP_REPLACE(COALESCE(e.title_text, ''),'[^\\p{Han}々]','')) AS KanjiCount,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', ''))  AS TotalCount
              FROM episodes e
              JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
                AND s.kind_code = 'TV'
            ),
            per_series AS (
              SELECT
                s.series_id        AS SeriesId,
                s.title            AS SeriesTitle,
                s.slug             AS SeriesSlug,
                SUM(p.KanjiCount)  AS KanjiCount,
                SUM(p.TotalCount)  AS TotalCount
              FROM per_episode p
              JOIN series s ON s.series_id = p.series_id
              GROUP BY s.series_id, s.title, s.slug
              HAVING SUM(p.TotalCount) > 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY (KanjiCount / TotalCount) {{direction}}) AS `Rank`,
                SeriesId, SeriesTitle, SeriesSlug, KanjiCount, TotalCount,
                (KanjiCount / TotalCount) AS Ratio
              FROM per_series
            )
            SELECT `Rank`, SeriesId, SeriesTitle, SeriesSlug, KanjiCount, TotalCount, Ratio
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, SeriesId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesRatioRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// エピソード単位 記号率ランキング。
    /// 記号率＝（記号扱いの文字数）÷（空白除く全文字数）。
    /// 「記号」の定義は「ひらがな・カタカナ・漢字・英字・数字以外」で、文字統計 JSON の categories 配下から
    /// Symbols（記号）+ Punct（句読点）+ Emoji（絵文字）+ Other（その他）の 4 カテゴリを合算する。
    /// 同点同順。
    /// </summary>
    /// <param name="ascending">true で低い順、false で高い順。</param>
    public async Task<IReadOnlyList<EpisodeRatioRow>> GetSymbolRateEpisodeAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        string direction = ascending ? "ASC" : "DESC";
        // 「記号」の集計対象は categories.Symbols / Punct / Emoji / Other の 4 カテゴリ。
        // TitleCharStatsBuilder は文字種別カウンタを categories.{Hiragana,Katakana,Kanji,Latin,Digits,Emoji,Punct,Symbols,Other} の
        // 9 カテゴリで持つので、ひらがな・カタカナ・漢字・英字・数字以外の 4 カテゴリを足し合わせれば「記号扱い文字」が得られる。
        string sql = $"""
            WITH base AS (
              SELECT
                e.episode_id   AS EpisodeId,
                s.title        AS SeriesTitle,
                s.slug         AS SeriesSlug,
                e.series_ep_no AS SeriesEpNo,
                e.title_text   AS TitleText,
                (
                  COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Symbols') AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Punct')   AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Emoji')   AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Other')   AS UNSIGNED), 0)
                ) AS SymbolCount,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', ''))  AS TotalCount
              FROM episodes e
              LEFT JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
            ),
            valid AS (
              SELECT EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText,
                     SymbolCount AS KanjiCount,  -- DTO 互換: KanjiCount フィールドに記号件数を流用
                     TotalCount,
                     SymbolCount / TotalCount AS Ratio
              FROM base
              WHERE TotalCount > 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY Ratio {direction}) AS `Rank`,
                EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, KanjiCount, TotalCount, Ratio
              FROM valid
            )
            SELECT `Rank`, EpisodeId, SeriesTitle, SeriesSlug, SeriesEpNo, TitleText, KanjiCount, TotalCount, Ratio
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, EpisodeId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeRatioRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ単位 記号率ランキング（テレビシリーズのみ対象）。
    /// シリーズ記号率＝シリーズ内エピソードの記号扱い文字合計 ÷ 空白除く全文字合計。
    /// 「記号」の定義は「ひらがな・カタカナ・漢字・英字・数字以外」で、文字統計 JSON の categories 配下から
    /// Symbols（記号）+ Punct（句読点）+ Emoji（絵文字）+ Other（その他）の 4 カテゴリを合算する。
    /// 同点同順。
    /// </summary>
    /// <param name="ascending">true で低い順、false で高い順。</param>
    public async Task<IReadOnlyList<SeriesRatioRow>> GetSymbolRateSeriesAsync(bool ascending, int limit, CancellationToken ct = default)
    {
        string direction = ascending ? "ASC" : "DESC";
        string sql = $"""
            WITH per_episode AS (
              SELECT
                e.series_id,
                (
                  COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Symbols') AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Punct')   AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Emoji')   AS UNSIGNED), 0)
                + COALESCE(CAST(JSON_EXTRACT(e.title_char_stats, '$.categories.Other')   AS UNSIGNED), 0)
                ) AS SymbolCount,
                CHAR_LENGTH(REPLACE(REPLACE(COALESCE(e.title_text, ''), ' ', ''), '　', '')) AS TotalCount
              FROM episodes e
              JOIN series s ON s.series_id = e.series_id
              WHERE e.is_deleted = 0
                AND s.kind_code = 'TV'
            ),
            per_series AS (
              SELECT
                s.series_id         AS SeriesId,
                s.title             AS SeriesTitle,
                s.slug              AS SeriesSlug,
                SUM(p.SymbolCount)  AS KanjiCount,  -- DTO 互換: SeriesRatioRow.KanjiCount に記号合計を流用
                SUM(p.TotalCount)   AS TotalCount
              FROM per_episode p
              JOIN series s ON s.series_id = p.series_id
              GROUP BY s.series_id, s.title, s.slug
              HAVING SUM(p.TotalCount) > 0
            ),
            ranked AS (
              SELECT
                RANK() OVER (ORDER BY (KanjiCount / TotalCount) {direction}) AS `Rank`,
                SeriesId, SeriesTitle, SeriesSlug, KanjiCount, TotalCount,
                (KanjiCount / TotalCount) AS Ratio
              FROM per_series
            )
            SELECT `Rank`, SeriesId, SeriesTitle, SeriesSlug, KanjiCount, TotalCount, Ratio
            FROM ranked
            WHERE `Rank` <= @limit
            ORDER BY `Rank` ASC, SeriesId ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesRatioRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 記号出現回数（全件、初使用が早い順）。
    /// 「初使用」＝各記号文字が初めて使われたエピソードの放送日。同放送日のときは episode_id 昇順。
    /// 漢字限定や TOP 100 ランキングとは違い、TOP N の切捨てはせず全記号を並べる。
    /// 戻り値には初使用エピソードのシリーズタイトル・話数・サブタイトル・放送日を含めて、
    /// テンプレ側で「初使用」グループ列の各セルに表示できるようにする。
    /// </summary>
    public async Task<IReadOnlyList<SymbolFirstAppearRow>> GetSymbolsByFirstAppearAsync(CancellationToken ct = default)
    {
        // 段階：(1) 文字 × エピソードの行を作る → (2) 文字単位に集計 + 初使用 episode_id を求める →
        // (3) 初使用 episode_id でエピソードと series を JOIN し、表示用の付帯情報を合流。
        const string sql = """
            WITH per_episode_chars AS (
              SELECT
                e.episode_id,
                e.on_air_date,
                jt.ch                                            AS `char`,
                CONVERT(jt.ch USING utf8mb4) COLLATE utf8mb4_bin AS char_bin,
                CAST(
                  JSON_EXTRACT(
                    e.title_char_stats,
                    CONCAT('$.chars."', REPLACE(jt.ch, '"', '\\"'), '"')
                  ) AS UNSIGNED
                ) AS cnt
              FROM episodes e
              JOIN JSON_TABLE(
                     JSON_KEYS(e.title_char_stats, '$.chars'),
                     '$[*]' COLUMNS (ch VARCHAR(64) PATH '$')
                   ) jt
              WHERE e.is_deleted = 0
                -- 記号判定：漢字でもひらがなでもカタカナでも英字でも数字でも空白でもない
                AND jt.ch NOT REGEXP '\\p{Han}|[々]|\\p{Hiragana}|\\p{Katakana}|[A-Za-z]|[0-9]|[ 　]'
            ),
            grouped AS (
              SELECT
                MIN(`char` COLLATE utf8mb4_bin) AS `Char`,
                char_bin,
                SUM(cnt)                       AS TotalCount,
                MIN(on_air_date)               AS FirstBroadcastDate,
                MIN(episode_id)                AS FirstEpisodeId
              FROM per_episode_chars
              GROUP BY char_bin
            )
            SELECT
              g.`Char`                  AS `Char`,
              g.TotalCount              AS TotalCount,
              g.FirstBroadcastDate      AS FirstBroadcastDate,
              g.FirstEpisodeId          AS FirstEpisodeId,
              s.title                   AS FirstSeriesTitle,
              s.slug                    AS FirstSeriesSlug,
              e.series_ep_no            AS FirstSeriesEpNo,
              e.title_text              AS FirstTitleText
            FROM grouped g
            JOIN episodes e ON e.episode_id = g.FirstEpisodeId
            JOIN series   s ON s.series_id  = e.series_id
            ORDER BY g.FirstBroadcastDate ASC, g.FirstEpisodeId ASC, g.`Char` ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SymbolFirstAppearRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    // ──────────────────────────────────────────────────────
    // 追加 DTO（v1.3.0 ブラッシュアップ続編）
    // ──────────────────────────────────────────────────────

    /// <summary>シリーズ単位の数値平均ランキング 1 行（平均文字数など）。</summary>
    public sealed class SeriesAverageRow
    {
        public int Rank { get; set; }
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        /// <summary>平均値（小数）。</summary>
        public double Average { get; set; }
        /// <summary>サンプル件数（シリーズ内のエピソード数）。表示参考用。</summary>
        public int EpisodeCount { get; set; }
    }

    /// <summary>シリーズ単位の比率ランキング 1 行（漢字率・記号率など）。</summary>
    public sealed class SeriesRatioRow
    {
        public int Rank { get; set; }
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        /// <summary>分子（漢字合計、記号合計など）。集計対象に応じて意味が変わる。</summary>
        public long KanjiCount { get; set; }
        /// <summary>分母（空白除く全文字合計）。</summary>
        public long TotalCount { get; set; }
        /// <summary>比率（0.0〜1.0）。</summary>
        public double Ratio { get; set; }
    }

    /// <summary>
    /// 記号の初出現エピソード情報を含む 1 行
    /// （v1.3.0 ブラッシュアップ続編で表示用にシリーズ・話数・サブタイトル列を追加）。
    /// 「初使用」グループ列の各セル（シリーズ / 話数 / サブタイトル / 放送日）への展開に利用する。
    /// </summary>
    public sealed class SymbolFirstAppearRow
    {
        public string Char { get; set; } = "";
        public long TotalCount { get; set; }
        /// <summary>その記号が初めて登場したエピソードの放送日。</summary>
        public DateOnly? FirstBroadcastDate { get; set; }
        public int FirstEpisodeId { get; set; }
        /// <summary>初使用エピソードが属するシリーズの表示用タイトル。</summary>
        public string FirstSeriesTitle { get; set; } = "";
        /// <summary>初使用エピソードが属するシリーズのスラッグ（URL 構築用）。</summary>
        public string FirstSeriesSlug { get; set; } = "";
        /// <summary>初使用エピソードのシリーズ内話数。</summary>
        public int FirstSeriesEpNo { get; set; }
        /// <summary>初使用エピソードのサブタイトル本文。</summary>
        public string FirstTitleText { get; set; } = "";
    }
}
