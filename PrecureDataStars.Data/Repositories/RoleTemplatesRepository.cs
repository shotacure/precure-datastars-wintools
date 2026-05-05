using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// role_templates テーブル（役職テンプレート）の CRUD リポジトリ（v1.2.0 工程 H-10 で導入）。
/// <para>
/// 旧設計の <c>roles.default_format_template</c>（既定）と <c>series_role_format_overrides</c>（オーバーライド）
/// を統合した単一テーブル。 (role_code, series_id) で UNIQUE。
/// </para>
/// <para>
/// 解決ロジック：<see cref="ResolveAsync"/> が「(role_code, series_id) で検索 →
/// 無ければ (role_code, NULL) フォールバック」のロジックを 1 SQL 内で実行する。
/// </para>
/// </summary>
public sealed class RoleTemplatesRepository
{
    private readonly IConnectionFactory _factory;

    public RoleTemplatesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（role_code, series_id 順）。マスタ管理 GUI で一覧表示用。</summary>
    public async Task<IReadOnlyList<RoleTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              template_id     AS TemplateId,
              role_code       AS RoleCode,
              series_id       AS SeriesId,
              format_template AS FormatTemplate,
              notes           AS Notes,
              created_at      AS CreatedAt,
              updated_at      AS UpdatedAt,
              created_by      AS CreatedBy,
              updated_by      AS UpdatedBy
            FROM role_templates
            ORDER BY role_code, IFNULL(series_id, 0);
            """;

        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleTemplate>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定役職の全テンプレ（既定 + シリーズ別）を取得する。マスタ管理 GUI の役職フィルタで使う。</summary>
    public async Task<IReadOnlyList<RoleTemplate>> GetByRoleAsync(string roleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              template_id     AS TemplateId,
              role_code       AS RoleCode,
              series_id       AS SeriesId,
              format_template AS FormatTemplate,
              notes           AS Notes,
              created_at      AS CreatedAt,
              updated_at      AS UpdatedAt,
              created_by      AS CreatedBy,
              updated_by      AS UpdatedBy
            FROM role_templates
            WHERE role_code = @roleCode
            ORDER BY IFNULL(series_id, 0);
            """;
        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleTemplate>(new CommandDefinition(sql, new { roleCode }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キーで 1 件取得。</summary>
    public async Task<RoleTemplate?> GetByIdAsync(int templateId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              template_id     AS TemplateId,
              role_code       AS RoleCode,
              series_id       AS SeriesId,
              format_template AS FormatTemplate,
              notes           AS Notes,
              created_at      AS CreatedAt,
              updated_at      AS UpdatedAt,
              created_by      AS CreatedBy,
              updated_by      AS UpdatedBy
            FROM role_templates
            WHERE template_id = @templateId;
            """;
        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<RoleTemplate>(new CommandDefinition(sql, new { templateId }, cancellationToken: ct));
    }

    /// <summary>
    /// テンプレ解決：(role_code, series_id) で 1 件、無ければ (role_code, NULL) で 1 件、
    /// それも無ければ null を返す。クレジットレンダリング時に呼ぶ専用メソッド。
    /// </summary>
    /// <param name="roleCode">役職コード。</param>
    /// <param name="seriesId">シリーズ ID（NULL なら既定のみを引く）。</param>
    public async Task<RoleTemplate?> ResolveAsync(string roleCode, int? seriesId, CancellationToken ct = default)
    {
        // (role_code, series_id) を優先、無ければ (role_code, NULL) を返す。1 SQL で済ませるために
        // UNION ALL + 「優先順位列」でソート → 先頭 1 件を取る。
        const string sql = """
            SELECT
              template_id     AS TemplateId,
              role_code       AS RoleCode,
              series_id       AS SeriesId,
              format_template AS FormatTemplate,
              notes           AS Notes,
              created_at      AS CreatedAt,
              updated_at      AS UpdatedAt,
              created_by      AS CreatedBy,
              updated_by      AS UpdatedBy,
              priority
            FROM (
                SELECT *, 1 AS priority FROM role_templates
                  WHERE role_code = @roleCode AND series_id = @seriesId
                UNION ALL
                SELECT *, 2 AS priority FROM role_templates
                  WHERE role_code = @roleCode AND series_id IS NULL
            ) t
            ORDER BY priority
            LIMIT 1;
            """;
        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<RoleTemplate>(new CommandDefinition(sql, new { roleCode, seriesId }, cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT：(role_code, series_id) のキーで衝突したら format_template / notes を更新する。
    /// MySQL では UNIQUE KEY (role_code, series_id) があり、series_id NULL も含めて検出する設計のため、
    /// アプリ側で「既存 1 件確認 → INSERT or UPDATE」の 2 段階で実装する（NULL を含む UNIQUE は
    /// MySQL 8.0 で複数行を許容する仕様の都合）。
    /// </summary>
    public async Task UpsertAsync(RoleTemplate t, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 既存検索（series_id が NULL の場合は IS NULL で検索する必要がある）
            const string findSql = """
                SELECT template_id FROM role_templates
                 WHERE role_code = @RoleCode
                   AND ((@SeriesId IS NULL AND series_id IS NULL)
                     OR (@SeriesId IS NOT NULL AND series_id = @SeriesId))
                 LIMIT 1;
                """;
            int? existingId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(findSql, t, transaction: tx, cancellationToken: ct));

            if (existingId.HasValue)
            {
                // UPDATE
                const string upd = """
                    UPDATE role_templates SET
                      format_template = @FormatTemplate,
                      notes           = @Notes,
                      updated_by      = @UpdatedBy
                    WHERE template_id = @ExistingId;
                    """;
                await conn.ExecuteAsync(new CommandDefinition(upd, new
                {
                    t.FormatTemplate,
                    t.Notes,
                    t.UpdatedBy,
                    ExistingId = existingId.Value
                }, transaction: tx, cancellationToken: ct));
                t.TemplateId = existingId.Value;
            }
            else
            {
                // INSERT
                const string ins = """
                    INSERT INTO role_templates
                      (role_code, series_id, format_template, notes, created_by, updated_by)
                    VALUES
                      (@RoleCode, @SeriesId, @FormatTemplate, @Notes, @CreatedBy, @UpdatedBy);
                    SELECT LAST_INSERT_ID();
                    """;
                int newId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(ins, t, transaction: tx, cancellationToken: ct));
                t.TemplateId = newId;
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>主キーで削除。</summary>
    public async Task DeleteAsync(int templateId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM role_templates WHERE template_id = @templateId;";
        await using MySqlConnection conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { templateId }, cancellationToken: ct));
    }
}
