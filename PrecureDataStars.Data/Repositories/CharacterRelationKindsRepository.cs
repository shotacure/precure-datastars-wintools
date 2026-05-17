using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// character_relation_kinds テーブル（キャラクター続柄マスタ）の CRUD リポジトリ。
/// <para>
/// CRUD に加えて、display_order の 10 単位飛び番一括再採番
/// （<see cref="BulkUpdateDisplayOrderAsync"/>）を提供。マスタ画面の DnD 並べ替えで使う。
/// 構造は <see cref="CharacterKindsRepository"/> と同等。
/// </para>
/// </summary>
public sealed class CharacterRelationKindsRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterRelationKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          relation_code   AS RelationCode,
          name_ja         AS NameJa,
          name_en         AS NameEn,
          display_order   AS DisplayOrder,
          notes           AS Notes,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt,
          created_by      AS CreatedBy,
          updated_by      AS UpdatedBy
        """;

    /// <summary>全件取得。display_order 昇順、未設定行は末尾に。</summary>
    public async Task<IReadOnlyList<CharacterRelationKind>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_relation_kinds
            ORDER BY (display_order IS NULL), display_order, relation_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterRelationKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（relation_code）で 1 件取得する。</summary>
    public async Task<CharacterRelationKind?> GetByCodeAsync(string relationCode, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_relation_kinds
            WHERE relation_code = @code
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CharacterRelationKind>(
            new CommandDefinition(sql, new { code = relationCode }, cancellationToken: ct));
    }

    /// <summary>新規作成。</summary>
    public async Task InsertAsync(CharacterRelationKind row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_relation_kinds
              (relation_code, name_ja, name_en, display_order, notes, created_by, updated_by)
            VALUES
              (@RelationCode, @NameJa, @NameEn, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>更新（relation_code はキーなので変更不可）。</summary>
    public async Task UpdateAsync(CharacterRelationKind row, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE character_relation_kinds SET
              name_ja        = @NameJa,
              name_en        = @NameEn,
              display_order  = @DisplayOrder,
              notes          = @Notes,
              updated_by     = @UpdatedBy
            WHERE relation_code = @RelationCode;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>削除（character_family_relations から参照されている場合は FK で守られる）。</summary>
    public async Task DeleteAsync(string relationCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM character_relation_kinds WHERE relation_code = @code;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { code = relationCode }, cancellationToken: ct));
    }

    /// <summary>
    /// display_order を 10 単位の飛び番（10, 20, 30, ...）で一括再採番する。
    /// マスタ画面の DnD 並べ替え後に呼び出す。UNIQUE 制約があるため
    /// いったん退避値（200, 201, ...）に逃がしてから本番値で再設定する 2 段階方式。
    /// </summary>
    public async Task BulkUpdateDisplayOrderAsync(
        IEnumerable<string> orderedRelationCodes,
        CancellationToken ct = default)
    {
        if (orderedRelationCodes is null) throw new ArgumentNullException(nameof(orderedRelationCodes));
        var list = orderedRelationCodes.ToList();
        if (list.Count == 0) return;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：退避値（200, 201, 202, ...）
            int i = 0;
            foreach (var code in list)
            {
                int tempOrder = 200 + i;
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE character_relation_kinds SET display_order = @Tmp WHERE relation_code = @Code;",
                    new { Tmp = tempOrder, Code = code },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値（10, 20, 30, ...）
            int order = 10;
            foreach (var code in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE character_relation_kinds SET display_order = @DisplayOrder WHERE relation_code = @Code;",
                    new { DisplayOrder = order, Code = code },
                    transaction: tx, cancellationToken: ct));
                order += 10;
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