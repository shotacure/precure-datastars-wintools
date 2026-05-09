using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_theme_songs テーブル（エピソード × 主題歌の紐付け）の CRUD リポジトリ。
/// <para>
/// v1.2.0 工程 B' で複合主キーを 4 列構成
/// (episode_id, is_broadcast_only, theme_kind, seq) に拡張。
/// 既定の <c>is_broadcast_only=0</c> 行が「本放送・Blu-ray・配信ともに同じ主題歌」を表し、
/// 本放送だけ例外的に異なる場合に限り <c>is_broadcast_only=1</c> の追加行を別途立てる
/// 運用とする。
/// </para>
/// <para>
/// v1.3.0 で旧 <c>insert_seq</c> 列を <c>seq</c> にリネーム。
/// 列の意味も変更：
/// 旧仕様では「OP/ED は 0 固定 / INSERT は 1〜n」という排他ルールがあったが、
/// 新仕様では <c>seq</c> はエピソード内の劇中順（1, 2, 3, ...）を表す汎用カラム。
/// OP が冒頭にあるとは限らない作品にも対応するため、theme_kind と seq の関係性は
/// 緩和されている（旧 CHECK 制約 ck_ets_op_ed_no_insert_seq は v1.3.0 マイグレで撤廃）。
/// </para>
/// <para>
/// クレジットの THEME_SONG ロールエントリは、本テーブルから歌情報を引いてレンダリングする想定。
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
          seq                     AS Seq,
          song_recording_id       AS SongRecordingId,
          notes                   AS Notes,
          created_at              AS CreatedAt,
          updated_at              AS UpdatedAt,
          created_by              AS CreatedBy,
          updated_by              AS UpdatedBy
        """;

    /// <summary>
    /// 全エピソード × 主題歌の紐付け行を取得する（episode_id → is_broadcast_only → seq 昇順）。
    /// SiteBuilder（v1.3.0）の楽曲詳細ページで「歌が主題歌として使用されたエピソード」を逆引きするため、
    /// 起動時 1 回だけ全件をメモリに読み込む用途。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeThemeSong>> GetAllAsync(CancellationToken ct = default)
    {
        // v1.3.0：seq は劇中順を表すため、ORDER BY を episode_id → is_broadcast_only → seq に
        // 単純化（旧 theme_kind を含む 4 列ソートは不要）。
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            ORDER BY episode_id, is_broadcast_only, seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeThemeSong>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定エピソードに紐付く主題歌一覧を取得する。
    /// is_broadcast_only → seq 昇順で並ぶ（劇中順）。
    /// 既定（フラグ 0）行と本放送限定（フラグ 1）行が連続して表示される。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeThemeSong>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id = @episodeId
            ORDER BY is_broadcast_only, seq;
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
            ORDER BY seq;
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
    /// 4 列複合 PK で 1 件取得する（v1.3.0：第 4 列は seq）。
    /// </summary>
    public async Task<EpisodeThemeSong?> GetByKeyAsync(
        int episodeId, bool isBroadcastOnly, string themeKind, byte seq,
        CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id        = @episodeId
              AND is_broadcast_only = @flag
              AND theme_kind        = @themeKind
              AND seq               = @seq
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<EpisodeThemeSong>(
            new CommandDefinition(
                sql,
                new { episodeId, flag = isBroadcastOnly ? 1 : 0, themeKind, seq },
                cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT（v1.3.0：PK の第 4 列が seq に変更）。is_broadcast_only が PK の一部のため、
    /// フラグが変わると別レコードとして INSERT される。
    /// </summary>
    public async Task UpsertAsync(EpisodeThemeSong row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO episode_theme_songs
              (episode_id, is_broadcast_only, theme_kind, seq, song_recording_id,
               notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @IsBroadcastOnly, @ThemeKind, @Seq, @SongRecordingId,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              song_recording_id       = VALUES(song_recording_id),
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
              (episode_id, is_broadcast_only, theme_kind, seq, song_recording_id,
               notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @IsBroadcastOnly, @ThemeKind, @Seq, @SongRecordingId,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              song_recording_id       = VALUES(song_recording_id),
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

    /// <summary>4 列複合 PK で 1 件削除する（v1.3.0：第 4 列は seq）。</summary>
    public async Task DeleteAsync(
        int episodeId, bool isBroadcastOnly, string themeKind, byte seq,
        CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM episode_theme_songs
            WHERE episode_id        = @EpisodeId
              AND is_broadcast_only = @Flag
              AND theme_kind        = @ThemeKind
              AND seq               = @Seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                EpisodeId = episodeId,
                Flag = isBroadcastOnly ? 1 : 0,
                ThemeKind = themeKind,
                Seq = seq
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// 同一 (episode_id, is_broadcast_only) グループ内の主題歌行について
    /// <c>seq</c>（劇中順）を一括再採番する（v1.3.0：旧 BulkUpdateInsertSeqAsync を改名・拡張）。
    /// <para>
    /// 旧仕様では「INSERT 行のみ」が再採番対象だったが、新仕様では OP/ED/INSERT 全種が
    /// 1 つの劇中順に統合されるため、グループ内の全行を一度に並べ替えられる。
    /// </para>
    /// <para>
    /// PK が <c>(episode_id, is_broadcast_only, theme_kind, seq)</c> の 4 列複合のため、
    /// 並べ替えで seq を入れ替える際は PK 衝突を避けるべく、いったん DELETE → INSERT する
    /// トランザクション設計（episode_theme_songs は AUTO_INCREMENT 列を持たない自然キー表）。
    /// </para>
    /// </summary>
    /// <param name="episodeId">対象エピソード。</param>
    /// <param name="isBroadcastOnly">本放送限定フラグ（PK の一部）。</param>
    /// <param name="orderedRows">
    /// 並べ替え後の行リスト。先頭が seq=1、次が 2、... に再採番される。
    /// 全行が同じ <paramref name="episodeId"/> / <paramref name="isBroadcastOnly"/> である必要がある
    /// （混在は ArgumentException）。theme_kind は OP/ED/INSERT の混在を許容する。
    /// </param>
    public async Task BulkUpdateSeqAsync(
        int episodeId,
        bool isBroadcastOnly,
        IList<EpisodeThemeSong> orderedRows,
        CancellationToken ct = default)
    {
        if (orderedRows is null) throw new ArgumentNullException(nameof(orderedRows));
        if (orderedRows.Count == 0) return;
        if (orderedRows.Any(r => r.EpisodeId != episodeId
                                  || r.IsBroadcastOnly != isBroadcastOnly))
        {
            throw new ArgumentException(
                "BulkUpdateSeqAsync: orderedRows に対象グループ外の行 " +
                "（異なる episode_id / is_broadcast_only）が混在しています。",
                nameof(orderedRows));
        }

        const string sqlDelete = """
            DELETE FROM episode_theme_songs
             WHERE episode_id        = @EpisodeId
               AND is_broadcast_only = @Flag;
            """;
        const string sqlInsert = """
            INSERT INTO episode_theme_songs
              (episode_id, is_broadcast_only, theme_kind, seq,
               song_recording_id, notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @Flag, @ThemeKind, @Seq,
               @SongRecordingId, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. 同グループ全行をいったん全削除（グループ外には影響しない）
            await conn.ExecuteAsync(new CommandDefinition(
                sqlDelete,
                new { EpisodeId = episodeId, Flag = isBroadcastOnly ? 1 : 0 },
                transaction: tx, cancellationToken: ct));

            // 2. 与えられた順序で 1, 2, 3, ... と seq を振り直して INSERT
            byte seq = 1;
            foreach (var r in orderedRows)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    sqlInsert,
                    new
                    {
                        EpisodeId = episodeId,
                        Flag = isBroadcastOnly ? 1 : 0,
                        ThemeKind = r.ThemeKind,
                        Seq = seq,
                        SongRecordingId = r.SongRecordingId,
                        Notes = r.Notes,
                        CreatedBy = r.CreatedBy,
                        UpdatedBy = Environment.UserName
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
