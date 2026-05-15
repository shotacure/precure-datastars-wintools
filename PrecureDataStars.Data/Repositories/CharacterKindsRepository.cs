using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// character_kinds テーブル（キャラクター区分マスタ）の CRUD リポジトリ。
/// <para>
/// CRUD に加えて、display_order の 10 単位飛び番一括再採番
/// （<see cref="BulkUpdateDisplayOrderAsync"/>）を提供。マスタ画面の DnD 並べ替えで使う。
/// </para>
/// </summary>
public sealed class CharacterKindsRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          character_kind  AS CharacterKindCode,
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
    public async Task<IReadOnlyList<CharacterKind>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_kinds
            ORDER BY (display_order IS NULL), display_order, character_kind;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（character_kind コード）で 1 件取得。</summary>
    public async Task<CharacterKind?> GetByCodeAsync(string characterKindCode, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_kinds
            WHERE character_kind = @code
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CharacterKind>(
            new CommandDefinition(sql, new { code = characterKindCode }, cancellationToken: ct));
    }

    /// <summary>新規作成。</summary>
    public async Task InsertAsync(CharacterKind row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_kinds
              (character_kind, name_ja, name_en, display_order, notes, created_by, updated_by)
            VALUES
              (@CharacterKindCode, @NameJa, @NameEn, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>更新（character_kind コードはキーなので変更不可）。</summary>
    public async Task UpdateAsync(CharacterKind row, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE character_kinds SET
              name_ja        = @NameJa,
              name_en        = @NameEn,
              display_order  = @DisplayOrder,
              notes          = @Notes,
              updated_by     = @UpdatedBy
            WHERE character_kind = @CharacterKindCode;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>削除（参照されていない場合のみ成功。FK で characters から守られている）。</summary>
    public async Task DeleteAsync(string characterKindCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM character_kinds WHERE character_kind = @code;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { code = characterKindCode }, cancellationToken: ct));
    }

    /// <summary>
    /// display_order を 10 単位の飛び番（10, 20, 30, ...）で一括再採番する。
    /// マスタ画面の DnD 並べ替え後に呼び出す。UNIQUE 制約があるため
    /// いったん退避値（200, 201, ...）に逃がしてから本番値で再設定する 2 段階方式。
    /// </summary>
    public async Task BulkUpdateDisplayOrderAsync(
        IEnumerable<string> orderedCharacterKindCodes,
        CancellationToken ct = default)
    {
        if (orderedCharacterKindCodes is null) throw new ArgumentNullException(nameof(orderedCharacterKindCodes));
        var list = orderedCharacterKindCodes.ToList();
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
                    "UPDATE character_kinds SET display_order = @Tmp WHERE character_kind = @Code;",
                    new { Tmp = tempOrder, Code = code },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値（10, 20, 30, ...）
            int order = 10;
            foreach (var code in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE character_kinds SET display_order = @DisplayOrder WHERE character_kind = @Code;",
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