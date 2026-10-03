using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Data.Repositories;

/// <summary>series テーブルの CRUD リポジトリ。 Dapper で <see cref="DateOnly"/> / <see cref="DateOnly?"/> を扱うための <see cref="SqlMapper.TypeHandler{T}"/> を静的コンストラクタで登録している。</summary>
public sealed class SeriesRepository : RepositoryBase
{
    /// <summary>slug の書式検証用正規表現（<c>^[a-z0-9-]+$</c>）。</summary>
    private static readonly Regex SlugRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>静的コンストラクタ: Dapper に DateOnly / DateOnly? / bool? (TINYINT) の TypeHandler を登録する。</summary>
    static SeriesRepository()
    {
        // Dapper に DateOnly / DateOnly? を扱わせる
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableBoolTinyIntHandler());
    }

    /// <summary><see cref="SeriesRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesRepository(IConnectionFactory factory) : base(factory) { }

    //  SELECT 列リストの共通定義（全メソッドで同一カラムを取得する）

    /// <summary>論理削除されていない全シリーズを開始日→ID 順で取得する。</summary>
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
              amazon_prime_video_asin       AS AmazonPrimeVideoAsin,
              youtube_trailer_url           AS YoutubeTrailerUrl,
              film_rating_no                AS FilmRatingNo,
              film_cj_mark                  AS FilmCjMark,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              subtitle_kerning              AS SubtitleKerning,
              subtitle_letter_spacing_em    AS SubtitleLetterSpacingEm,
              subtitle_ruby_letter_spacing_em AS SubtitleRubyLetterSpacingEm,
              font_subtitle_ruby            AS FontSubtitleRuby,
              subtitle_ruby_size_ratio      AS SubtitleRubySizeRatio,
              subtitle_ruby_raise_ratio     AS SubtitleRubyRaiseRatio,
              subtitle_ruby_oblique_deg     AS SubtitleRubyObliqueDeg,
              subtitle_line_gap_ratio       AS SubtitleLineGapRatio,
              subtitle_line_gap_ratio_3     AS SubtitleLineGapRatio3,
              subtitle_ruby_overhang_ratio  AS SubtitleRubyOverhangRatio,
              subtitle_ruby_line_edge       AS SubtitleRubyLineEdge,
              subtitle_ruby_grouping        AS SubtitleRubyGrouping,
              hide_storyboard_role  AS HideStoryboardRole,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE is_deleted = 0
            ORDER BY start_date, series_id;
        """;

        return await QueryListAsync<Series>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>kind_code = 'TV' のシリーズのみを開始日→ID 順で取得する。 エピソード編集画面の TV シリーズ一覧表示に使用される。</summary>
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
              amazon_prime_video_asin       AS AmazonPrimeVideoAsin,
              youtube_trailer_url           AS YoutubeTrailerUrl,
              film_rating_no                AS FilmRatingNo,
              film_cj_mark                  AS FilmCjMark,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              subtitle_kerning              AS SubtitleKerning,
              subtitle_letter_spacing_em    AS SubtitleLetterSpacingEm,
              subtitle_ruby_letter_spacing_em AS SubtitleRubyLetterSpacingEm,
              font_subtitle_ruby            AS FontSubtitleRuby,
              subtitle_ruby_size_ratio      AS SubtitleRubySizeRatio,
              subtitle_ruby_raise_ratio     AS SubtitleRubyRaiseRatio,
              subtitle_ruby_oblique_deg     AS SubtitleRubyObliqueDeg,
              subtitle_line_gap_ratio       AS SubtitleLineGapRatio,
              subtitle_line_gap_ratio_3     AS SubtitleLineGapRatio3,
              subtitle_ruby_overhang_ratio  AS SubtitleRubyOverhangRatio,
              subtitle_ruby_line_edge       AS SubtitleRubyLineEdge,
              subtitle_ruby_grouping        AS SubtitleRubyGrouping,
              hide_storyboard_role  AS HideStoryboardRole,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE is_deleted = 0 AND kind_code = 'TV'
            ORDER BY start_date, series_id;
        """;

        return await QueryListAsync<Series>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>主キーでシリーズを 1 件取得する（論理削除レコードも含む）。</summary>
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
              amazon_prime_video_asin       AS AmazonPrimeVideoAsin,
              youtube_trailer_url           AS YoutubeTrailerUrl,
              film_rating_no                AS FilmRatingNo,
              film_cj_mark                  AS FilmCjMark,
              vod_intro        AS VodIntro,
              font_subtitle    AS FontSubtitle,
              subtitle_kerning              AS SubtitleKerning,
              subtitle_letter_spacing_em    AS SubtitleLetterSpacingEm,
              subtitle_ruby_letter_spacing_em AS SubtitleRubyLetterSpacingEm,
              font_subtitle_ruby            AS FontSubtitleRuby,
              subtitle_ruby_size_ratio      AS SubtitleRubySizeRatio,
              subtitle_ruby_raise_ratio     AS SubtitleRubyRaiseRatio,
              subtitle_ruby_oblique_deg     AS SubtitleRubyObliqueDeg,
              subtitle_line_gap_ratio       AS SubtitleLineGapRatio,
              subtitle_line_gap_ratio_3     AS SubtitleLineGapRatio3,
              subtitle_ruby_overhang_ratio  AS SubtitleRubyOverhangRatio,
              subtitle_ruby_line_edge       AS SubtitleRubyLineEdge,
              subtitle_ruby_grouping        AS SubtitleRubyGrouping,
              hide_storyboard_role  AS HideStoryboardRole,
              created_by       AS CreatedBy,
              updated_by       AS UpdatedBy,
              is_deleted       AS IsDeleted
            FROM series
            WHERE series_id = @seriesId
            LIMIT 1;
        """;

        return await QuerySingleOrDefaultAsync<Series>(sql, new { seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>新しいシリーズを INSERT し、自動採番された series_id を返す。</summary>
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
              abc_official_site_url, amazon_prime_video_asin, youtube_trailer_url, film_rating_no, film_cj_mark, vod_intro, font_subtitle,
              subtitle_kerning, subtitle_letter_spacing_em, subtitle_ruby_letter_spacing_em,
              font_subtitle_ruby, subtitle_ruby_size_ratio, subtitle_ruby_raise_ratio, subtitle_ruby_oblique_deg,
              subtitle_line_gap_ratio, subtitle_line_gap_ratio_3, subtitle_ruby_overhang_ratio, subtitle_ruby_line_edge, subtitle_ruby_grouping,
              hide_storyboard_role,
              created_by, updated_by, is_deleted
            ) VALUES (
              @KindCode, @ParentSeriesId, @RelationToParent, @SeqInParent,
              @Title, @TitleKana, @TitleShort, @TitleShortKana,
              @TitleEn, @TitleShortEn,
              @Slug, @StartDate, @EndDate, @Episodes, @RunTimeSeconds,
              @ToeiAnimOfficialSiteUrl, @ToeiAnimLineupUrl,
              @AbcOfficialSiteUrl, @AmazonPrimeVideoAsin, @YoutubeTrailerUrl, @FilmRatingNo, @FilmCjMark, @VodIntro, @FontSubtitle,
              @SubtitleKerning, @SubtitleLetterSpacingEm, @SubtitleRubyLetterSpacingEm,
              @FontSubtitleRuby, @SubtitleRubySizeRatio, @SubtitleRubyRaiseRatio, @SubtitleRubyObliqueDeg,
              @SubtitleLineGapRatio, @SubtitleLineGapRatio3, @SubtitleRubyOverhangRatio, @SubtitleRubyLineEdge, @SubtitleRubyGrouping,
              @HideStoryboardRole,
              @CreatedBy, @UpdatedBy, 0
            );
            SELECT LAST_INSERT_ID();
        """;

        var id = await ExecuteScalarAsync<int>(sql, s, ct).ConfigureAwait(false);
        return id;
    }

    /// <summary>既存のシリーズを UPDATE する。主キー (<see cref="Series.SeriesId"/>) が一致するレコードを更新する。 論理削除の切り替えは本メソッドの対象外。</summary>
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
              amazon_prime_video_asin = @AmazonPrimeVideoAsin,
              youtube_trailer_url = @YoutubeTrailerUrl,
              film_rating_no = @FilmRatingNo,
              film_cj_mark = @FilmCjMark,
              vod_intro = @VodIntro,
              font_subtitle = @FontSubtitle,
              subtitle_kerning = @SubtitleKerning,
              subtitle_letter_spacing_em = @SubtitleLetterSpacingEm,
              subtitle_ruby_letter_spacing_em = @SubtitleRubyLetterSpacingEm,
              font_subtitle_ruby = @FontSubtitleRuby,
              subtitle_ruby_size_ratio = @SubtitleRubySizeRatio,
              subtitle_ruby_raise_ratio = @SubtitleRubyRaiseRatio,
              subtitle_ruby_oblique_deg = @SubtitleRubyObliqueDeg,
              subtitle_line_gap_ratio = @SubtitleLineGapRatio,
              subtitle_line_gap_ratio_3 = @SubtitleLineGapRatio3,
              subtitle_ruby_overhang_ratio = @SubtitleRubyOverhangRatio,
              subtitle_ruby_line_edge = @SubtitleRubyLineEdge,
              subtitle_ruby_grouping = @SubtitleRubyGrouping,
              hide_storyboard_role = @HideStoryboardRole,
              updated_by = @UpdatedBy
            WHERE series_id = @SeriesId;
        """;

        await ExecuteAsync(sql, s, ct).ConfigureAwait(false);
    }

    //  Dapper TypeHandler（DateOnly / bool? ↔ MySQL）

    /// <summary>Dapper 用 TypeHandler: MySQL の DATE/DATETIME 型と .NET の <see cref="DateOnly"/> を相互変換する。</summary>
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

    /// <summary>Dapper 用 TypeHandler: MySQL の DATE/DATETIME 型と .NET の Nullable{DateOnly} を相互変換する。</summary>
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

    /// <summary>Dapper 用 TypeHandler: MySQL の TINYINT (0/1) と .NET の Nullable{Boolean} を相互変換する。</summary>
    public sealed class NullableBoolTinyIntHandler : SqlMapper.TypeHandler<bool?>
    {
        public override bool? Parse(object value)
            => value is null or DBNull ? null
             : Convert.ToInt32(value) switch { 0 => false, 1 => true, _ => null };

        public override void SetValue(IDbDataParameter parameter, bool? value)
            => parameter.Value = value is null ? DBNull.Value : ((bool)value ? 1 : 0);
    }
}
