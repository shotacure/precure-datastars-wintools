using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// person_alias_members テーブル（ユニット名義の構成メンバー）の CRUD リポジトリ。
/// <para>
/// 親 alias（ユニット側）に対し、メンバーを順序付き（<see cref="PersonAliasMember.MemberSeq"/>）で
/// 持つ。メンバー種別は PERSON / CHARACTER の 2 値。
/// ネスト禁止は DB トリガーで担保されているため、本リポジトリでは事前バリデーションは
/// 行わずに DB に投げ、違反時の例外を呼び出し側で受ける運用にする。
/// </para>
/// </summary>
public sealed class PersonAliasMembersRepository
{
    private readonly IConnectionFactory _factory;

    public PersonAliasMembersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          parent_alias_id            AS ParentAliasId,
          member_seq                 AS MemberSeq,
          member_kind                AS MemberKind,
          member_person_alias_id     AS MemberPersonAliasId,
          member_character_alias_id  AS MemberCharacterAliasId,
          notes                      AS Notes,
          created_at                 AS CreatedAt,
          updated_at                 AS UpdatedAt,
          created_by                 AS CreatedBy,
          updated_by                 AS UpdatedBy
        """;

    /// <summary>指定ユニット alias のメンバー一覧を seq 昇順で取得する。</summary>
    public async Task<IReadOnlyList<PersonAliasMember>> GetByParentAsync(int parentAliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_alias_members
            WHERE parent_alias_id = @parentAliasId
            ORDER BY member_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAliasMember>(
            new CommandDefinition(sql, new { parentAliasId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定 alias がいずれかのユニットの「メンバー」として登録されているかを返す
    /// （ネスト判定の事前チェック等に使用）。
    /// </summary>
    public async Task<bool> IsRegisteredAsMemberAsync(int personAliasId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM person_alias_members
            WHERE member_kind = 'PERSON'
              AND member_person_alias_id = @personAliasId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        int n = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { personAliasId }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>
    /// 指定 alias が「ユニット」として何らかのメンバーを持っているかを返す
    /// （表示時に「これはユニット alias である」と判定するのに使う）。
    /// </summary>
    public async Task<bool> HasMembersAsync(int parentAliasId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM person_alias_members WHERE parent_alias_id = @parentAliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        int n = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { parentAliasId }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>新規追加。member_seq は呼び出し側で決めて渡す前提。</summary>
    public async Task InsertAsync(PersonAliasMember m, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO person_alias_members
              (parent_alias_id, member_seq, member_kind,
               member_person_alias_id, member_character_alias_id,
               notes, created_by, updated_by)
            VALUES
              (@ParentAliasId, @MemberSeq, @MemberKindStr,
               @MemberPersonAliasId, @MemberCharacterAliasId,
               @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            m.ParentAliasId,
            m.MemberSeq,
            MemberKindStr = m.MemberKind == PersonAliasMemberKind.Person ? "PERSON" : "CHARACTER",
            m.MemberPersonAliasId,
            m.MemberCharacterAliasId,
            m.Notes,
            m.CreatedBy,
            m.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>更新（PK は parent_alias_id + member_seq、メンバー本体・備考のみ書き換え可）。</summary>
    public async Task UpdateAsync(PersonAliasMember m, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE person_alias_members SET
              member_kind                = @MemberKindStr,
              member_person_alias_id     = @MemberPersonAliasId,
              member_character_alias_id  = @MemberCharacterAliasId,
              notes                      = @Notes,
              updated_by                 = @UpdatedBy
            WHERE parent_alias_id = @ParentAliasId
              AND member_seq      = @MemberSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            m.ParentAliasId,
            m.MemberSeq,
            MemberKindStr = m.MemberKind == PersonAliasMemberKind.Person ? "PERSON" : "CHARACTER",
            m.MemberPersonAliasId,
            m.MemberCharacterAliasId,
            m.Notes,
            m.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int parentAliasId, byte memberSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM person_alias_members
            WHERE parent_alias_id = @ParentAliasId
              AND member_seq      = @MemberSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ParentAliasId = parentAliasId, MemberSeq = memberSeq }, cancellationToken: ct));
    }

    /// <summary>指定ユニットのメンバーを全削除。</summary>
    public async Task DeleteAllByParentAsync(int parentAliasId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM person_alias_members WHERE parent_alias_id = @ParentAliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ParentAliasId = parentAliasId }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定ユニット alias の構成メンバーを丸ごと差し替える（既存全削除 → 新セットを順序通りに INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllAsync(int parentAliasId, IReadOnlyList<PersonAliasMember> members, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM person_alias_members WHERE parent_alias_id = @ParentAliasId;",
                new { ParentAliasId = parentAliasId },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var m in members)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO person_alias_members
                      (parent_alias_id, member_seq, member_kind,
                       member_person_alias_id, member_character_alias_id,
                       notes, created_by, updated_by)
                    VALUES
                      (@ParentAliasId, @MemberSeq, @MemberKindStr,
                       @MemberPersonAliasId, @MemberCharacterAliasId,
                       @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        ParentAliasId = parentAliasId,
                        MemberSeq = seq,
                        MemberKindStr = m.MemberKind == PersonAliasMemberKind.Person ? "PERSON" : "CHARACTER",
                        m.MemberPersonAliasId,
                        m.MemberCharacterAliasId,
                        m.Notes,
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