using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// 全リポジトリ共通の基底クラス。接続ファクトリの保持・null 検証と、
/// 「接続を開いて単発クエリを実行して閉じる」定型を protected ヘルパとして提供する。
/// 各ヘルパは <see cref="CommandDefinition"/>（CancellationToken 伝播付き）で 1 コマンドを実行し、
/// 接続はヘルパ内で開閉が完結する。
/// トランザクションや複数コマンドをまたぐ処理・<c>QueryMultiple</c> 等の複雑な経路は、
/// 従来どおり <see cref="Factory"/> から直接接続を取得して書いてよい（ヘルパ利用は強制しない）。
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>接続ファクトリ。単発ヘルパに乗らない処理（トランザクション等）はここから接続を取得する。</summary>
    protected IConnectionFactory Factory { get; }

    protected RepositoryBase(IConnectionFactory factory)
        => Factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>SELECT を実行し、全行をリスト化して返す（0 行なら空リスト）。</summary>
    protected async Task<IReadOnlyList<T>> QueryListAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>SELECT を実行し、単一行（0 行なら <c>default</c>）を返す。2 行以上は例外（Dapper の Single 系検査）。</summary>
    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>INSERT / UPDATE / DELETE 等を実行し、影響行数を返す。</summary>
    protected async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>単一スカラー値を返すクエリ（COUNT / LAST_INSERT_ID 等）を実行する。</summary>
    protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false);
    }
}
