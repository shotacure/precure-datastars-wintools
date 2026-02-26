using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// part_types テーブル（パート種別マスタ）の読み取りリポジトリ。
/// </summary>
public sealed class PartTypesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="PartTypesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public PartTypesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// part_types を全件取得する（display_order 昇順 → part_type 昇順）。
    /// display_order が NULL の場合は 255 として末尾に配置される。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>パート種別マスタの一覧。</returns>
    public async Task<IReadOnlyList<PartType>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              part_type AS PartTypeCode,
              name_ja        AS NameJa,
              name_en        AS NameEn,
              display_order  AS DisplayOrder,
              created_by     AS CreatedBy,
              updated_by     AS UpdatedBy
            FROM part_types
            ORDER BY COALESCE(display_order, 255), part_type;
        """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PartType>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定のパート種別コードで 1 件を取得する。
    /// </summary>
    /// <param name="code">パート種別コード（例: "AVANT"）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>見つかった場合は <see cref="PartType"/>、存在しなければ <c>null</c>。</returns>
    public async Task<PartType?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              part_type AS PartTypeCode,
              name_ja        AS NameJa,
              name_en        AS NameEn,
              display_order  AS DisplayOrder,
              created_by     AS CreatedBy,
              updated_by     AS UpdatedBy
            FROM part_types
            WHERE part_type = @code
            LIMIT 1;
        """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<PartType>(new CommandDefinition(sql, new { code }, cancellationToken: ct));
    }
}
