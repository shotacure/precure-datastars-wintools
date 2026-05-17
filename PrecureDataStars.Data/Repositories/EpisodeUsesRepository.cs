using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_uses テーブル（エピソード × パート × 使用順）の CRUD リポジトリ。
/// <para>
/// <c>tracks</c>（discs 配下）と同じ流儀で、内容種別（<c>content_kind_code</c>）に応じて
/// 参照列（song_recording_id / bgm_series_id+bgm_m_no_detail / use_title_override）を
/// 切り替えて保持する設計。詳細は <see cref="EpisodeUse"/> のドキュメント参照。
/// </para>
/// <para>
/// 主な利用シーン：
/// </para>
/// <list type="bullet">
///   <item><description>SiteBuilder のエピソード詳細ページで「このエピソードで流れた音声」をパート別に表示</description></item>
///   <item><description>SiteBuilder のシリーズ詳細ページの劇伴一覧表で「使用回数」列を出すため、
///     bgm_cues 単位の集計を逆引きする</description></item>
///   <item><description>劇伴 / 楽曲詳細ページで「この曲が流れたエピソード」逆引き</description></item>
/// </list>
/// </summary>
public sealed class EpisodeUsesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="EpisodeUsesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public EpisodeUsesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // 共通 SELECT 列。Dapper のカラムマッピング用に AS 別名で C# プロパティ名に揃える。
    private const string SelectColumns = """
          episode_id               AS EpisodeId,
          part_kind                AS PartKind,
          use_order                AS UseOrder,
          sub_order                AS SubOrder,
          content_kind_code        AS ContentKindCode,
          song_recording_id        AS SongRecordingId,
          song_size_variant_code   AS SongSizeVariantCode,
          song_part_variant_code   AS SongPartVariantCode,
          bgm_series_id            AS BgmSeriesId,
          bgm_m_no_detail          AS BgmMNoDetail,
          use_title_override       AS UseTitleOverride,
          scene_label              AS SceneLabel,
          duration_seconds         AS DurationSeconds,
          notes                    AS Notes,
          is_broadcast_only        AS IsBroadcastOnly,
          created_at               AS CreatedAt,
          updated_at               AS UpdatedAt,
          created_by               AS CreatedBy,
          updated_by               AS UpdatedBy
        """;

    /// <summary>
    /// 全 episode_uses 行を取得する。SiteBuilder のシリーズ詳細・劇伴一覧で
    /// 「この M ナンバーがエピソード何話で使われたか」を逆引きするため、起動時 1 回だけ
    /// 全件をメモリに読み込む用途。データ量がオーダーで膨らむことが想定されたら
    /// 専用の集計 API に切り替える。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeUse>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_uses
            ORDER BY episode_id, part_kind, use_order, sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeUse>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定エピソードの全使用行を取得する（part_kind, use_order, sub_order 昇順）。
    /// SiteBuilder のエピソード詳細ページで「使用音声」セクションを構築するときに使う。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeUse>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_uses
            WHERE episode_id = @episodeId
            ORDER BY part_kind, use_order, sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeUse>(new CommandDefinition(sql, new { episodeId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定の劇伴 M ナンバー（series_id + m_no_detail）が使われた episode_uses 行を取得する。
    /// 劇伴詳細ページや「劇伴使用回数」集計の逆引きに使う。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeUse>> GetByBgmCueAsync(int bgmSeriesId, string bgmMNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_uses
            WHERE bgm_series_id = @bgmSeriesId
              AND bgm_m_no_detail = @bgmMNoDetail
            ORDER BY episode_id, part_kind, use_order, sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeUse>(
            new CommandDefinition(sql, new { bgmSeriesId, bgmMNoDetail }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定楽曲録音（song_recording_id）が使われた episode_uses 行を取得する。
    /// 楽曲詳細ページの「劇中で流れた箇所」逆引き用。
    /// </summary>
    public async Task<IReadOnlyList<EpisodeUse>> GetBySongRecordingAsync(int songRecordingId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM episode_uses
            WHERE song_recording_id = @songRecordingId
            ORDER BY episode_id, part_kind, use_order, sub_order;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodeUse>(
            new CommandDefinition(sql, new { songRecordingId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定エピソードの使用行を一括置換する（既存を全削除してから一括 INSERT）。
    /// トランザクション内で実行され、途中失敗時は全体がロールバックされる。
    /// 編集 GUI（将来）の保存パスから呼ばれる想定。
    /// </summary>
    public async Task ReplaceAllForEpisodeAsync(int episodeId, IEnumerable<EpisodeUse> uses, CancellationToken ct = default)
    {
        const string deleteSql = "DELETE FROM episode_uses WHERE episode_id = @episodeId;";
        const string insertSql = """
            INSERT INTO episode_uses
              (episode_id, part_kind, use_order, sub_order, content_kind_code,
               song_recording_id, song_size_variant_code, song_part_variant_code,
               bgm_series_id, bgm_m_no_detail,
               use_title_override, scene_label, duration_seconds,
               notes, is_broadcast_only,
               created_by, updated_by)
            VALUES
              (@EpisodeId, @PartKind, @UseOrder, @SubOrder, @ContentKindCode,
               @SongRecordingId, @SongSizeVariantCode, @SongPartVariantCode,
               @BgmSeriesId, @BgmMNoDetail,
               @UseTitleOverride, @SceneLabel, @DurationSeconds,
               @Notes, @IsBroadcastOnly,
               @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { episodeId }, transaction: tx, cancellationToken: ct));
            // 入力行の episode_id が指定値と異なっていても、メソッド契約として episode_id を強制統一する。
            // （呼び出し側のミスを防ぐための保険。）
            var rows = uses.Select(u => new EpisodeUse
            {
                EpisodeId = episodeId,
                PartKind = u.PartKind,
                UseOrder = u.UseOrder,
                SubOrder = u.SubOrder,
                ContentKindCode = u.ContentKindCode,
                SongRecordingId = u.SongRecordingId,
                SongSizeVariantCode = u.SongSizeVariantCode,
                SongPartVariantCode = u.SongPartVariantCode,
                BgmSeriesId = u.BgmSeriesId,
                BgmMNoDetail = u.BgmMNoDetail,
                UseTitleOverride = u.UseTitleOverride,
                SceneLabel = u.SceneLabel,
                DurationSeconds = u.DurationSeconds,
                Notes = u.Notes,
                IsBroadcastOnly = u.IsBroadcastOnly,
                CreatedBy = u.CreatedBy,
                UpdatedBy = u.UpdatedBy
            }).ToList();
            if (rows.Count > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(insertSql, rows, transaction: tx, cancellationToken: ct));
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