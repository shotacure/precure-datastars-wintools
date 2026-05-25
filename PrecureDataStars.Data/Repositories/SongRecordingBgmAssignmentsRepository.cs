using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_recording_bgm_assignments テーブル（SONG ↔ BGM 両性紐付け）の CRUD リポジトリ。
/// <para>
/// 主キーは <c>(song_recording_id, song_part_variant_code, bgm_series_id, bgm_m_no_detail)</c>
/// の 4 列複合。<c>song_part_variant_code</c> は NOT NULL で、実パートコード
/// （'VOCAL' / 'INST' 等）か sentinel '_ANY'（パート区別なく適用）を取る。
/// 1 つの録音が複数の M ナンバーに紐付くケース（メドレートラック等）にも対応。
/// </para>
/// <para>
/// 当面の SiteBuilder 用途は読み取り専用なので最小限の取得メソッドのみ提供する。
/// 編集 UI 実装時に Upsert / Delete を追加する想定。
/// </para>
/// </summary>
public sealed class SongRecordingBgmAssignmentsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary><see cref="SongRecordingBgmAssignmentsRepository"/> の新しいインスタンスを生成する。</summary>
    public SongRecordingBgmAssignmentsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// 全件取得。SiteBuilder ではビルド開始時に一括ロードしてメモリ上の Lookup として参照する想定。
    /// 中間テーブルの行数は最大でも「両性扱いされる録音数 × パート数 × cue 数」で十分小さい
    /// （実運用では数十〜数百行程度を想定）。
    /// </summary>
    public async Task<IReadOnlyList<SongRecordingBgmAssignment>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              song_recording_id       AS SongRecordingId,
              song_part_variant_code  AS SongPartVariantCode,
              bgm_series_id           AS BgmSeriesId,
              bgm_m_no_detail         AS BgmMNoDetail,
              created_at              AS CreatedAt,
              updated_at              AS UpdatedAt,
              created_by              AS CreatedBy,
              updated_by              AS UpdatedBy
            FROM song_recording_bgm_assignments
            ORDER BY song_recording_id,
                     song_part_variant_code,
                     bgm_series_id,
                     bgm_m_no_detail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongRecordingBgmAssignment>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }
}
