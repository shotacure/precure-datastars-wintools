using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// role_relations テーブル（別の役職どうしの関連：段階・並列）の CRUD リポジトリ。
/// 1 組の役職に関係は 1 つ（PK = from_role_code + to_role_code）。並列は向きを持たないので、
/// <see cref="UpsertAsync"/> が role_code の序数比較で小さいほうを from にそろえてから書く。
/// 段階どうしの逆向き（A → B と B → A）や、同じ組の段階と並列の重複も入口で弾く。
/// </summary>
public sealed class RoleRelationsRepository : RepositoryBase
{
    public RoleRelationsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
        SELECT
          from_role_code           AS FromRoleCode,
          to_role_code             AS ToRoleCode,
          relation_kind            AS RelationKind,
          notes                    AS Notes,
          created_at               AS CreatedAt,
          updated_at               AS UpdatedAt,
          created_by               AS CreatedBy,
          updated_by               AS UpdatedBy
        FROM role_relations
        """;

    /// <summary>全件取得（from_role_code, to_role_code の 2 列で並べ替え）。</summary>
    public async Task<IReadOnlyList<RoleRelation>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = SelectColumns + "\nORDER BY from_role_code, to_role_code;";
        return await QueryListAsync<RoleRelation>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定の役職が from・to のどちらかに出る関係をすべて取得する（関連の編集ダイアログ用）。</summary>
    public async Task<IReadOnlyList<RoleRelation>> GetByRoleAsync(string roleCode, CancellationToken ct = default)
    {
        string sql = SelectColumns + """

            WHERE from_role_code = @roleCode OR to_role_code = @roleCode
            ORDER BY relation_kind, from_role_code, to_role_code;
            """;
        return await QueryListAsync<RoleRelation>(sql, new { roleCode }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// UPSERT（同 PK が無ければ INSERT、あれば notes と updated_by だけ更新）。
    /// 並列は from / to を role_code の序数比較で並べ替えてから書く。
    /// 自己ループ・同じ組に別の関係がすでにあるもの（逆向きの段階、段階と並列の重複）は例外で弾く。
    /// </summary>
    /// <exception cref="ArgumentException">自己ループ、または関係の種類が不正なとき。</exception>
    /// <exception cref="InvalidOperationException">同じ組の役職に別の関係がすでにあるとき。</exception>
    public async Task UpsertAsync(RoleRelation r, CancellationToken ct = default)
    {
        if (r is null) throw new ArgumentNullException(nameof(r));
        if (string.Equals(r.FromRoleCode, r.ToRoleCode, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"role_relations の自己ループ（from と to が同一: '{r.FromRoleCode}'）は登録できません。",
                nameof(r));
        }
        if (r.RelationKind is not (RoleRelationKinds.StepUp or RoleRelationKinds.Parallel))
        {
            throw new ArgumentException($"role_relations の関係の種類が不正です: '{r.RelationKind}'", nameof(r));
        }

        var row = new RoleRelation
        {
            FromRoleCode = r.FromRoleCode,
            ToRoleCode = r.ToRoleCode,
            RelationKind = r.RelationKind,
            Notes = r.Notes,
            CreatedBy = r.CreatedBy,
            UpdatedBy = r.UpdatedBy
        };
        if (row.RelationKind == RoleRelationKinds.Parallel
            && string.CompareOrdinal(row.FromRoleCode, row.ToRoleCode) > 0)
        {
            (row.FromRoleCode, row.ToRoleCode) = (row.ToRoleCode, row.FromRoleCode);
        }

        // 同じ組（向きを問わない）に別の関係が無いかを確かめる。同じ PK・同じ種類なら備考の更新として通す。
        const string existingSql = """
            SELECT from_role_code AS FromRoleCode, to_role_code AS ToRoleCode, relation_kind AS RelationKind
              FROM role_relations
             WHERE (from_role_code = @a AND to_role_code = @b)
                OR (from_role_code = @b AND to_role_code = @a);
            """;
        var existing = await QueryListAsync<RoleRelation>(existingSql, new { a = row.FromRoleCode, b = row.ToRoleCode }, ct)
            .ConfigureAwait(false);
        foreach (var e in existing)
        {
            bool samePk = string.Equals(e.FromRoleCode, row.FromRoleCode, StringComparison.Ordinal)
                          && string.Equals(e.ToRoleCode, row.ToRoleCode, StringComparison.Ordinal);
            if (samePk && string.Equals(e.RelationKind, row.RelationKind, StringComparison.Ordinal)) continue;
            throw new InvalidOperationException(
                $"役職 '{row.FromRoleCode}' と '{row.ToRoleCode}' には別の関連（{e.RelationKind}: {e.FromRoleCode} → {e.ToRoleCode}）が登録済みです。");
        }

        const string sql = """
            INSERT INTO role_relations
              (from_role_code, to_role_code, relation_kind, notes, created_by, updated_by)
            VALUES
              (@FromRoleCode, @ToRoleCode, @RelationKind, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              notes      = VALUES(notes),
              updated_by = VALUES(updated_by);
            """;

        await ExecuteAsync(sql, row, ct).ConfigureAwait(false);
    }

    /// <summary>指定の (from, to) 関係を削除する。並列はどちらの順で渡してもよい。</summary>
    public async Task DeleteAsync(string fromRoleCode, string toRoleCode, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM role_relations
            WHERE (from_role_code = @fromRoleCode AND to_role_code = @toRoleCode)
               OR (relation_kind = 'PARALLEL' AND from_role_code = @toRoleCode AND to_role_code = @fromRoleCode);
            """;
        await ExecuteAsync(sql, new { fromRoleCode, toRoleCode }, ct).ConfigureAwait(false);
    }
}
