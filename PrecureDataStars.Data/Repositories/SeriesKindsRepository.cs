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
    /// 追加された <c>credit_attach_to</c> 列も併せて返す。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>種別マスタの一覧。</returns>
    public async Task<IReadOnlyList<SeriesKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code         AS KindCode,
              name_ja           AS NameJa,
              name_en           AS NameEn,
              credit_attach_to  AS CreditAttachTo,
              created_by        AS CreatedBy,
              updated_by        AS UpdatedBy
            FROM series_kinds
            ORDER BY kind_code;
            """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// UPSERT。既存コードがあれば更新、無ければ追加する。
    /// 新カラム <c>credit_attach_to</c> も含めて 1 ステートメントで反映する。
    /// </summary>
    public async Task UpsertAsync(SeriesKind kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO series_kinds
              (kind_code, name_ja, name_en, credit_attach_to, created_by, updated_by)
            VALUES
              (@KindCode, @NameJa, @NameEn, @CreditAttachTo, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja          = VALUES(name_ja),
              name_en          = VALUES(name_en),
              credit_attach_to = VALUES(credit_attach_to),
              updated_by       = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, kind, cancellationToken: ct));
    }

    /// <summary>
    /// 指定コードのマスタを削除する。
    /// series.kind_code から参照されている場合は FK 違反で失敗する。
    /// </summary>
    public async Task DeleteAsync(string kindCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM series_kinds WHERE kind_code = @KindCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { KindCode = kindCode }, cancellationToken: ct));
    }
}