using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// character_voice_castings テーブル（キャラクター ⇄ 声優キャスティング）の CRUD リポジトリ。
/// <para>
/// 同一 (character_id, person_id) に対して期間や種別を変えて複数行を持てる。
/// 種別は REGULAR / SUBSTITUTE / TEMPORARY / MOB の 4 種。
/// </para>
/// </summary>
public sealed class CharacterVoiceCastingsRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterVoiceCastingsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          casting_id    AS CastingId,
          character_id  AS CharacterId,
          person_id     AS PersonId,
          casting_kind  AS CastingKind,
          valid_from    AS ValidFrom,
          valid_to      AS ValidTo,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy,
          is_deleted    AS IsDeleted
        """;

    /// <summary>全件取得（casting_id 昇順）。</summary>
    public async Task<IReadOnlyList<CharacterVoiceCasting>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_voice_castings
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY casting_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterVoiceCasting>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（casting_id）で 1 件取得する。</summary>
    public async Task<CharacterVoiceCasting?> GetByIdAsync(int castingId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_voice_castings
            WHERE casting_id = @castingId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CharacterVoiceCasting>(
            new CommandDefinition(sql, new { castingId }, cancellationToken: ct));
    }

    /// <summary>指定キャラクターに紐付くキャスティング履歴を取得する（valid_from 昇順、NULL 先頭）。</summary>
    public async Task<IReadOnlyList<CharacterVoiceCasting>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_voice_castings
            WHERE character_id = @characterId AND is_deleted = 0
            ORDER BY COALESCE(valid_from, '1900-01-01'), casting_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterVoiceCasting>(
            new CommandDefinition(sql, new { characterId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定声優に紐付くキャスティング履歴を取得する。</summary>
    public async Task<IReadOnlyList<CharacterVoiceCasting>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_voice_castings
            WHERE person_id = @personId AND is_deleted = 0
            ORDER BY COALESCE(valid_from, '1900-01-01'), casting_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterVoiceCasting>(
            new CommandDefinition(sql, new { personId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の casting_id を返す。</summary>
    public async Task<int> InsertAsync(CharacterVoiceCasting casting, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_voice_castings
              (character_id, person_id, casting_kind, valid_from, valid_to,
               notes, created_by, updated_by)
            VALUES
              (@CharacterId, @PersonId, @CastingKind, @ValidFrom, @ValidTo,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, casting, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CharacterVoiceCasting casting, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE character_voice_castings SET
              character_id  = @CharacterId,
              person_id     = @PersonId,
              casting_kind  = @CastingKind,
              valid_from    = @ValidFrom,
              valid_to      = @ValidTo,
              notes         = @Notes,
              updated_by    = @UpdatedBy,
              is_deleted    = @IsDeleted
            WHERE casting_id = @CastingId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, casting, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int castingId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE character_voice_castings SET is_deleted = 1, updated_by = @UpdatedBy WHERE casting_id = @CastingId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CastingId = castingId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
