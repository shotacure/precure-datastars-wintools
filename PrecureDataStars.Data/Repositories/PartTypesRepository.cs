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
    /// v1.2.0 で追加された <c>default_credit_kind</c> 列も併せて返す。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>パート種別マスタの一覧。</returns>
    public async Task<IReadOnlyList<PartType>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              part_type            AS PartTypeCode,
              name_ja              AS NameJa,
              name_en              AS NameEn,
              display_order        AS DisplayOrder,
              default_credit_kind  AS DefaultCreditKind,
              created_by           AS CreatedBy,
              updated_by           AS UpdatedBy
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
              part_type            AS PartTypeCode,
              name_ja              AS NameJa,
              name_en              AS NameEn,
              display_order        AS DisplayOrder,
              default_credit_kind  AS DefaultCreditKind,
              created_by           AS CreatedBy,
              updated_by           AS UpdatedBy
            FROM part_types
            WHERE part_type = @code
            LIMIT 1;
        """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<PartType>(new CommandDefinition(sql, new { code }, cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT（v1.2.0 追加）。既存コードがあれば更新、無ければ追加する。
    /// 新カラム <c>default_credit_kind</c> も含めて 1 ステートメントで反映する。
    /// </summary>
    public async Task UpsertAsync(PartType pt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO part_types
              (part_type, name_ja, name_en, display_order, default_credit_kind, created_by, updated_by)
            VALUES
              (@PartTypeCode, @NameJa, @NameEn, @DisplayOrder, @DefaultCreditKind, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja              = VALUES(name_ja),
              name_en              = VALUES(name_en),
              display_order        = VALUES(display_order),
              default_credit_kind  = VALUES(default_credit_kind),
              updated_by           = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, pt, cancellationToken: ct));
    }

    /// <summary>
    /// 指定コードのマスタを削除する（v1.2.0 追加）。
    /// episode_parts.part_type / credits.part_type から参照されている場合は FK 違反で失敗する。
    /// </summary>
    public async Task DeleteAsync(string partTypeCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM part_types WHERE part_type = @PartTypeCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PartTypeCode = partTypeCode }, cancellationToken: ct));
    }
}
