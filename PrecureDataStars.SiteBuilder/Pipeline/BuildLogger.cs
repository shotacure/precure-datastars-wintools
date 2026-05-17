namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 標準出力ベースの簡易ログ出力。
/// <para>
/// SiteBuilder では非同期ジョブ並列化を行わずシリアル実行する前提なので、
/// ロックなどは設けず単純に <see cref="Console.WriteLine"/> を呼ぶ。
/// 警告は <see cref="WarningCount"/> でカウントしておき、ビルドサマリーで表示する。
/// </para>
/// </summary>
public sealed class BuildLogger
{
    /// <summary>累計警告数（情報・成功は数えない）。</summary>
    public int WarningCount { get; private set; }

    /// <summary>セクション開始。視認のため空行を挟んでから出力する。</summary>
    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }

    /// <summary>進捗的な情報出力。</summary>
    public void Info(string message)
    {
        Console.WriteLine($"  {message}");
    }

    /// <summary>成功扱いの軽い通知。Info とは色や記号で区別したいときに使う想定（現在は同内容）。</summary>
    public void Success(string message)
    {
        Console.WriteLine($"  ✓ {message}");
    }

    /// <summary>警告。<see cref="WarningCount"/> をインクリメントしつつ標準エラー出力に出す。</summary>
    public void Warn(string message)
    {
        WarningCount++;
        Console.Error.WriteLine($"  ! {message}");
    }
}