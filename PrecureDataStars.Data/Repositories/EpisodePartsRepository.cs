using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Data;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_parts テーブルの CRUD リポジトリ。
/// <para>
/// エピソードを構成する各パート（アバン、A/B パート、ED、次回予告等）の
/// 追加・更新・削除・一括置換・差分適用と、パート尺の統計分析クエリを提供する。
/// </para>
/// </summary>
public sealed class EpisodePartsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="EpisodePartsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public EpisodePartsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // ────────────────────────────────────────────────
    //  基本 CRUD
    // ────────────────────────────────────────────────

    /// <summary>
    /// 指定エピソードのパート一覧を episode_seq 昇順で取得する。
    /// </summary>
    /// <param name="episodeId">エピソード ID。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>パート一覧。</returns>
    public async Task<IReadOnlyList<EpisodePart>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              episode_id   AS EpisodeId,
              episode_seq  AS EpisodeSeq,
              part_type    AS PartType,
              oa_length    AS OaLength,
              disc_length  AS DiscLength,
              vod_length   AS VodLength,
              notes        AS Notes,
              created_by   AS CreatedBy,
              updated_by   AS UpdatedBy
            FROM episode_parts
            WHERE episode_id = @episodeId
            ORDER BY episode_seq;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EpisodePart>(new CommandDefinition(sql, new { episodeId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// パートを 1 件 INSERT する（複合 PK: episode_id + episode_seq）。
    /// </summary>
    /// <param name="p">挿入対象のパート。EpisodeId / EpisodeSeq / PartType は必須。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">必須項目が未設定の場合。</exception>
    public async Task InsertAsync(EpisodePart p, CancellationToken ct = default)
    {
        if (p.EpisodeId <= 0) throw new ArgumentException("EpisodeId is required.", nameof(p));
        if (p.EpisodeSeq < 1) throw new ArgumentException("EpisodeSeq must be >= 1.", nameof(p)); // :contentReference[oaicite:2]{index=2}
        if (string.IsNullOrWhiteSpace(p.PartType)) throw new ArgumentException("PartType is required.", nameof(p));

        const string sql = """
            INSERT INTO episode_parts(
              episode_id, episode_seq, part_type,
              oa_length, disc_length, vod_length,
              notes, created_by, updated_by
            ) VALUES (
              @EpisodeId, @EpisodeSeq, @PartType,
              @OaLength, @DiscLength, @VodLength,
              @Notes, @CreatedBy, @UpdatedBy
            );
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    /// <summary>
    /// パートを 1 件 UPDATE する（主キー一致）。episode_seq は変更しない。
    /// </summary>
    /// <param name="p">更新対象のパート。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">EpisodeId が不正、または EpisodeSeq が 0 の場合。</exception>
    public async Task UpdateAsync(EpisodePart p, CancellationToken ct = default)
    {
        if (p.EpisodeId <= 0) throw new ArgumentException("EpisodeId is required.", nameof(p));
        if (p.EpisodeSeq < 1) throw new ArgumentException("EpisodeSeq must be >= 1.", nameof(p));

        const string sql = """
            UPDATE episode_parts SET
              part_type   = @PartType,
              oa_length   = @OaLength,
              disc_length = @DiscLength,
              vod_length  = @VodLength,
              notes       = @Notes,
              updated_by  = @UpdatedBy
            WHERE episode_id = @EpisodeId
              AND episode_seq = @EpisodeSeq;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    /// <summary>
    /// パートを 1 件 DELETE する（主キー指定）。
    /// </summary>
    /// <param name="episodeId">エピソード ID。</param>
    /// <param name="episodeSeq">削除対象のパート連番。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task DeleteAsync(int episodeId, byte episodeSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM episode_parts
            WHERE episode_id = @episodeId AND episode_seq = @episodeSeq;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { episodeId, episodeSeq }, cancellationToken: ct));
    }

    // ────────────────────────────────────────────────
    //  一括置換（全パートを差し替え）
    // ────────────────────────────────────────────────

    /// <summary>
    /// 指定エピソードの全パートを、与えられたリストで置換する（トランザクション使用）。
    /// 既存行をすべて DELETE してから新規 INSERT する。
    /// </summary>
    /// <param name="episodeId">エピソード ID。</param>
    /// <param name="parts">置換後のパート一覧。各要素の EpisodeId は <paramref name="episodeId"/> と一致する必要がある。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">パートの EpisodeId 不一致、または必須項目が未設定の場合。</exception>
    public async Task ReplaceAllForEpisodeAsync(int episodeId, IEnumerable<EpisodePart> parts, CancellationToken ct = default)
    {
        // トランザクション開始: 全行 DELETE → 新しいパート群を INSERT の順で一括置換
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Step 1: 既存パートを全削除（CASCADE は使わず明示的に DELETE）
        const string del = "DELETE FROM episode_parts WHERE episode_id = @episodeId;";
        await conn.ExecuteAsync(new CommandDefinition(del, new { episodeId }, transaction: tx, cancellationToken: ct));

        const string ins = """
            INSERT INTO episode_parts(
              episode_id, episode_seq, part_type,
              oa_length, disc_length, vod_length,
              notes, created_by, updated_by
            ) VALUES (
              @EpisodeId, @EpisodeSeq, @PartType,
              @OaLength, @DiscLength, @VodLength,
              @Notes, @CreatedBy, @UpdatedBy
            );
        """;

        // Step 2: 新しいパート群を 1 件ずつ INSERT（妥当性チェック付き）
        foreach (var p in parts)
        {
            // 最低限の妥当性（FK: part_type は part_types に定義済みであること）:contentReference[oaicite:3]{index=3}
            if (p.EpisodeId != episodeId) throw new ArgumentException("EpisodeId mismatch in parts.");
            if (p.EpisodeSeq < 1) throw new ArgumentException("EpisodeSeq must be >= 1.");
            if (string.IsNullOrWhiteSpace(p.PartType)) throw new ArgumentException("PartType is required.");

            await conn.ExecuteAsync(new CommandDefinition(ins, p, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ────────────────────────────────────────────────
    //  差分操作（GUI からの細かい編集を一括適用）
    // ────────────────────────────────────────────────

    /// <summary>
    /// GUI のパートグリッドで行われた編集操作（削除・並び替え・更新・追加）を
    /// まとめて適用するための操作セット。
    /// </summary>
    public sealed class EpisodePartOps
    {
        /// <summary>削除対象の episode_seq 一覧。</summary>
        public List<byte> Deletes { get; set; } = new();

        /// <summary>並び順変更の一覧（旧 seq → 新 seq）。</summary>
        public List<(byte OldSeq, byte NewSeq)> Moves { get; set; } = new();

        /// <summary>内容のみ更新するパート一覧（seq は変更しない）。</summary>
        public List<EpisodePart> Updates { get; set; } = new();

        /// <summary>新規追加するパート一覧。</summary>
        public List<EpisodePart> Inserts { get; set; } = new();
    }

    /// <summary>
    /// <see cref="EpisodePartOps"/> で定義された差分操作を単一トランザクションで適用する。
    /// <para>
    /// 実行順序:
    /// <list type="number">
    ///   <item>DELETE — 先に空きを作る</item>
    ///   <item>MOVE — episode_seq の付け替え（CASE 式で一括更新）</item>
    ///   <item>UPDATE — 内容のみ変更（seq は変更しない）</item>
    ///   <item>INSERT — 不足分の新規追加</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="episodeId">対象エピソード ID。</param>
    /// <param name="ops">適用する操作セット。</param>
    /// <param name="auditUser">監査用ユーザー名（updated_by / created_by に設定）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ApplyOpsAsync(
    int episodeId,
    EpisodePartOps ops,
    string auditUser,
    CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // 1) DELETE：先に空きを作る
        if (ops.Deletes.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM episode_parts WHERE episode_id = @episodeId AND episode_seq IN @seqs;",
                new { episodeId, seqs = ops.Deletes },
                transaction: tx, cancellationToken: ct));
        }

        // 2) MOVE：入替・位置変更（seqだけ付替え）→ CASE 一発
        if (ops.Moves.Count > 0)
        {
            // OldSeq が重複した場合は最後の指示を優先（GroupBy + Last）
            // CASE 式で複数行の episode_seq を一括振替（1 SQL で完了）
            var map = ops.Moves.GroupBy(m => m.OldSeq).ToDictionary(g => g.Key, g => g.Last().NewSeq);
            var whenClauses = string.Join("\n", map.Select(kv => $"WHEN {kv.Key} THEN {kv.Value}"));
            var inList = string.Join(",", map.Keys.OrderBy(x => x));

            var sql = $@"
            UPDATE episode_parts
               SET episode_seq = CASE episode_seq
                   {whenClauses}
                   ELSE episode_seq
               END,
                   updated_by = @auditUser
             WHERE episode_id = @episodeId
               AND episode_seq IN ({inList});";

            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { episodeId, auditUser }, transaction: tx, cancellationToken: ct));
        }

        // 3) UPDATE：内容のみ（seq は変更しない）
        foreach (var u in ops.Updates)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
            UPDATE episode_parts SET
              part_type   = @PartType,
              oa_length   = @OaLength,
              disc_length = @DiscLength,
              vod_length  = @VodLength,
              notes       = @Notes,
              updated_by  = @auditUser
            WHERE episode_id = @EpisodeId
              AND episode_seq = @EpisodeSeq;
            """,
                new { u.EpisodeId, u.EpisodeSeq, u.PartType, u.OaLength, u.DiscLength, u.VodLength, u.Notes, auditUser },
                transaction: tx, cancellationToken: ct));
        }

        // 4) INSERT：不足分の新規追加
        if (ops.Inserts.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
            INSERT INTO episode_parts
              (episode_id, episode_seq, part_type, oa_length, disc_length, vod_length, notes, created_by, updated_by)
            VALUES
              (@EpisodeId, @EpisodeSeq, @PartType, @OaLength, @DiscLength, @VodLength, @Notes, @auditUser, @auditUser);
            """,
                ops.Inserts.Select(r => new {
                    r.EpisodeId,
                    r.EpisodeSeq,
                    r.PartType,
                    r.OaLength,
                    r.DiscLength,
                    r.VodLength,
                    r.Notes,
                    auditUser
                }),
                transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ────────────────────────────────────────────────
    //  パート尺の統計分析（偏差値・順位）
    // ────────────────────────────────────────────────

    /// <summary>
    /// パート尺（OA 尺）の統計情報を保持する DTO。
    /// シリーズ内および全シリーズ横断（歴代）での順位と偏差値を含む。
    /// </summary>
    public sealed class PartLengthStat
    {
        /// <summary>パート種別コード（例: "AVANT", "PART_A", "PART_B"）。</summary>
        public required string PartType { get; init; }

        /// <summary>パート種別の日本語名（part_types.name_ja）。</summary>
        public required string PartTypeNameJa { get; init; }

        /// <summary>所属シリーズの略称（series.title_short）。</summary>
        public required string SeriesTitleShort { get; init; }

        /// <summary>シリーズ内での OA 尺順位（降順、1 が最長）。</summary>
        public required int SeriesRank { get; init; }

        /// <summary>シリーズ内の同種パートを持つエピソード総数。</summary>
        public required int SeriesTotal { get; init; }

        /// <summary>シリーズ内での OA 尺偏差値（平均 50、標準偏差 10）。</summary>
        public required double SeriesHensachi { get; init; }

        /// <summary>全シリーズ横断（歴代）での OA 尺順位。</summary>
        public required int GlobalRank { get; init; }

        /// <summary>全シリーズ横断の同種パートを持つエピソード総数。</summary>
        public required int GlobalTotal { get; init; }

        /// <summary>全シリーズ横断での OA 尺偏差値。</summary>
        public required double GlobalHensachi { get; init; }
    }

    /// <summary>
    /// 指定エピソードの AVANT / PART_A / PART_B パートについて、
    /// シリーズ内および歴代での OA 尺順位・偏差値を算出する。
    /// <para>
    /// ウィンドウ関数（RANK / AVG / STDDEV_POP）を使い、
    /// シリーズ別・パート種別別および全体でのランキングを一括取得する。
    /// </para>
    /// </summary>
    /// <param name="episodeId">対象エピソードの ID。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>パート種別ごとの統計情報。対象パートが存在しない場合は空リスト。</returns>
    public async Task<IReadOnlyList<PartLengthStat>> GetPartLengthStatsAsync(
    int episodeId,
    CancellationToken ct = default)
    {
        const string sql = @"
WITH parts AS (
    SELECT
        e.episode_id,
        e.series_id,
        sr.title_short,
        ep.part_type,
        SUM(ep.oa_length) AS seconds
    FROM episodes e
    JOIN episode_parts ep
      ON ep.episode_id = e.episode_id
    JOIN series sr
      ON e.series_id = sr.series_id
    WHERE ep.part_type IN ('AVANT','PART_A','PART_B')
      AND e.is_deleted = 0
    GROUP BY e.episode_id, e.series_id, ep.part_type
),
series_stats AS (
    SELECT
        p.episode_id,
        p.part_type,
        RANK() OVER (
            PARTITION BY p.series_id, p.part_type
            ORDER BY p.seconds DESC
        ) AS series_rank,
        COUNT(*) OVER (
            PARTITION BY p.series_id, p.part_type
        ) AS series_total,
        AVG(p.seconds) OVER (
            PARTITION BY p.series_id, p.part_type
        ) AS series_avg,
        STDDEV_POP(p.seconds) OVER (
            PARTITION BY p.series_id, p.part_type
        ) AS series_std
    FROM parts p
),
global_stats AS (
    SELECT
        p.episode_id,
        p.part_type,
        RANK() OVER (
            PARTITION BY p.part_type
            ORDER BY p.seconds DESC
        ) AS global_rank,
        COUNT(*) OVER (
            PARTITION BY p.part_type
        ) AS global_total,
        AVG(p.seconds) OVER (
            PARTITION BY p.part_type
        ) AS global_avg,
        STDDEV_POP(p.seconds) OVER (
            PARTITION BY p.part_type
        ) AS global_std
    FROM parts p
)
SELECT
    p.part_type          AS PartType,
    pt.name_ja           AS PartTypeNameJa,
    p.title_short        AS SeriesTitleShort,
    s.series_rank        AS SeriesRank,
    s.series_total       AS SeriesTotal,
    50.0 + 10.0 * (p.seconds - s.series_avg) / NULLIF(s.series_std, 0) AS SeriesHensachi,
    g.global_rank        AS GlobalRank,
    g.global_total       AS GlobalTotal,
    50.0 + 10.0 * (p.seconds - g.global_avg) / NULLIF(g.global_std, 0) AS GlobalHensachi
FROM parts p
JOIN series_stats s
  ON s.episode_id = p.episode_id
 AND s.part_type   = p.part_type
JOIN global_stats g
  ON g.episode_id = p.episode_id
 AND g.part_type   = p.part_type
JOIN part_types pt
  ON pt.part_type = p.part_type
WHERE p.episode_id = @EpisodeId;
";

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PartLengthStat>(
            new CommandDefinition(sql, new { EpisodeId = episodeId }, cancellationToken: ct));
        return rows.ToList();
    }
}
