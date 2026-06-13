namespace PrecureDataStars.SiteBuilder.Configuration;

/// <summary>
/// デプロイ実行時の意図を表すフラグ群。コマンドライン引数（<c>--deploy</c> / <c>--dry-run</c> /
/// <c>--yes</c>）に由来し、AWS ターゲット設定（バケット・Distribution 等）とは分けて保持する。
/// AWS ターゲット側は <see cref="BuildConfig"/> が App.config から読み出す。
/// </summary>
/// <param name="Requested">本番ビルド後に S3 同期＋CloudFront invalidation を実行するか（<c>--deploy</c>）。</param>
/// <param name="DryRun">差分の計画だけ表示し、S3 / CloudFront を一切変更しないか（<c>--dry-run</c>）。</param>
/// <param name="SkipConfirm">破壊的操作（削除）前の対話確認をスキップするか（<c>--yes</c>）。</param>
public sealed record DeployRuntimeOptions(bool Requested, bool DryRun, bool SkipConfirm)
{
    /// <summary>デプロイ指定なし（既定）。</summary>
    public static DeployRuntimeOptions None { get; } = new(Requested: false, DryRun: false, SkipConfirm: false);
}
