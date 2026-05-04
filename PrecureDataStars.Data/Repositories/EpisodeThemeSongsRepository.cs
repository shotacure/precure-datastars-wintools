using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_theme_songs テーブル（エピソード × 主題歌の紐付け）の CRUD リポジトリ。
/// <para>
/// v1.2.0 工程 B' で複合主キーを 4 列構成
/// (episode_id, is_broadcast_only, theme_kind, insert_seq) に拡張。
/// 既定の <c>is_broadcast_only=0</c> 行が「本放送・Blu-ray・配信ともに同じ主題歌」を表し、
/// 本放送だけ例外的に異なる場合に限り <c>is_broadcast_only=1</c> の追加行を別途立てる
/// 運用とする。OP / ED は <c>insert_seq</c>=0 の 1 行ずつ、INSERT は <c>insert_seq</c>=1, 2, ...
/// と複数行ずつを各フラグごとに保持できる。
/// </para>
/// <para>
/// theme_kind と insert_seq の整合性は DB 側 CHECK 制約 <c>ck_ets_op_ed_no_insert_seq</c>
/// でも担保される。クレジットの THEME_SONG ロールエントリは、本テーブルから歌情報を
/// 引いてレンダリングする想定。
/// </para>
/// </summary>
public sealed class EpisodeThemeSongsRepository
{
    private readonly IConnectionFactory _factory;

    public EpisodeThemeSongsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          episode_id              AS EpisodeId,
          is_broadcast_only       AS IsBroadcastOnly,
          theme_kind              AS ThemeKind,
          insert_seq              AS InsertSeq,
          song_recording_id       AS SongRecordingId,
          label_company_alias_id  AS LabelCompanyAliasId,
          notes                   AS Notes,
          created_at              AS CreatedAt,
          updated_at              AS UpdatedAt,
          created_by              AS CreatedBy,
          updated_by              AS UpdatedBy
        """;

    /// <summary>
    /// 指定エピソードに紐付く主題歌一覧を取得する。
    /// is_broadcast_only → theme_kind → insert_seq 昇順で並ぶ。
    /// 既定（フラグ 0）行と本放送限定（フラグ 1）行が連続して表示される。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeThemeSong>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id = @episodeId
            ORDER BY is_broadcast_only, theme_kind, insert_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeThemeSong>(
            new CommandDefinition(sql, new { episodeId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定エピソード × 指定本放送限定フラグに紐付く主題歌のみを取得する
    /// （v1.2.0 工程 B' 追加。コピーダイアログのコピー元読み込みで活用）。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeThemeSong>> GetByEpisodeAndFlagAsync(
        int episodeId, bool isBroadcastOnly, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id = @episodeId AND is_broadcast_only = @flag
            ORDER BY theme_kind, insert_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeThemeSong>(
            new CommandDefinition(
                sql,
                new { episodeId, flag = isBroadcastOnly ? 1 : 0 },
                cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 4 列複合 PK で 1 件取得する。
    /// </summary>
    public async Task<EpisodeThemeSong?> GetByKeyAsync(
        int episodeId, bool isBroadcastOnly, string themeKind, byte insertSeq,
        CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id        = @episodeId
              AND is_broadcast_only = @flag
              AND theme_kind        = @themeKind
              AND insert_seq        = @insertSeq
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<EpisodeThemeSong>(
            new CommandDefinition(
                sql,
                new { episodeId, flag = isBroadcastOnly ? 1 : 0, themeKind, insertSeq },
                cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT（v1.2.0 工程 B' で PK が 4 列構成 (episode_id, is_broadcast_only, theme_kind,
    /// insert_seq) に変更）。is_broadcast_only が PK の一部のため、フラグが変わると
    /// 別レコードとして INSERT される。
    /// </summary>
    public async Task UpsertAsync(EpisodeThemeSong row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO episode_theme_songs
              (episode_id, is_broadcast_only, theme_kind, insert_seq, song_recording_id,
               label_company_alias_id, notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @IsBroadcastOnly, @ThemeKind, @InsertSeq, @SongRecordingId,
               @LabelCompanyAliasId, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              song_recording_id       = VALUES(song_recording_id),
              label_company_alias_id  = VALUES(label_company_alias_id),
              notes                   = VALUES(notes),
              updated_by              = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>
    /// 複数行を 1 トランザクションで一括 UPSERT する（v1.2.0 工程 B' 追加）。
    /// <para>
    /// エピソード主題歌コピーダイアログで「他話のレコードをまとめて別エピソードに反映する」
    /// シナリオ用。プレビュー画面で組み上げた行群をユーザーが「すべて保存」を押した時点で
    /// 初めて DB に反映する流れに使う。途中で例外が起きた場合はロールバックされる。
    /// </para>
    /// <para>
    /// <paramref name="updatedBy"/> は呼び出し側で <c>Environment.UserName</c> 等を
    /// セットして渡す（個別行の <see cref="EpisodeThemeSong.UpdatedBy"/> を上書きする）。
    /// </para>
    /// </summary>
    public async Task BulkUpsertAsync(
        IEnumerable<EpisodeThemeSong> rows, string? updatedBy, CancellationToken ct = default)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        const string sql = """
            INSERT INTO episode_theme_songs
              (episode_id, is_broadcast_only, theme_kind, insert_seq, song_recording_id,
               label_company_alias_id, notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @IsBroadcastOnly, @ThemeKind, @InsertSeq, @SongRecordingId,
               @LabelCompanyAliasId, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              song_recording_id       = VALUES(song_recording_id),
              label_company_alias_id  = VALUES(label_company_alias_id),
              notes                   = VALUES(notes),
              updated_by              = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(updatedBy))
                {
                    row.UpdatedBy = updatedBy;
                    if (string.IsNullOrEmpty(row.CreatedBy)) row.CreatedBy = updatedBy;
                }
                await conn.ExecuteAsync(new CommandDefinition(sql, row, transaction: tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>4 列複合 PK で 1 件削除する。</summary>
    public async Task DeleteAsync(
        int episodeId, bool isBroadcastOnly, string themeKind, byte insertSeq,
        CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM episode_theme_songs
            WHERE episode_id        = @EpisodeId
              AND is_broadcast_only = @Flag
              AND theme_kind        = @ThemeKind
              AND insert_seq        = @InsertSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                EpisodeId = episodeId,
                Flag = isBroadcastOnly ? 1 : 0,
                ThemeKind = themeKind,
                InsertSeq = insertSeq
            },
            cancellationToken: ct));
    }
}
