namespace PrecureDataStars.Data.Db;

/// <summary>
/// MySQL への接続設定を保持する不変オブジェクト。
/// <para>
/// アプリケーション起動時に App.config 等から接続文字列を読み込み、
/// 本クラスのインスタンスを生成して <see cref="MySqlConnectionFactory"/> に渡す。
/// </para>
/// </summary>
public sealed class DbConfig
{
    /// <summary>MySQL への接続文字列（MySqlConnector 形式）。</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// <see cref="DbConfig"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="connectionString">MySQL 接続文字列。空白のみ・NULL の場合は例外を送出する。</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> が空白または NULL の場合。</exception>
    public DbConfig(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        ConnectionString = connectionString;
    }
}
