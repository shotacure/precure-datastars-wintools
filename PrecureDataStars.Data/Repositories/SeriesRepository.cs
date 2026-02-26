using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series テーブルの CRUD リポジトリ。
/// <para>
/// Dapper で <see cref="DateOnly"/> / <see cref="DateOnly?"/> を扱うための
/// <see cref="SqlMapper.TypeHandler{T}"/> を静的コンストラクタで登録している。
/// </para>
/// </summary>
public sealed class SeriesRepository
{
    /// <summary>slug の書式検証用正規表現（<c>^[a-z0-9-]+$</c>）。</summary>
    private static readonly Regex SlugRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    private readonly IConnectionFactory _factory;

    /// <summary>
    /// 静的コンストラクタ: Dapper に DateOnly / DateOnly? / bool? (TINYINT) の TypeHandler を登録する。
    /// MySQL の DATE/TINYINT 型と .NET の DateOnly/bool? 間の相互変換を実現する。
    /// </summary>
    static SeriesRepository()
    {
        // Dapper に DateOnly / DateOnly? を扱わせる
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableBoolTinyIntHandler());
    }

    /// <summary>
    /// <see cref="SeriesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // ────────────────────────────────────────────────
    //  SELECT 列リストの共通定義（全メソッドで同一カラムを取得する）
    // ────────────────────────────────────────────────

    /// <summary>
    /// 論理削除されていない全シリーズを開始日→ID 順で取得する。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>有効なシリーズの一覧。</returns>
    public async Task<IReadOnlyList<Series>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              series_id        AS SeriesId,
              kind_code        AS KindCode,
              parent_series_id AS ParentSeriesId,
              relation_to_parent AS RelationToParent,
              seq_in_parent    AS SeqInParent,
              title            AS Title,
              title_kana       AS TitleKana,
              title_short      AS TitleShort,
              title_short_kana AS TitleShortKana,
              title_en         AS TitleEn,
              title_short_en   AS TitleShortEn,
              slug             AS Slug,
              start_date       AS StartDate,
              end_date         AS EndDate,
              episodes         AS Episodes,
              run_time_seconds AS RunTimeSeconds,
              toei_anim_official_site_url   AS ToeiAnimOfficialSiteUrl,
              toei_anim_lineup_url          AS ToeiAnimLineupUrl,
              abc_official_site_url         AS AbcOfficialSiteUrl,
              amazon_prime_distribution_url AS AmazonPrimeDistributionUrl,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE is_deleted = 0
            ORDER BY start_date, series_id;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Series>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// kind_code = 'TV' のシリーズのみを開始日→ID 順で取得する。
    /// エピソード編集画面の TV シリーズ一覧表示に使用される。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>TV シリーズの一覧。</returns>
    public async Task<IReadOnlyList<Series>> GetTvSeriesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              series_id        AS SeriesId,
              kind_code        AS KindCode,
              parent_series_id AS ParentSeriesId,
              relation_to_parent AS RelationToParent,
              seq_in_parent    AS SeqInParent,
              title            AS Title,
              title_kana       AS TitleKana,
              title_short      AS TitleShort,
              title_short_kana AS TitleShortKana,
              title_en         AS TitleEn,
              title_short_en   AS TitleShortEn,
              slug             AS Slug,
              start_date       AS StartDate,
              end_date         AS EndDate,
              episodes         AS Episodes,
              run_time_seconds AS RunTimeSeconds,
              toei_anim_official_site_url   AS ToeiAnimOfficialSiteUrl,
              toei_anim_lineup_url          AS ToeiAnimLineupUrl,
              abc_official_site_url         AS AbcOfficialSiteUrl,
              amazon_prime_distribution_url AS AmazonPrimeDistributionUrl,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE is_deleted = 0 AND kind_code = 'TV'
            ORDER BY start_date, series_id;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Series>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 主キーでシリーズを 1 件取得する（論理削除レコードも含む）。
    /// </summary>
    /// <param name="seriesId">シリーズ ID。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>見つかった場合は <see cref="Series"/>、存在しなければ <c>null</c>。</returns>
    public async Task<Series?> GetByIdAsync(int seriesId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              series_id        AS SeriesId,
              kind_code        AS KindCode,
              parent_series_id AS ParentSeriesId,
              relation_to_parent AS RelationToParent,
              seq_in_parent    AS SeqInParent,
              title            AS Title,
              title_kana       AS TitleKana,
              title_short      AS TitleShort,
              title_short_kana AS TitleShortKana,
              title_en         AS TitleEn,
              title_short_en   AS TitleShortEn,
              slug             AS Slug,
              start_date       AS StartDate,
              end_date         AS EndDate,
              episodes         AS Episodes,
              run_time_seconds AS RunTimeSeconds,
              toei_anim_official_site_url   AS ToeiAnimOfficialSiteUrl,
              toei_anim_lineup_url          AS ToeiAnimLineupUrl,
              abc_official_site_url         AS AbcOfficialSiteUrl,
              amazon_prime_distribution_url AS AmazonPrimeDistributionUrl,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE series_id = @seriesId
            LIMIT 1;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Series>(
            new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
    }

    /// <summary>
    /// 新しいシリーズを INSERT し、自動採番された series_id を返す。
    /// </summary>
    /// <param name="s">挿入対象のシリーズ。Title / KindCode / Slug は必須。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>新しい series_id。</returns>
    /// <exception cref="ArgumentException">必須項目が未設定、または slug が不正な書式の場合。</exception>
    public async Task<int> InsertAsync(Series s, CancellationToken ct = default)
    {
        // 必須フィールドのバリデーション（スキーマの NOT NULL / CHECK 制約に対応）
        if (string.IsNullOrWhiteSpace(s.Title)) throw new ArgumentException("Title is required.", nameof(s));
        if (string.IsNullOrWhiteSpace(s.KindCode)) throw new ArgumentException("KindCode is required.", nameof(s));
        if (string.IsNullOrWhiteSpace(s.Slug) || !SlugRegex.IsMatch(s.Slug))
            throw new ArgumentException("Slug must match ^[a-z0-9-]+$.", nameof(s));

        const string sql = """
            INSERT INTO series(
              kind_code, parent_series_id, relation_to_parent, seq_in_parent,
              title, title_kana, title_short, title_short_kana,
              title_en, title_short_en,
              slug, start_date, end_date, episodes, run_time_seconds,
              toei_anim_official_site_url, toei_anim_lineup_url,
              abc_official_site_url, amazon_prime_distribution_url, vod_intro, font_subtitle,
              created_by, updated_by, is_deleted
            ) VALUES (
              @KindCode, @ParentSeriesId, @RelationToParent, @SeqInParent,
              @Title, @TitleKana, @TitleShort, @TitleShortKana,
              @TitleEn, @TitleShortEn,
              @Slug, @StartDate, @EndDate, @Episodes, @RunTimeSeconds,
              @ToeiAnimOfficialSiteUrl, @ToeiAnimLineupUrl,
              @AbcOfficialSiteUrl, @AmazonPrimeDistributionUrl, @VodIntro, @FontSubtitle,
              @CreatedBy, @UpdatedBy, 0
            );
            SELECT LAST_INSERT_ID();
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, s, cancellationToken: ct));
        return id;
    }

    /// <summary>
    /// 既存のシリーズを UPDATE する。主キー (<see cref="Series.SeriesId"/>) が一致するレコードを更新する。
    /// 論理削除の切り替えは本メソッドの対象外。
    /// </summary>
    /// <param name="s">更新対象のシリーズ。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">必須項目が未設定、または slug が不正な書式の場合。</exception>
    public async Task UpdateAsync(Series s, CancellationToken ct = default)
    {
        if (s.SeriesId <= 0) throw new ArgumentException("Invalid SeriesId.", nameof(s));
        if (string.IsNullOrWhiteSpace(s.Title)) throw new ArgumentException("Title is required.", nameof(s));
        if (string.IsNullOrWhiteSpace(s.KindCode)) throw new ArgumentException("KindCode is required.", nameof(s));
        if (string.IsNullOrWhiteSpace(s.Slug) || !SlugRegex.IsMatch(s.Slug))
            throw new ArgumentException("Slug must match ^[a-z0-9-]+$.", nameof(s));

        const string sql = """
            UPDATE series SET
              kind_code = @KindCode,
              parent_series_id = @ParentSeriesId,
              relation_to_parent = @RelationToParent,
              seq_in_parent = @SeqInParent,
              title = @Title,
              title_kana = @TitleKana,
              title_short = @TitleShort,
              title_short_kana = @TitleShortKana,
              title_en = @TitleEn,
              title_short_en = @TitleShortEn,
              slug = @Slug,
              start_date = @StartDate,
              end_date = @EndDate,
              episodes = @Episodes,
              run_time_seconds = @RunTimeSeconds,
              toei_anim_official_site_url = @ToeiAnimOfficialSiteUrl,
              toei_anim_lineup_url = @ToeiAnimLineupUrl,
              abc_official_site_url = @AbcOfficialSiteUrl,
              amazon_prime_distribution_url = @AmazonPrimeDistributionUrl,
              vod_intro = @VodIntro,
              font_subtitle = @FontSubtitle,
              updated_by = @UpdatedBy
            WHERE series_id = @SeriesId;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, s, cancellationToken: ct));
    }

    // ────────────────────────────────────────────────
    //  Dapper TypeHandler（DateOnly / bool? ↔ MySQL）
    // ────────────────────────────────────────────────

    /// <summary>
    /// Dapper 用 TypeHandler: MySQL の DATE/DATETIME 型と .NET の <see cref="DateOnly"/> を相互変換する。
    /// </summary>
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value)
            => value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                MySqlDateTime md => DateOnly.FromDateTime(md.GetDateTime()),
                string s => DateOnly.Parse(s),
                _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
            };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
            => parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>
    /// Dapper 用 TypeHandler: MySQL の DATE/DATETIME 型と .NET の <see cref="Nullable{DateOnly}"/> を相互変換する。
    /// NULL / DBNull は <c>null</c> として扱う。
    /// </summary>
    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override DateOnly? Parse(object value)
            => value is null || value is DBNull ? null
             : value is DateTime dt ? DateOnly.FromDateTime(dt)
             : value is MySqlDateTime md ? DateOnly.FromDateTime(md.GetDateTime())
             : value is string s ? DateOnly.Parse(s)
             : DateOnly.FromDateTime(Convert.ToDateTime(value));

        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
            => parameter.Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
    }

    /// <summary>
    /// Dapper 用 TypeHandler: MySQL の TINYINT (0/1) と .NET の <see cref="Nullable{Boolean}"/> を相互変換する。
    /// 0=false, 1=true, NULL=null。
    /// </summary>
    public sealed class NullableBoolTinyIntHandler : SqlMapper.TypeHandler<bool?>
    {
        public override bool? Parse(object value)
            => value is null or DBNull ? null
             : Convert.ToInt32(value) switch { 0 => false, 1 => true, _ => null };

        public override void SetValue(IDbDataParameter parameter, bool? value)
            => parameter.Value = value is null ? DBNull.Value : ((bool)value ? 1 : 0);
    }
}
