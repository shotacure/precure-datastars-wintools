using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// character_family_relations テーブル（家族関係、汎用、v1.2.4 新設）の CRUD リポジトリ。
/// <para>
/// 1 行 = 「<c>character_id</c> から見た <c>related_character_id</c> の続柄」。
/// 双方向で表現する場合は別途 (related_character_id, character_id, 逆続柄) を立てる運用
/// （自動補完なし）。家族リスト編集 UI は character_id 単位で <see cref="GetByCharacterAsync"/> を
/// 呼んで一覧表示し、追加・削除・並べ替えを行う。
/// </para>
/// </summary>
public sealed class CharacterFamilyRelationsRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterFamilyRelationsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          character_id          AS CharacterId,
          related_character_id  AS RelatedCharacterId,
          relation_code         AS RelationCode,
          display_order         AS DisplayOrder,
          notes                 AS Notes,
          created_at            AS CreatedAt,
          updated_at            AS UpdatedAt,
          created_by            AS CreatedBy,
          updated_by            AS UpdatedBy
        """;

    /// <summary>
    /// 自分（character_id）視点の家族関係を全件取得する（display_order 昇順 → relation_code 昇順）。
    /// プリキュア編集 GUI の家族グリッドで使う。
    /// </summary>
    public async Task<IReadOnlyList<CharacterFamilyRelation>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_family_relations
            WHERE character_id = @characterId
            ORDER BY (display_order IS NULL), display_order, relation_code, related_character_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterFamilyRelation>(
            new CommandDefinition(sql, new { characterId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 自分視点の家族関係を、相手キャラの表示名と続柄表示名込みで取得する。
    /// グリッド表示用の軽量プロジェクション。
    /// </summary>
    public async Task<IReadOnlyList<CharacterFamilyRelationListRow>> GetByCharacterWithNamesAsync(int characterId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              cfr.character_id          AS CharacterId,
              cfr.related_character_id  AS RelatedCharacterId,
              cr.name                   AS RelatedCharacterName,
              cfr.relation_code         AS RelationCode,
              crk.name_ja               AS RelationName,
              cfr.display_order         AS DisplayOrder,
              cfr.notes                 AS Notes
            FROM character_family_relations cfr
            LEFT JOIN characters                cr  ON cr.character_id   = cfr.related_character_id
            LEFT JOIN character_relation_kinds  crk ON crk.relation_code = cfr.relation_code
            WHERE cfr.character_id = @characterId
            ORDER BY (cfr.display_order IS NULL), cfr.display_order, crk.display_order, cfr.relation_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterFamilyRelationListRow>(
            new CommandDefinition(sql, new { characterId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。複合 PK のため UPSERT は <see cref="UpsertAsync"/> を使用。</summary>
    public async Task InsertAsync(CharacterFamilyRelation row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_family_relations
              (character_id, related_character_id, relation_code, display_order, notes, created_by, updated_by)
            VALUES
              (@CharacterId, @RelatedCharacterId, @RelationCode, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>UPSERT（display_order と notes のみ更新可能。同じ 3 つ組キーなら上書き）。</summary>
    public async Task UpsertAsync(CharacterFamilyRelation row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_family_relations
              (character_id, related_character_id, relation_code, display_order, notes, created_by, updated_by)
            VALUES
              (@CharacterId, @RelatedCharacterId, @RelationCode, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              display_order = VALUES(display_order),
              notes         = VALUES(notes),
              updated_by    = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>削除（複合 PK 指定）。</summary>
    public async Task DeleteAsync(int characterId, int relatedCharacterId, string relationCode, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM character_family_relations
            WHERE character_id = @CharacterId
              AND related_character_id = @RelatedCharacterId
              AND relation_code = @RelationCode;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { CharacterId = characterId, RelatedCharacterId = relatedCharacterId, RelationCode = relationCode },
            cancellationToken: ct));
    }

    /// <summary>
    /// 指定 character_id 配下の全関係を 1 トランザクションで置き換える（家族グリッド保存ボタン用）。
    /// 既存行を一括 DELETE してから新規行を INSERT する素朴な実装。display_order の整合性も保てる。
    /// </summary>
    public async Task ReplaceAllForCharacterAsync(
        int characterId,
        IEnumerable<CharacterFamilyRelation> rows,
        CancellationToken ct = default)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        var list = rows.ToList();

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 既存削除
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM character_family_relations WHERE character_id = @characterId;",
                new { characterId }, transaction: tx, cancellationToken: ct));

            // 新規投入
            const string insertSql = """
                INSERT INTO character_family_relations
                  (character_id, related_character_id, relation_code, display_order, notes, created_by, updated_by)
                VALUES
                  (@CharacterId, @RelatedCharacterId, @RelationCode, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy);
                """;

            foreach (var row in list)
            {
                row.CharacterId = characterId; // 安全のため上書き
                await conn.ExecuteAsync(new CommandDefinition(insertSql, row, transaction: tx, cancellationToken: ct));
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

/// <summary>
/// 家族関係グリッド表示用の軽量プロジェクション（v1.2.4）。
/// 相手キャラ名・続柄表示名を結合済みで持つ。
/// </summary>
public sealed class CharacterFamilyRelationListRow
{
    public int CharacterId { get; set; }
    public int RelatedCharacterId { get; set; }
    public string? RelatedCharacterName { get; set; }
    public string RelationCode { get; set; } = "";
    public string? RelationName { get; set; }
    public byte? DisplayOrder { get; set; }
    public string? Notes { get; set; }
}
