using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_theme_songs テーブル（エピソード × 主題歌の紐付け）の CRUD リポジトリ。
/// <para>
/// 複合主キー (episode_id, theme_kind, insert_seq) を持ち、
/// OP / ED は <c>insert_seq</c>=0 の 1 行のみ、INSERT は <c>insert_seq</c>=1, 2, ... と複数可。
/// この排他は DB 側 CHECK 制約 <c>ck_ets_op_ed_no_insert_seq</c> でも担保。
/// クレジットの THEME_SONG ロールエントリは、本テーブルから歌情報を引いて
/// レンダリングする想定。
/// </para>
/// </summary>
public sealed class EpisodeThemeSongsRepository
{
    private readonly IConnectionFactory _factory;

    public EpisodeThemeSongsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          episode_id              AS EpisodeId,
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

    /// <summary>指定エピソードに紐付く主題歌一覧を取得する（theme_kind → insert_seq 昇順）。</summary>
    public async Task<IReadOnlyList<EpisodeThemeSong>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id = @episodeId
            ORDER BY theme_kind, insert_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeThemeSong>(
            new CommandDefinition(sql, new { episodeId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>複合 PK で 1 件取得する。</summary>
    public async Task<EpisodeThemeSong?> GetByKeyAsync(int episodeId, string themeKind, byte insertSeq, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_theme_songs
            WHERE episode_id = @episodeId AND theme_kind = @themeKind AND insert_seq = @insertSeq
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<EpisodeThemeSong>(
            new CommandDefinition(sql, new { episodeId, themeKind, insertSeq }, cancellationToken: ct));
    }

    /// <summary>UPSERT（PK は episode_id, theme_kind, insert_seq の 3 列）。</summary>
    public async Task UpsertAsync(EpisodeThemeSong row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO episode_theme_songs
              (episode_id, theme_kind, insert_seq, song_recording_id, label_company_alias_id,
               notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @ThemeKind, @InsertSeq, @SongRecordingId, @LabelCompanyAliasId,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              song_recording_id       = VALUES(song_recording_id),
              label_company_alias_id  = VALUES(label_company_alias_id),
              notes                   = VALUES(notes),
              updated_by              = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>複合 PK で 1 件削除する。</summary>
    public async Task DeleteAsync(int episodeId, string themeKind, byte insertSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM episode_theme_songs
            WHERE episode_id = @EpisodeId AND theme_kind = @ThemeKind AND insert_seq = @InsertSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { EpisodeId = episodeId, ThemeKind = themeKind, InsertSeq = insertSeq },
            cancellationToken: ct));
    }
}
