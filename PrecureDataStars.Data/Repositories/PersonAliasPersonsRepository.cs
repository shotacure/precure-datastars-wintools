using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// person_alias_persons テーブル（人物名義 ⇄ 人物の中間テーブル）の CRUD リポジトリ。
/// <para>
/// 通常 1 alias = 1 person だが、共同名義（複数人物が 1 つの表記を共有する稀ケース）に
/// 対応するため多対多の中間表として設計している。
/// </para>
/// </summary>
public sealed class PersonAliasPersonsRepository
{
    private readonly IConnectionFactory _factory;

    public PersonAliasPersonsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          alias_id    AS AliasId,
          person_id   AS PersonId,
          person_seq  AS PersonSeq,
          created_at  AS CreatedAt,
          updated_at  AS UpdatedAt
        """;

    /// <summary>指定 alias_id に紐付く全結合行を返す（person_seq 昇順）。</summary>
    public async Task<IReadOnlyList<PersonAliasPerson>> GetByAliasAsync(int aliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_alias_persons
            WHERE alias_id = @aliasId
            ORDER BY person_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAliasPerson>(new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定 person_id に紐付く全結合行を返す（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<PersonAliasPerson>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_alias_persons
            WHERE person_id = @personId
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAliasPerson>(new CommandDefinition(sql, new { personId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規追加（重複時は ON DUPLICATE KEY UPDATE で person_seq のみ更新）。</summary>
    public async Task UpsertAsync(PersonAliasPerson row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO person_alias_persons (alias_id, person_id, person_seq)
            VALUES (@AliasId, @PersonId, @PersonSeq)
            ON DUPLICATE KEY UPDATE
              person_seq = VALUES(person_seq);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>指定 (alias_id, person_id) の関連を削除する。</summary>
    public async Task DeleteAsync(int aliasId, int personId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM person_alias_persons WHERE alias_id = @AliasId AND person_id = @PersonId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, PersonId = personId }, cancellationToken: ct));
    }

    /// <summary>指定 alias の関連を全削除（alias 削除前のクリーンアップ用）。</summary>
    public async Task DeleteByAliasAsync(int aliasId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM person_alias_persons WHERE alias_id = @AliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId }, cancellationToken: ct));
    }
}
