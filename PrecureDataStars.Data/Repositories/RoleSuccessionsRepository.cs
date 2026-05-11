
using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// role_successions テーブル（役職系譜の関係エンティティ）の CRUD リポジトリ
/// （v1.3.0 ブラッシュアップ続編で新設）。
/// <para>
/// 役職の系譜は分裂・併合を含む多対多の関係なので、from_role_code → to_role_code の
/// 有向辺を 1 行 1 関係として持つ関係テーブル方式で表現する。1 つの from は複数の to を
/// 持てるし、複数の from から同じ to へ集約することもできる。
/// </para>
/// <para>
/// 自己ループ（FromRoleCode == ToRoleCode）は DB の CHECK 制約で禁止されているため、
/// 呼び出し側の事前チェックは不要。
/// </para>
/// </summary>
public sealed class RoleSuccessionsRepository
{
    private readonly IConnectionFactory _factory;

    public RoleSuccessionsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（from_role_code, to_role_code の 2 列で並べ替え）。</summary>
    public async Task<IReadOnlyList<RoleSuccession>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              from_role_code           AS FromRoleCode,
              to_role_code             AS ToRoleCode,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM role_successions
            ORDER BY from_role_code, to_role_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleSuccession>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定の from_role_code を起点とする系譜行（後任候補）を取得する。</summary>
    public async Task<IReadOnlyList<RoleSuccession>> GetByFromAsync(string fromRoleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              from_role_code           AS FromRoleCode,
              to_role_code             AS ToRoleCode,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM role_successions
            WHERE from_role_code = @fromRoleCode
            ORDER BY to_role_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleSuccession>(
            new CommandDefinition(sql, new { fromRoleCode }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定の to_role_code を終点とする系譜行（前任候補）を取得する。</summary>
    public async Task<IReadOnlyList<RoleSuccession>> GetByToAsync(string toRoleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              from_role_code           AS FromRoleCode,
              to_role_code             AS ToRoleCode,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM role_successions
            WHERE to_role_code = @toRoleCode
            ORDER BY from_role_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleSuccession>(
            new CommandDefinition(sql, new { toRoleCode }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// UPSERT（同 PK が無ければ INSERT、あれば notes と updated_by だけ更新）。
    /// 監査列のみが更新対象なので、関係本体（from / to）は新規追加と等価。
    /// <para>
    /// 自己ループ（FromRoleCode == ToRoleCode）はアプリ層でここでガードする。
    /// 本来は DB の CHECK 制約で禁止したいが、MySQL 8 では FK の参照アクション
    /// （CASCADE 等）で変更される列を CHECK で参照できない仕様（Error 3823）のため、
    /// アプリ側で弾く方針を採る。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// FromRoleCode と ToRoleCode が同一の場合に投げる。
    /// </exception>
    public async Task UpsertAsync(RoleSuccession s, CancellationToken ct = default)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));
        if (string.Equals(s.FromRoleCode, s.ToRoleCode, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"role_successions の自己ループ（from と to が同一: '{s.FromRoleCode}'）は登録できません。",
                nameof(s));
        }

        const string sql = """
            INSERT INTO role_successions
              (from_role_code, to_role_code, notes, created_by, updated_by)
            VALUES
              (@FromRoleCode, @ToRoleCode, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              notes      = VALUES(notes),
              updated_by = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, s, cancellationToken: ct));
    }

    /// <summary>指定の (from, to) 関係を削除する。</summary>
    public async Task DeleteAsync(string fromRoleCode, string toRoleCode, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM role_successions
            WHERE from_role_code = @fromRoleCode
              AND to_role_code   = @toRoleCode;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { fromRoleCode, toRoleCode }, cancellationToken: ct));
    }
}
