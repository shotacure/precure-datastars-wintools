using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// discs テーブル（物理ディスク）の CRUD リポジトリ。
/// <para>
/// 品番（catalog_no）を主キーとする。CDAnalyzer/BDAnalyzer からの登録・更新、
/// 商品に紐づくディスク一覧取得、MCN/CDDB-ID 等での照合検索に対応する。
/// </para>
/// </summary>
public sealed class DiscsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="DiscsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public DiscsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // SELECT 共通列定義（Disc エンティティのプロパティ名に合わせる）
    private const string SelectColumns = """
          catalog_no                  AS CatalogNo,
          product_catalog_no          AS ProductCatalogNo,
          title                       AS Title,
          title_short                 AS TitleShort,
          title_en                    AS TitleEn,
          disc_no_in_set              AS DiscNoInSet,
          disc_kind_code              AS DiscKindCode,
          media_format                AS MediaFormat,
          mcn                         AS Mcn,
          total_tracks                AS TotalTracks,
          total_length_frames         AS TotalLengthFrames,
          num_chapters                AS NumChapters,
          volume_label                AS VolumeLabel,
          cd_text_album_title         AS CdTextAlbumTitle,
          cd_text_album_performer     AS CdTextAlbumPerformer,
          cd_text_album_songwriter    AS CdTextAlbumSongwriter,
          cd_text_album_composer      AS CdTextAlbumComposer,
          cd_text_album_arranger      AS CdTextAlbumArranger,
          cd_text_album_message       AS CdTextAlbumMessage,
          cd_text_disc_id             AS CdTextDiscId,
          cd_text_genre               AS CdTextGenre,
          cddb_disc_id                AS CddbDiscId,
          musicbrainz_disc_id         AS MusicbrainzDiscId,
          last_read_at                AS LastReadAt,
          notes                       AS Notes,
          created_at                  AS CreatedAt,
          updated_at                  AS UpdatedAt,
          created_by                  AS CreatedBy,
          updated_by                  AS UpdatedBy,
          is_deleted                  AS IsDeleted
        """;

    /// <summary>品番で 1 件取得する。</summary>
    public async Task<Disc?> GetByCatalogNoAsync(string catalogNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE catalog_no = @catalogNo
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Disc>(
            new CommandDefinition(sql, new { catalogNo }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定商品に所属する全ディスクを取得する（disc_no_in_set 昇順、NULL は末尾）。
    /// </summary>
    public async Task<IReadOnlyList<Disc>> GetByProductCatalogNoAsync(string productCatalogNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE product_catalog_no = @productCatalogNo AND is_deleted = 0
            ORDER BY COALESCE(disc_no_in_set, 255), catalog_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Disc>(new CommandDefinition(sql, new { productCatalogNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// MCN（JAN/EAN バーコード）で検索する。CDAnalyzer の自動照合に利用。
    /// </summary>
    public async Task<IReadOnlyList<Disc>> FindByMcnAsync(string mcn, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE mcn = @mcn AND is_deleted = 0;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Disc>(new CommandDefinition(sql, new { mcn }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// freedb 互換 Disc ID で検索する。CDAnalyzer の TOC ハッシュによる照合に利用。
    /// </summary>
    public async Task<IReadOnlyList<Disc>> FindByCddbIdAsync(string cddbId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE cddb_disc_id = @cddbId AND is_deleted = 0;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Disc>(new CommandDefinition(sql, new { cddbId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 総尺 ±tolerance フレーム、総トラック数一致での曖昧照合。
    /// MCN や DiscID が取れないケースのフォールバックとして使用する。
    /// </summary>
    public async Task<IReadOnlyList<Disc>> FindByTocFuzzyAsync(byte totalTracks, uint totalLengthFrames, uint tolerance, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE is_deleted = 0
              AND total_tracks = @totalTracks
              AND total_length_frames BETWEEN @lo AND @hi;
            """;
        uint lo = totalLengthFrames > tolerance ? totalLengthFrames - tolerance : 0;
        uint hi = totalLengthFrames + tolerance;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Disc>(new CommandDefinition(sql, new { totalTracks, lo, hi }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// タイトル or 品番の部分一致で検索（DiscMatchDialog の手動検索から利用）。
    /// </summary>
    public async Task<IReadOnlyList<Disc>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM discs
            WHERE is_deleted = 0
              AND (catalog_no LIKE @kw OR title LIKE @kw OR volume_label LIKE @kw OR cd_text_album_title LIKE @kw)
            ORDER BY catalog_no
            LIMIT 200;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Disc>(new CommandDefinition(sql, new { kw = $"%{keyword}%" }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// ディスクを UPSERT する（catalog_no が主キー）。
    /// 既存の tracks は別途 TracksRepository 側で置き換える。
    /// </summary>
    public async Task UpsertAsync(Disc disc, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO discs
              (catalog_no, product_catalog_no, title, title_short, title_en,
               disc_no_in_set, disc_kind_code, media_format,
               mcn, total_tracks, total_length_frames, num_chapters, volume_label,
               cd_text_album_title, cd_text_album_performer, cd_text_album_songwriter,
               cd_text_album_composer, cd_text_album_arranger, cd_text_album_message,
               cd_text_disc_id, cd_text_genre,
               cddb_disc_id, musicbrainz_disc_id, last_read_at,
               notes, created_by, updated_by)
            VALUES
              (@CatalogNo, @ProductCatalogNo, @Title, @TitleShort, @TitleEn,
               @DiscNoInSet, @DiscKindCode, @MediaFormat,
               @Mcn, @TotalTracks, @TotalLengthFrames, @NumChapters, @VolumeLabel,
               @CdTextAlbumTitle, @CdTextAlbumPerformer, @CdTextAlbumSongwriter,
               @CdTextAlbumComposer, @CdTextAlbumArranger, @CdTextAlbumMessage,
               @CdTextDiscId, @CdTextGenre,
               @CddbDiscId, @MusicbrainzDiscId, @LastReadAt,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              product_catalog_no       = VALUES(product_catalog_no),
              title                    = VALUES(title),
              title_short              = VALUES(title_short),
              title_en                 = VALUES(title_en),
              disc_no_in_set           = VALUES(disc_no_in_set),
              disc_kind_code           = VALUES(disc_kind_code),
              media_format             = VALUES(media_format),
              mcn                      = VALUES(mcn),
              total_tracks             = VALUES(total_tracks),
              total_length_frames      = VALUES(total_length_frames),
              num_chapters             = VALUES(num_chapters),
              volume_label             = VALUES(volume_label),
              cd_text_album_title      = VALUES(cd_text_album_title),
              cd_text_album_performer  = VALUES(cd_text_album_performer),
              cd_text_album_songwriter = VALUES(cd_text_album_songwriter),
              cd_text_album_composer   = VALUES(cd_text_album_composer),
              cd_text_album_arranger   = VALUES(cd_text_album_arranger),
              cd_text_album_message    = VALUES(cd_text_album_message),
              cd_text_disc_id          = VALUES(cd_text_disc_id),
              cd_text_genre            = VALUES(cd_text_genre),
              cddb_disc_id             = VALUES(cddb_disc_id),
              musicbrainz_disc_id      = VALUES(musicbrainz_disc_id),
              last_read_at             = VALUES(last_read_at),
              notes                    = VALUES(notes),
              updated_by               = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, disc, cancellationToken: ct));
    }

    /// <summary>
    /// ディスクの物理情報のみを UPSERT する（CDAnalyzer / BDAnalyzer 同期専用）。
    /// <para>
    /// このメソッドはディスクから直接読み取れる物理情報（MCN、TOC、CD-Text、CDDB-ID、last_read_at 等）
    /// のみを更新対象とし、以下の列は一切触らない:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>product_catalog_no</c>: 商品への紐付け（Catalog 側の運用情報）</item>
    ///   <item><c>title</c> / <c>title_short</c> / <c>title_en</c>: Catalog で磨いた正式タイトル</item>
    ///   <item><c>disc_no_in_set</c>: 複数枚組内の順序（商品設計情報）</item>
    ///   <item><c>disc_kind_code</c>: ディスク用途種別（本編／特典 等）</item>
    ///   <item><c>media_format</c>: メディア種別</item>
    ///   <item><c>notes</c>: 備考</item>
    /// </list>
    /// <para>
    /// 既存行が無い場合は INSERT する（CDAnalyzer 初回認識時）。
    /// </para>
    /// </summary>
    public async Task UpsertPhysicalInfoAsync(Disc disc, CancellationToken ct = default)
    {
        // INSERT パスでは product_catalog_no / title / media_format / disc_no_in_set 等に
        // 呼び出し側がセットした値を使う（新規作成ケース）。
        // UPDATE パスでは上記列を一切触らない（既存の Catalog 情報を保全）。
        const string sql = """
            INSERT INTO discs
              (catalog_no, product_catalog_no, title, title_short, title_en,
               disc_no_in_set, disc_kind_code, media_format,
               mcn, total_tracks, total_length_frames, num_chapters, volume_label,
               cd_text_album_title, cd_text_album_performer, cd_text_album_songwriter,
               cd_text_album_composer, cd_text_album_arranger, cd_text_album_message,
               cd_text_disc_id, cd_text_genre,
               cddb_disc_id, musicbrainz_disc_id, last_read_at,
               notes, created_by, updated_by)
            VALUES
              (@CatalogNo, @ProductCatalogNo, @Title, @TitleShort, @TitleEn,
               @DiscNoInSet, @DiscKindCode, @MediaFormat,
               @Mcn, @TotalTracks, @TotalLengthFrames, @NumChapters, @VolumeLabel,
               @CdTextAlbumTitle, @CdTextAlbumPerformer, @CdTextAlbumSongwriter,
               @CdTextAlbumComposer, @CdTextAlbumArranger, @CdTextAlbumMessage,
               @CdTextDiscId, @CdTextGenre,
               @CddbDiscId, @MusicbrainzDiscId, @LastReadAt,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              -- 物理情報のみ更新。title / disc_kind_code / product_catalog_no 等は保全する。
              mcn                      = VALUES(mcn),
              total_tracks             = VALUES(total_tracks),
              total_length_frames      = VALUES(total_length_frames),
              num_chapters             = VALUES(num_chapters),
              volume_label             = VALUES(volume_label),
              cd_text_album_title      = VALUES(cd_text_album_title),
              cd_text_album_performer  = VALUES(cd_text_album_performer),
              cd_text_album_songwriter = VALUES(cd_text_album_songwriter),
              cd_text_album_composer   = VALUES(cd_text_album_composer),
              cd_text_album_arranger   = VALUES(cd_text_album_arranger),
              cd_text_album_message    = VALUES(cd_text_album_message),
              cd_text_disc_id          = VALUES(cd_text_disc_id),
              cd_text_genre            = VALUES(cd_text_genre),
              cddb_disc_id             = VALUES(cddb_disc_id),
              musicbrainz_disc_id      = VALUES(musicbrainz_disc_id),
              last_read_at             = VALUES(last_read_at),
              updated_by               = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, disc, cancellationToken: ct));
    }

    /// <summary>論理削除（is_deleted=1）。</summary>
    public async Task SoftDeleteAsync(string catalogNo, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE discs SET is_deleted = 1, updated_by = @UpdatedBy WHERE catalog_no = @CatalogNo;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CatalogNo = catalogNo, UpdatedBy = updatedBy }, cancellationToken: ct));
    }

    /// <summary>
    /// 閲覧用ディスク一覧を取得する。products / series / product_kinds を LEFT JOIN し、
    /// シリーズ名・商品種別名（翻訳値）を同時に解決する。
    /// </summary>
    /// <remarks>
    /// 並び順は発売日昇順（時系列閲覧用）、同一日内は品番昇順。
    /// 論理削除行は含めない。
    /// </remarks>
    public async Task<IReadOnlyList<DiscBrowserRow>> GetBrowserListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              d.catalog_no            AS CatalogNo,
              d.product_catalog_no    AS ProductCatalogNo,
              COALESCE(d.title, p.title)      AS DisplayTitle,
              s.series_id             AS SeriesId,
              COALESCE(s.title_short, s.title) AS SeriesName,
              p.product_kind_code     AS ProductKindCode,
              pk.name_ja              AS ProductKindName,
              d.media_format          AS MediaFormat,
              p.release_date          AS ReleaseDate,
              d.disc_no_in_set        AS DiscNoInSet,
              p.disc_count            AS DiscCount,
              d.mcn                   AS Mcn,
              d.total_tracks          AS TotalTracks,
              d.total_length_frames   AS TotalLengthFrames
            FROM discs d
            LEFT JOIN products p      ON p.product_catalog_no = d.product_catalog_no
            LEFT JOIN series s        ON s.series_id = p.series_id
            LEFT JOIN product_kinds pk ON pk.kind_code = p.product_kind_code
            WHERE d.is_deleted = 0
            ORDER BY p.release_date, d.catalog_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<DiscBrowserRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }
}

/// <summary>
/// DiscBrowserForm 用のビュー DTO。リポジトリが products/series/product_kinds を
/// LEFT JOIN して翻訳済み表示値を同梱する。
/// </summary>
public sealed class DiscBrowserRow
{
    /// <summary>品番（PK）。</summary>
    public string CatalogNo { get; set; } = "";
    /// <summary>所属商品の代表品番。</summary>
    public string ProductCatalogNo { get; set; } = "";
    /// <summary>表示タイトル（ディスク個別タイトル優先、なければ商品タイトル）。</summary>
    public string? DisplayTitle { get; set; }
    /// <summary>所属シリーズ ID（NULL=オールスターズ）。</summary>
    public int? SeriesId { get; set; }
    /// <summary>シリーズ名（翻訳値。略称優先、なければ正式名称）。</summary>
    public string? SeriesName { get; set; }
    /// <summary>商品種別コード。</summary>
    public string? ProductKindCode { get; set; }
    /// <summary>商品種別名（翻訳値、日本語）。</summary>
    public string? ProductKindName { get; set; }
    /// <summary>物理メディアフォーマット (CD/BD/DVD/DL/OTHER)。</summary>
    public string? MediaFormat { get; set; }
    /// <summary>発売日。</summary>
    public DateTime? ReleaseDate { get; set; }
    /// <summary>組中位置（単品は NULL）。</summary>
    public uint? DiscNoInSet { get; set; }
    /// <summary>商品内総枚数。</summary>
    public byte? DiscCount { get; set; }
    /// <summary>MCN（バーコード）。</summary>
    public string? Mcn { get; set; }
    /// <summary>総トラック数。</summary>
    public byte? TotalTracks { get; set; }
    /// <summary>総尺（CD-DA フレーム、1/75 秒単位）。</summary>
    public uint? TotalLengthFrames { get; set; }
}
