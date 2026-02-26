using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Data;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_relation_kinds テーブル（シリーズ親子関係種別マスタ）の読み取り専用リポジトリ。
/// </summary>
public sealed class SeriesRelationKindsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="SeriesRelationKindsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesRelationKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// series_relation_kinds を全件取得する（relation_code 昇順）。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>関係種別マスタの一覧。</returns>
    public async Task<IReadOnlyList<SeriesRelationKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              relation_code AS RelationCode,
              name_ja       AS NameJa,
              name_en       AS NameEn
            FROM series_relation_kinds
            ORDER BY relation_code;
            """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesRelationKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }
}
