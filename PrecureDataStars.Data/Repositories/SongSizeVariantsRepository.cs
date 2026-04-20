using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_size_variants テーブル（曲のサイズ種別マスタ）の読み取りリポジトリ。
/// </summary>
public sealed class SongSizeVariantsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="SongSizeVariantsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public SongSizeVariantsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（display_order 昇順）。</summary>
    public async Task<IReadOnlyList<SongSizeVariant>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              variant_code  AS VariantCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM song_size_variants
            ORDER BY COALESCE(display_order, 255), variant_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongSizeVariant>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>UPSERT（MastersEditor から利用）。</summary>
    public async Task UpsertAsync(SongSizeVariant kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_size_variants (variant_code, name_ja, name_en, display_order, created_by, updated_by)
            VALUES (@VariantCode, @NameJa, @NameEn, @DisplayOrder, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja = VALUES(name_ja),
              name_en = VALUES(name_en),
              display_order = VALUES(display_order),
              updated_by = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, kind, cancellationToken: ct));
    }

    /// <summary>指定コードのマスタを削除する。</summary>
    public async Task DeleteAsync(string variantCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM song_size_variants WHERE variant_code = @VariantCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { VariantCode = variantCode }, cancellationToken: ct));
    }
}