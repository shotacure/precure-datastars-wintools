using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// tracks テーブル（物理トラック）の CRUD リポジトリ。
/// <para>
/// 複合主キーは <c>(catalog_no, track_no, sub_order)</c>。通常のトラックは <c>sub_order=0</c> の 1 行で
/// 表し、1 トラックに複数の曲が入っているケース（メドレー、ボーナストラックの複数曲構成、BGM 前後半分割等）
/// では同じ <c>track_no</c> の下に <c>sub_order=1, 2, ...</c> を追加して複数行で表現する。
/// </para>
/// <para>
/// 物理情報（start_lba, length_frames, isrc 等）は <c>sub_order=0</c> の親行にだけ持つ。
/// 子行では NULL/0 でなければトリガーで拒否される。
/// BGM 参照は 2 列の複合外部キー <c>(bgm_series_id, bgm_m_no_detail)</c> で bgm_cues を指す。
/// SONG は <c>song_recording_id</c>（int）で song_recordings を指す。
/// </para>
/// </summary>
public sealed class TracksRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="TracksRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public TracksRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // 共通 SELECT 列
    private const string SelectColumns = """
          catalog_no               AS CatalogNo,
          track_no                 AS TrackNo,
          sub_order                AS SubOrder,
          content_kind_code        AS ContentKindCode,
          song_recording_id        AS SongRecordingId,
          song_size_variant_code   AS SongSizeVariantCode,
          song_part_variant_code   AS SongPartVariantCode,
          bgm_series_id            AS BgmSeriesId,
          bgm_m_no_detail          AS BgmMNoDetail,
          track_title_override     AS TrackTitleOverride,
          start_lba                AS StartLba,
          length_frames            AS LengthFrames,
          isrc                     AS Isrc,
          is_data_track            AS IsDataTrack,
          has_pre_emphasis         AS HasPreEmphasis,
          is_copy_permitted        AS IsCopyPermitted,
          cd_text_title            AS CdTextTitle,
          cd_text_performer        AS CdTextPerformer,
          notes                    AS Notes,
          created_at               AS CreatedAt,
          updated_at               AS UpdatedAt,
          created_by               AS CreatedBy,
          updated_by               AS UpdatedBy
        """;

    /// <summary>
    /// 指定ディスクの全トラック行を取得する（track_no, sub_order 昇順）。
    /// sub_order > 0 の子行も含めて全部返す。親行だけが必要な場合は呼び出し側で sub_order=0 を絞る。
    /// </summary>
    public async Task<IReadOnlyList<Track>> GetByCatalogNoAsync(string catalogNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM tracks
            WHERE catalog_no = @catalogNo
            ORDER BY track_no, sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Track>(new CommandDefinition(sql, new { catalogNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定ディスクのトラックを一括置換する（既存を全削除してから一括 INSERT）。
    /// トランザクション内で実行され、途中失敗時は全体がロールバックされる。
    /// CDAnalyzer の新規登録パスで使用する。既存ディスクの同期では <see cref="UpsertPhysicalInfoForDiscAsync"/> を使うこと
    /// （Catalog で磨いた情報を保全するため）。
    /// </summary>
    public async Task ReplaceAllForDiscAsync(string catalogNo, IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        const string deleteSql = "DELETE FROM tracks WHERE catalog_no = @catalogNo;";
        const string insertSql = """
            INSERT INTO tracks
              (catalog_no, track_no, sub_order, content_kind_code,
               song_recording_id,
               song_size_variant_code, song_part_variant_code,
               bgm_series_id, bgm_m_no_detail,
               track_title_override,
               start_lba, length_frames, isrc,
               is_data_track, has_pre_emphasis, is_copy_permitted,
               cd_text_title, cd_text_performer, notes,
               created_by, updated_by)
            VALUES
              (@CatalogNo, @TrackNo, @SubOrder, @ContentKindCode,
               @SongRecordingId,
               @SongSizeVariantCode, @SongPartVariantCode,
               @BgmSeriesId, @BgmMNoDetail,
               @TrackTitleOverride,
               @StartLba, @LengthFrames, @Isrc,
               @IsDataTrack, @HasPreEmphasis, @IsCopyPermitted,
               @CdTextTitle, @CdTextPerformer, @Notes,
               @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { catalogNo }, transaction: tx, cancellationToken: ct));
            var prepared = tracks.Select(t =>
            {
                t.CatalogNo = catalogNo;
                return t;
            }).ToList();
            if (prepared.Count > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(insertSql, prepared, transaction: tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>1 トラック行単位の UPSERT（手動編集用）。キーは (catalog_no, track_no, sub_order)。</summary>
    public async Task UpsertAsync(Track track, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tracks
              (catalog_no, track_no, sub_order, content_kind_code,
               song_recording_id,
               song_size_variant_code, song_part_variant_code,
               bgm_series_id, bgm_m_no_detail,
               track_title_override,
               start_lba, length_frames, isrc,
               is_data_track, has_pre_emphasis, is_copy_permitted,
               cd_text_title, cd_text_performer, notes,
               created_by, updated_by)
            VALUES
              (@CatalogNo, @TrackNo, @SubOrder, @ContentKindCode,
               @SongRecordingId,
               @SongSizeVariantCode, @SongPartVariantCode,
               @BgmSeriesId, @BgmMNoDetail,
               @TrackTitleOverride,
               @StartLba, @LengthFrames, @Isrc,
               @IsDataTrack, @HasPreEmphasis, @IsCopyPermitted,
               @CdTextTitle, @CdTextPerformer, @Notes,
               @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              content_kind_code      = VALUES(content_kind_code),
              song_recording_id      = VALUES(song_recording_id),
              song_size_variant_code = VALUES(song_size_variant_code),
              song_part_variant_code = VALUES(song_part_variant_code),
              bgm_series_id          = VALUES(bgm_series_id),
              bgm_m_no_detail        = VALUES(bgm_m_no_detail),
              track_title_override   = VALUES(track_title_override),
              start_lba              = VALUES(start_lba),
              length_frames          = VALUES(length_frames),
              isrc                   = VALUES(isrc),
              is_data_track          = VALUES(is_data_track),
              has_pre_emphasis       = VALUES(has_pre_emphasis),
              is_copy_permitted      = VALUES(is_copy_permitted),
              cd_text_title          = VALUES(cd_text_title),
              cd_text_performer      = VALUES(cd_text_performer),
              notes                  = VALUES(notes),
              updated_by             = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, track, cancellationToken: ct));
    }

    /// <summary>
    /// トラックの物理情報のみを UPSERT する（CDAnalyzer / BDAnalyzer 同期専用）。
    /// <para>
    /// このメソッドはディスクから直接読み取れる物理情報（LBA・尺・ISRC・CD-Text 等）
    /// のみを更新対象とし、以下の列は一切触らない:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>content_kind_code</c>: トラック内容種別（SONG/BGM/DRAMA 等）</item>
    ///   <item><c>song_recording_id</c>: 歌紐付け</item>
    ///   <item><c>song_size_variant_code</c> / <c>song_part_variant_code</c>: 歌のサイズ・パート種別</item>
    ///   <item><c>bgm_series_id</c> / <c>bgm_m_no_detail</c>: 劇伴紐付け</item>
    ///   <item><c>track_title_override</c>: 収録盤固有タイトル表記（Catalog で磨いた情報）</item>
    ///   <item><c>notes</c>: 備考</item>
    /// </list>
    /// <para>
    /// 既存行が無い場合は INSERT する（<c>content_kind_code='OTHER'</c>・紐付け系列は NULL として新規作成）。
    /// 物理情報は親行 (sub_order=0) だけが持つので、CDAnalyzer が提供する Track の SubOrder は常に 0 で呼び出す。
    /// </para>
    /// </summary>
    public async Task UpsertPhysicalInfoAsync(Track track, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tracks
              (catalog_no, track_no, sub_order, content_kind_code,
               song_recording_id,
               song_size_variant_code, song_part_variant_code,
               bgm_series_id, bgm_m_no_detail,
               track_title_override,
               start_lba, length_frames, isrc,
               is_data_track, has_pre_emphasis, is_copy_permitted,
               cd_text_title, cd_text_performer, notes,
               created_by, updated_by)
            VALUES
              (@CatalogNo, @TrackNo, @SubOrder, @ContentKindCode,
               @SongRecordingId,
               @SongSizeVariantCode, @SongPartVariantCode,
               @BgmSeriesId, @BgmMNoDetail,
               @TrackTitleOverride,
               @StartLba, @LengthFrames, @Isrc,
               @IsDataTrack, @HasPreEmphasis, @IsCopyPermitted,
               @CdTextTitle, @CdTextPerformer, @Notes,
               @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              start_lba            = VALUES(start_lba),
              length_frames        = VALUES(length_frames),
              isrc                 = VALUES(isrc),
              is_data_track        = VALUES(is_data_track),
              has_pre_emphasis     = VALUES(has_pre_emphasis),
              is_copy_permitted    = VALUES(is_copy_permitted),
              cd_text_title        = VALUES(cd_text_title),
              cd_text_performer    = VALUES(cd_text_performer),
              updated_by           = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, track, cancellationToken: ct));
    }

    /// <summary>
    /// 指定ディスクの複数トラックの物理情報をまとめて UPSERT する（CDAnalyzer / BDAnalyzer 同期専用）。
    /// <para>
    /// 既存の tracks 行は <b>削除しない</b>（<see cref="ReplaceAllForDiscAsync"/> との決定的な違い）。
    /// ディスクから読めた各トラックについて物理情報のみを UPSERT する。
    /// </para>
    /// </summary>
    public async Task UpsertPhysicalInfoForDiscAsync(string catalogNo, IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            const string sql = """
                INSERT INTO tracks
                  (catalog_no, track_no, sub_order, content_kind_code,
                   song_recording_id,
                   song_size_variant_code, song_part_variant_code,
                   bgm_series_id, bgm_m_no_detail,
                   track_title_override,
                   start_lba, length_frames, isrc,
                   is_data_track, has_pre_emphasis, is_copy_permitted,
                   cd_text_title, cd_text_performer, notes,
                   created_by, updated_by)
                VALUES
                  (@CatalogNo, @TrackNo, @SubOrder, @ContentKindCode,
                   @SongRecordingId,
                   @SongSizeVariantCode, @SongPartVariantCode,
                   @BgmSeriesId, @BgmMNoDetail,
                   @TrackTitleOverride,
                   @StartLba, @LengthFrames, @Isrc,
                   @IsDataTrack, @HasPreEmphasis, @IsCopyPermitted,
                   @CdTextTitle, @CdTextPerformer, @Notes,
                   @CreatedBy, @UpdatedBy)
                ON DUPLICATE KEY UPDATE
                  start_lba            = VALUES(start_lba),
                  length_frames        = VALUES(length_frames),
                  isrc                 = VALUES(isrc),
                  is_data_track        = VALUES(is_data_track),
                  has_pre_emphasis     = VALUES(has_pre_emphasis),
                  is_copy_permitted    = VALUES(is_copy_permitted),
                  cd_text_title        = VALUES(cd_text_title),
                  cd_text_performer    = VALUES(cd_text_performer),
                  updated_by           = VALUES(updated_by);
                """;

            // catalog_no / sub_order を強制的に揃えてから UPSERT
            // （物理情報は親行 sub_order=0 だけが保持するため、CDAnalyzer からの呼び出しは常に sub_order=0）
            var prepared = tracks.Select(t =>
            {
                t.CatalogNo = catalogNo;
                t.SubOrder = 0;
                return t;
            });

            foreach (var t in prepared)
            {
                await conn.ExecuteAsync(new CommandDefinition(sql, t, transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>1 トラック行を削除する（複合 PK 指定）。</summary>
    public async Task DeleteAsync(string catalogNo, byte trackNo, byte subOrder, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM tracks WHERE catalog_no = @CatalogNo AND track_no = @TrackNo AND sub_order = @SubOrder;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CatalogNo = catalogNo, TrackNo = trackNo, SubOrder = subOrder }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定 track_no に属する全 sub_order 行をまとめて削除する。親行 (sub_order=0) を削除する場合に子行も同時に消したい場面向け。
    /// </summary>
    public async Task DeleteAllSubOrdersAsync(string catalogNo, byte trackNo, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM tracks WHERE catalog_no = @CatalogNo AND track_no = @TrackNo;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CatalogNo = catalogNo, TrackNo = trackNo }, cancellationToken: ct));
    }

    /// <summary>
    /// 閲覧用トラック一覧を取得する。指定ディスク (catalog_no) に属する全トラック行（sub_order 含む全て）を、
    /// 内容種別名（翻訳値）・表示タイトル・アーティスト・尺付きで返す。
    /// </summary>
    /// <remarks>
    /// <para>タイトル解決順（収録盤固有の表記を最優先）：</para>
    /// <list type="number">
    ///   <item>t.track_title_override（非空なら最優先）</item>
    ///   <item>SONG → variant_label があればそれを単独表示、無ければ songs.title（親曲名）</item>
    ///   <item>BGM  → bgm_cues.menu_title（空なら m_no_detail を代替表示）</item>
    ///   <item>それ以外 → t.cd_text_title</item>
    /// </list>
    /// <para>SONG で variant_label を単独表示する意図: 旧仕様では "親曲名 (variant_label)" の併記だったが、
    /// variant_label そのものに派生 title（例: "DANZEN! ふたりはプリキュア Ver. MaxHeart"）が
    /// 入るケースが多く、親曲名と併記すると "DANZEN! ふたりはプリキュア (DANZEN! ふたりはプリキュア Ver. MaxHeart)"
    /// のような冗長表記になっていた。variant_label には派生後の完全タイトルが入っている前提で、
    /// NULL のときだけ親曲名にフォールバックする。</para>
    /// <para>アーティスト解決順：SONG は歌唱者、BGM は編曲者→作曲者、その他は CD-Text。</para>
    /// <para>尺は CD-DA フレーム (length_frames, 1/75 秒単位) を優先（親行のみ保有、子行は NULL）。</para>
    /// </remarks>
    public async Task<IReadOnlyList<TrackBrowserRow>> GetBrowserListByCatalogNoAsync(string catalogNo, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              t.catalog_no            AS CatalogNo,
              t.track_no              AS TrackNo,
              t.sub_order             AS SubOrder,
              t.content_kind_code     AS ContentKindCode,
              tck.name_ja             AS ContentKindName,
              COALESCE(
                t.track_title_override,
                CASE t.content_kind_code
                  WHEN 'SONG' THEN
                    CONCAT(
                      -- variant_label が非空ならそれを表示（親曲名とは併記しない）。NULL/空なら親曲名で代替。
                      CASE WHEN sr.variant_label IS NOT NULL AND sr.variant_label <> ''
                           THEN sr.variant_label
                           ELSE sg.title
                      END,
                      CASE WHEN ssv.name_ja IS NOT NULL THEN CONCAT(' [', ssv.name_ja, ']') ELSE '' END,
                      CASE WHEN spv.name_ja IS NOT NULL AND spv.variant_code <> 'VOCAL'
                           THEN CONCAT(' [', spv.name_ja, ']') ELSE '' END
                    )
                  WHEN 'BGM'  THEN COALESCE(bc.menu_title, bc.m_no_detail)
                  ELSE NULL
                END,
                t.cd_text_title
              )                       AS DisplayTitle,
              CASE t.content_kind_code
                WHEN 'SONG' THEN COALESCE(sr.singer_name, t.cd_text_performer)
                WHEN 'BGM'  THEN COALESCE(bc.arranger_name, bc.composer_name, t.cd_text_performer)
                ELSE t.cd_text_performer
              END                     AS Artist,
              t.length_frames         AS LengthFrames,
              CASE t.content_kind_code
                WHEN 'BGM'  THEN bc.length_seconds
                ELSE NULL
              END                     AS LengthSecondsFallback,
              t.isrc                  AS Isrc,
              t.notes                 AS Notes
            FROM tracks t
            LEFT JOIN track_content_kinds tck ON tck.kind_code = t.content_kind_code
            LEFT JOIN song_recordings sr      ON sr.song_recording_id = t.song_recording_id
            LEFT JOIN songs sg                ON sg.song_id = sr.song_id
            LEFT JOIN song_size_variants ssv  ON ssv.variant_code = t.song_size_variant_code
            LEFT JOIN song_part_variants spv  ON spv.variant_code = t.song_part_variant_code
            LEFT JOIN bgm_cues bc             ON  bc.series_id   = t.bgm_series_id
                                              AND bc.m_no_detail = t.bgm_m_no_detail
            WHERE t.catalog_no = @catalogNo
            ORDER BY t.track_no, t.sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<TrackBrowserRow>(new CommandDefinition(sql, new { catalogNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定の song_recording_id を参照しているトラック（＝どのディスクのどのトラックに収録されているか）を取得する。
    /// 歌マスタ画面で収録情報パネルに表示するためのクエリ。
    /// </summary>
    public async Task<IReadOnlyList<SongRecordingTrackRef>> GetTracksBySongRecordingAsync(int songRecordingId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              t.catalog_no        AS CatalogNo,
              t.track_no          AS TrackNo,
              t.sub_order         AS SubOrder,
              d.title             AS DiscTitle,
              p.title             AS ProductTitle,
              p.release_date      AS ReleaseDate,
              t.song_size_variant_code AS SongSizeVariantCode,
              ssv.name_ja              AS SongSizeVariantName,
              t.song_part_variant_code AS SongPartVariantCode,
              spv.name_ja              AS SongPartVariantName,
              t.track_title_override   AS TrackTitleOverride
            FROM tracks t
            JOIN discs d ON d.catalog_no = t.catalog_no
            LEFT JOIN products p ON p.product_catalog_no = d.product_catalog_no
            LEFT JOIN song_size_variants ssv ON ssv.variant_code = t.song_size_variant_code
            LEFT JOIN song_part_variants spv ON spv.variant_code = t.song_part_variant_code
            WHERE t.song_recording_id = @songRecordingId
            ORDER BY p.release_date, t.catalog_no, t.track_no, t.sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongRecordingTrackRef>(new CommandDefinition(sql, new { songRecordingId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定の bgm_cue (series_id, m_no_detail) を参照しているトラック一覧を取得する。
    /// 劇伴マスタ画面で収録情報パネルに表示するためのクエリ。
    /// </summary>
    public async Task<IReadOnlyList<BgmCueTrackRef>> GetTracksByBgmCueAsync(
        int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              t.catalog_no        AS CatalogNo,
              t.track_no          AS TrackNo,
              t.sub_order         AS SubOrder,
              d.title             AS DiscTitle,
              p.title             AS ProductTitle,
              p.release_date      AS ReleaseDate,
              t.track_title_override AS TrackTitleOverride
            FROM tracks t
            JOIN discs d ON d.catalog_no = t.catalog_no
            LEFT JOIN products p ON p.product_catalog_no = d.product_catalog_no
            WHERE t.bgm_series_id  = @seriesId
              AND t.bgm_m_no_detail = @mNoDetail
            ORDER BY p.release_date, t.catalog_no, t.track_no, t.sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCueTrackRef>(
            new CommandDefinition(sql, new { seriesId, mNoDetail }, cancellationToken: ct));
        return rows.ToList();
    }
}

/// <summary>
/// DiscBrowserForm 用のトラック行 DTO。TracksRepository が必要テーブルを LEFT JOIN して
/// 翻訳済み表示値（種別名・タイトル・アーティスト・尺）を返却する。
/// </summary>
public sealed class TrackBrowserRow
{
    /// <summary>所属ディスク品番。</summary>
    public string CatalogNo { get; set; } = "";
    /// <summary>トラック番号（1 始まり）。</summary>
    public byte TrackNo { get; set; }
    /// <summary>トラック内順序（通常は 0、メドレー等で 1, 2, ...）。</summary>
    public byte SubOrder { get; set; }
    /// <summary>内容種別コード (SONG/BGM/DRAMA/RADIO/JINGLE/CHAPTER/OTHER)。</summary>
    public string? ContentKindCode { get; set; }
    /// <summary>内容種別の日本語表示名（翻訳値）。</summary>
    public string? ContentKindName { get; set; }
    /// <summary>表示タイトル（歌・劇伴・上書き・CD-Text のいずれかから解決済み）。</summary>
    public string? DisplayTitle { get; set; }
    /// <summary>アーティスト／演奏者。</summary>
    public string? Artist { get; set; }
    /// <summary>尺（CD-DA フレーム単位。1/75 秒）。sub_order>0 では NULL。</summary>
    public uint? LengthFrames { get; set; }
    /// <summary>尺のフォールバック（秒）。BGM で cue 側に秒数だけ入っている時に使う。</summary>
    public ushort? LengthSecondsFallback { get; set; }
    /// <summary>ISRC。</summary>
    public string? Isrc { get; set; }
    /// <summary>備考。</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 歌マスタ画面で「この録音がどのディスクに収録されているか」を表示するための 1 行。
/// </summary>
public sealed class SongRecordingTrackRef
{
    public string CatalogNo { get; set; } = "";
    public byte TrackNo { get; set; }
    public byte SubOrder { get; set; }
    public string? DiscTitle { get; set; }
    public string? ProductTitle { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? SongSizeVariantCode { get; set; }
    public string? SongSizeVariantName { get; set; }
    public string? SongPartVariantCode { get; set; }
    public string? SongPartVariantName { get; set; }
    public string? TrackTitleOverride { get; set; }
}

/// <summary>
/// 劇伴マスタ画面で「この cue がどのディスクに収録されているか」を表示するための 1 行。
/// </summary>
public sealed class BgmCueTrackRef
{
    public string CatalogNo { get; set; } = "";
    public byte TrackNo { get; set; }
    public byte SubOrder { get; set; }
    public string? DiscTitle { get; set; }
    public string? ProductTitle { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? TrackTitleOverride { get; set; }
}