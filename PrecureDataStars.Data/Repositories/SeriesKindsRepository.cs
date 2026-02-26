using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using System.Data;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_kinds テーブル（シリーズ種別マスタ）の読み取り専用リポジトリ。
/// </summary>
public sealed class SeriesKindsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="SeriesKindsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// series_kinds を全件取得する（kind_code 昇順）。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>種別マスタの一覧。</returns>
    public async Task<IReadOnlyList<SeriesKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code AS KindCode,
              name_ja   AS NameJa,
              name_en   AS NameEn
            FROM series_kinds
            ORDER BY kind_code;
            """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }
}
