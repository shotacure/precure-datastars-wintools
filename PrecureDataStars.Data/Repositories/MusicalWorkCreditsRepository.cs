using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// musical_work_credits テーブル（曲（著作物）の作家の連名）のリポジトリ。
/// 1 つの曲（著作物）（work_id）に対して、役職（credit_role。歌の作家と同じ役職コード）ごとに
/// 連名を順序付き（credit_seq）で持つ。編集は役職単位の丸ごと差し替え（<see cref="ReplaceAllByRoleAsync"/>）で行う。
/// </summary>
public sealed class MusicalWorkCreditsRepository : RepositoryBase
{
    public MusicalWorkCreditsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          work_id              AS WorkId,
          credit_role          AS CreditRole,
          credit_seq           AS CreditSeq,
          person_alias_id      AS PersonAliasId,
          preceding_separator  AS PrecedingSeparator,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>役職の並び（歌の作家と同じ LYRICS → COMPOSITION → ARRANGEMENT、並びに無い役職は末尾で role_code 昇順）。</summary>
    private const string RoleOrder = "FIELD(credit_role,'LYRICS','COMPOSITION','ARRANGEMENT') = 0, FIELD(credit_role,'LYRICS','COMPOSITION','ARRANGEMENT'), credit_role, credit_seq";

    /// <summary>全行を取得する（work_id → 役職の並び → credit_seq）。SiteBuilder の起動時の全件ロード用。</summary>
    public async Task<IReadOnlyList<MusicalWorkCredit>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM musical_work_credits
            ORDER BY work_id, {RoleOrder};
            """;
        return await QueryListAsync<MusicalWorkCredit>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定した曲（著作物）の作家を役職の並び → credit_seq で取得する。</summary>
    public async Task<IReadOnlyList<MusicalWorkCredit>> GetByWorkAsync(int workId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM musical_work_credits
            WHERE work_id = @workId
            ORDER BY {RoleOrder};
            """;
        return await QueryListAsync<MusicalWorkCredit>(sql, new { workId }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定した曲（著作物）・役職の連名を丸ごと差し替える（既存全削除 → 渡された並びどおり credit_seq を 1 から振り直して INSERT）。
    /// 先頭行の区切り文字は持たない。1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int workId, string role, IReadOnlyList<MusicalWorkCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM musical_work_credits WHERE work_id = @WorkId AND credit_role = @Role;",
                new { WorkId = workId, Role = role },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO musical_work_credits
                      (work_id, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
                    VALUES
                      (@WorkId, @Role, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        WorkId = workId,
                        Role = role,
                        CreditSeq = seq,
                        c.PersonAliasId,
                        PrecedingSeparator = seq == 1 ? null : c.PrecedingSeparator,
                        c.Notes,
                        CreatedBy = updatedBy,
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
