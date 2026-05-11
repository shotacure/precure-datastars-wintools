using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cues テーブル（劇伴の音源 1 件 = 1 行）の CRUD リポジトリ。
/// <para>
/// v1.1.0 の旧 bgm_cues + bgm_recordings の二階層構造は廃止し、1 テーブルに統合した。
/// 録音セッションは <c>session_no</c> 属性として保持し、<c>bgm_sessions</c> マスタへ FK する。
/// 主キーは <c>(series_id, m_no_detail)</c> の 2 列複合。
/// </para>
/// <para>
/// v1.1.3 より <c>is_temp_m_no</c> 列を取り扱う。内部管理用の仮 M 番号（"_temp_..." 等）を
/// 識別するためのフラグで、閲覧 UI 側で表示抑制するのに使う。マスタメンテ画面では素のまま
/// 表示・編集する。
/// </para>
/// <para>
/// v1.3.0 で <c>seq_in_session</c> 列を追加。同一 (series_id, session_no) 内の並び順を整数で持ち、
/// Catalog 側 GUI からの DnD 並べ替えで更新可能。SiteBuilder の劇伴詳細ページ
/// （<c>/bgms/{slug}/</c>）で表内の並び順として使う。
/// </para>
/// </summary>
public sealed class BgmCuesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="BgmCuesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public BgmCuesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // v1.1.3: is_temp_m_no を SELECT 列に追加。Dapper が BgmCue.IsTempMNo に tinyint(0/1) を
    // bool としてマップしてくれる。
    // v1.3.0: seq_in_session を SELECT 列に追加。
    private const string SelectColumns = """
          series_id            AS SeriesId,
          m_no_detail          AS MNoDetail,
          session_no           AS SessionNo,
          seq_in_session       AS SeqInSession,
          m_no_class           AS MNoClass,
          menu_title           AS MenuTitle,
          composer_name        AS ComposerName,
          composer_name_kana   AS ComposerNameKana,
          arranger_name        AS ArrangerName,
          arranger_name_kana   AS ArrangerNameKana,
          length_seconds       AS LengthSeconds,
          notes                AS Notes,
          is_temp_m_no         AS IsTempMNo,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy,
          is_deleted           AS IsDeleted
        """;

    /// <summary>
    /// 指定シリーズの全 cue を取得する。
    /// 並び順は (session_no, seq_in_session, m_no_detail) の昇順
    /// （v1.3.0：セッション内ではユーザー指定の seq_in_session を尊重し、
    /// 同値の場合は m_no_detail でタイブレーク）。
    /// </summary>
    public async Task<IReadOnlyList<BgmCue>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND is_deleted = 0
            ORDER BY session_no, seq_in_session, m_no_detail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定シリーズ・指定セッションの cue を取得する（seq_in_session 順）。</summary>
    public async Task<IReadOnlyList<BgmCue>> GetBySeriesAndSessionAsync(int seriesId, byte sessionNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND session_no = @sessionNo AND is_deleted = 0
            ORDER BY seq_in_session, m_no_detail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(sql, new { seriesId, sessionNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー (series_id, m_no_detail) で 1 件取得する。</summary>
    public async Task<BgmCue?> GetAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND m_no_detail = @mNoDetail
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<BgmCue>(
            new CommandDefinition(sql, new { seriesId, mNoDetail }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定シリーズ内で、キーワードを <c>m_no_detail</c> / <c>m_no_class</c> / <c>menu_title</c>
    /// / <c>composer_name</c> / <c>arranger_name</c> に対して部分一致させて検索する。
    /// トラック編集フォームの BGM オートコンプリート選択から利用する。
    /// </summary>
    /// <param name="seriesId">絞り込みシリーズ ID。</param>
    /// <param name="keyword">検索キーワード。空文字のときは空リストを返す。</param>
    /// <param name="includeTemp">
    /// true のときは <c>is_temp_m_no=1</c>（仮番号）の行も候補に含める。false のときは除外する。
    /// トラック登録時の既定は false（実番号から選ばせる）、マスタメンテからの呼び出しでは true。
    /// </param>
    /// <param name="limit">最大返却件数。既定 100。</param>
    public async Task<IReadOnlyList<BgmCue>> SearchInSeriesAsync(
        int seriesId, string keyword, bool includeTemp = false, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<BgmCue>();

        // LIKE の前後に % を付ける。m_no_class が「M220」のようなキーワードでもヒットするよう、
        // 全検索カラム共通の単一パターンで引き当てる。
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId
              AND is_deleted = 0
              {(includeTemp ? "" : "AND is_temp_m_no = 0")}
              AND (
                    m_no_detail   LIKE @kw
                 OR m_no_class    LIKE @kw
                 OR menu_title    LIKE @kw
                 OR composer_name LIKE @kw
                 OR arranger_name LIKE @kw
              )
            ORDER BY m_no_class, m_no_detail
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(
            sql,
            new { seriesId, kw = $"%{keyword}%", limit },
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ指定なしでキーワード横断検索する（全シリーズ対象）。件数制御は呼び出し側で行う想定。
    /// トラック編集フォームで「シリーズ未指定」状態でも BGM 検索を許容するために用意する。
    /// </summary>
    public async Task<IReadOnlyList<BgmCue>> SearchAllSeriesAsync(
        string keyword, bool includeTemp = false, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<BgmCue>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE is_deleted = 0
              {(includeTemp ? "" : "AND is_temp_m_no = 0")}
              AND (
                    m_no_detail   LIKE @kw
                 OR m_no_class    LIKE @kw
                 OR menu_title    LIKE @kw
                 OR composer_name LIKE @kw
                 OR arranger_name LIKE @kw
              )
            ORDER BY series_id, m_no_class, m_no_detail
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(
            sql,
            new { kw = $"%{keyword}%", limit },
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定シリーズで既に使われている <c>_temp_</c> 接頭辞の連番から、次に使える番号を採番する。
    /// 採番形式は <c>_temp_NNNNNN</c>（6 桁ゼロ埋め）。連番の途中が抜けていても詰めず、最大値 + 1 を返す。
    /// 該当が無ければ <c>_temp_000001</c> を返す。
    /// </summary>
    public async Task<string> GenerateNextTempMNoAsync(int seriesId, CancellationToken ct = default)
    {
        // m_no_detail は varchar なので、数値抽出のため "_temp_" プレフィックスを外して CAST する。
        // 非数値接尾辞（"_temp_foo" のような不正値）は CAST UNSIGNED で 0 扱いになるため自然と無視される。
        const string sql = """
            SELECT COALESCE(
                     MAX(CAST(SUBSTRING(m_no_detail, 7) AS UNSIGNED)),
                     0
                   ) AS max_no
              FROM bgm_cues
             WHERE series_id = @seriesId
               AND m_no_detail LIKE '\\_temp\\_%' ESCAPE '\\';
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        ulong current = await conn.ExecuteScalarAsync<ulong>(
            new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        ulong next = current + 1UL;
        return $"_temp_{next:D6}";
    }

    /// <summary>
    /// UPSERT。PK 衝突時は全属性を新しい値で上書きする。
    /// v1.1.3 より <c>is_temp_m_no</c> も UPSERT 対象。
    /// v1.3.0 より <c>seq_in_session</c> も UPSERT 対象。
    /// </summary>
    public async Task UpsertAsync(BgmCue cue, CancellationToken ct = default)
    {
        // 新規 INSERT 時に SeqInSession=0 のまま渡されるケースが想定される（GUI 側で
        // 連番を引き当てる前のドラフト状態）。マイグレ実行時の自然順初期投入とは別経路で
        // 0 が混入することを許容するが、運用上は GUI で並び替え後にバルク更新で
        // 1, 2, 3... が振られる流れになる。
        const string sql = """
            INSERT INTO bgm_cues
              (series_id, m_no_detail, session_no, seq_in_session, m_no_class, menu_title,
               composer_name, composer_name_kana,
               arranger_name, arranger_name_kana,
               length_seconds, notes, is_temp_m_no,
               created_by, updated_by)
            VALUES
              (@SeriesId, @MNoDetail, @SessionNo, @SeqInSession, @MNoClass, @MenuTitle,
               @ComposerName, @ComposerNameKana,
               @ArrangerName, @ArrangerNameKana,
               @LengthSeconds, @Notes, @IsTempMNo,
               @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              session_no         = VALUES(session_no),
              seq_in_session     = VALUES(seq_in_session),
              m_no_class         = VALUES(m_no_class),
              menu_title         = VALUES(menu_title),
              composer_name      = VALUES(composer_name),
              composer_name_kana = VALUES(composer_name_kana),
              arranger_name      = VALUES(arranger_name),
              arranger_name_kana = VALUES(arranger_name_kana),
              length_seconds     = VALUES(length_seconds),
              notes              = VALUES(notes),
              is_temp_m_no       = VALUES(is_temp_m_no),
              updated_by         = VALUES(updated_by),
              is_deleted         = 0;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, cue, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int seriesId, string mNoDetail, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_cues
               SET is_deleted = 1, updated_by = @UpdatedBy
             WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { SeriesId = seriesId, MNoDetail = mNoDetail, UpdatedBy = updatedBy },
            cancellationToken: ct));
    }

    /// <summary>
    /// 同一 (series_id, session_no) グループ内の cue について <c>seq_in_session</c> を
    /// 一括再採番する（v1.3.0 追加）。
    /// <para>
    /// Catalog 側の劇伴管理画面で DnD によりセッション内の並び順を変更したあと呼び出す。
    /// 与えられた順で先頭から 1, 2, 3, ... と振り直す。<c>seq_in_session</c> には UNIQUE 制約
    /// が無いため退避値経由の 2 段階更新は不要で、1 トランザクションで順次 UPDATE する。
    /// </para>
    /// </summary>
    /// <param name="seriesId">対象シリーズ。</param>
    /// <param name="sessionNo">対象セッション番号。</param>
    /// <param name="orderedMNoDetails">
    /// 並べ替え後の m_no_detail 列（先頭が seq_in_session=1、次が 2、...）。
    /// 全 cue が同じ (seriesId, sessionNo) に属する前提。
    /// </param>
    /// <param name="updatedBy">監査ユーザ名。</param>
    public async Task BulkUpdateSeqInSessionAsync(
        int seriesId,
        byte sessionNo,
        IList<string> orderedMNoDetails,
        string? updatedBy,
        CancellationToken ct = default)
    {
        if (orderedMNoDetails is null) throw new ArgumentNullException(nameof(orderedMNoDetails));
        if (orderedMNoDetails.Count == 0) return;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int seq = 1;
            foreach (var mNoDetail in orderedMNoDetails)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE bgm_cues
                       SET seq_in_session = @SeqInSession,
                           updated_by     = @UpdatedBy
                     WHERE series_id      = @SeriesId
                       AND session_no     = @SessionNo
                       AND m_no_detail    = @MNoDetail;
                    """,
                    new
                    {
                        SeriesId = seriesId,
                        SessionNo = sessionNo,
                        MNoDetail = mNoDetail,
                        SeqInSession = seq,
                        UpdatedBy = updatedBy
                    },
                    transaction: tx, cancellationToken: ct));
                seq++;
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
