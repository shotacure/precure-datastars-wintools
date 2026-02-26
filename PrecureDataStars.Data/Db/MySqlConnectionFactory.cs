using MySqlConnector;

namespace PrecureDataStars.Data.Db;

/// <summary>
/// <see cref="MySqlConnection"/> の生成を抽象化するインターフェース。
/// <para>
/// リポジトリ層は本インターフェースを介して接続を取得するため、
/// テスト時にモック差し替えが可能になる。
/// </para>
/// </summary>
public interface IConnectionFactory
{
    /// <summary>
    /// 未オープンの <see cref="MySqlConnection"/> を返す。
    /// 呼び出し側で <c>Open</c> / <c>OpenAsync</c> を実行すること。
    /// </summary>
    MySqlConnection Create();

    /// <summary>
    /// オープン済みの <see cref="MySqlConnection"/> を返す。
    /// 呼び出し側で <c>using</c> による破棄を行うこと。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>オープン済みの接続。</returns>
    Task<MySqlConnection> CreateOpenedAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="IConnectionFactory"/> の MySQL 実装。
/// <see cref="DbConfig"/> から接続文字列を受け取り、<see cref="MySqlConnection"/> を生成する。
/// </summary>
public sealed class MySqlConnectionFactory : IConnectionFactory
{
    private readonly DbConfig _config;

    /// <summary>
    /// <see cref="MySqlConnectionFactory"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="config">接続設定。</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> が NULL の場合。</exception>
    public MySqlConnectionFactory(DbConfig config)
        => _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <inheritdoc />
    public MySqlConnection Create()
        => new MySqlConnection(_config.ConnectionString);

    /// <inheritdoc />
    public async Task<MySqlConnection> CreateOpenedAsync(CancellationToken ct = default)
    {
        // 接続文字列からインスタンス生成 → Open まで行い、呼び出し元に using で管理させる
        var conn = new MySqlConnection(_config.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }
}
