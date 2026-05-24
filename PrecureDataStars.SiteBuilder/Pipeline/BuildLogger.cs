namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 標準出力ベースの簡易ログ出力。SiteBuilder では非同期ジョブ並列化を行わずシリアル実行する前提なので、
/// ロックなどは設けず単純に <see cref="Console.WriteLine"/> を呼ぶ。警告は <see cref="WarningCount"/> で
/// カウントしておき、ビルドサマリーで表示する。
/// <para>
/// 任意で <see cref="ProgressReporter"/> を受け取って連携できる。Reporter が指定されているときは、
/// 各ログ出力を <see cref="ProgressReporter.RunSuspended"/> で囲み、プログレスバー領域を一旦消去 →
/// ログ出力 → バー再描画 の順序で割り込み制御する。これにより末尾固定のプログレスバーと
/// 通常ログが視覚的に競合しない。
/// </para>
/// </summary>
public sealed class BuildLogger
{
    /// <summary>進捗バーとの調停先。null のときは従来どおりの素朴な Console 出力。</summary>
    private readonly ProgressReporter? _reporter;

    /// <summary>累計警告数（情報・成功は数えない）。</summary>
    public int WarningCount { get; private set; }

    public BuildLogger(ProgressReporter? reporter = null)
    {
        _reporter = reporter;
    }

    /// <summary>セクション開始。視認のため空行を挟んでから出力する。</summary>
    public void Section(string title)
    {
        WriteWithReporter(() =>
        {
            Console.WriteLine();
            Console.WriteLine($"== {title} ==");
        });
    }

    /// <summary>進捗的な情報出力。</summary>
    public void Info(string message)
    {
        WriteWithReporter(() => Console.WriteLine($"  {message}"));
    }

    /// <summary>成功扱いの軽い通知。Info とは色や記号で区別したいときに使う想定（現在は同内容）。</summary>
    public void Success(string message)
    {
        WriteWithReporter(() => Console.WriteLine($"  ✓ {message}"));
    }

    /// <summary>警告。<see cref="WarningCount"/> をインクリメントしつつ標準エラー出力に出す。</summary>
    public void Warn(string message)
    {
        WarningCount++;
        WriteWithReporter(() => Console.Error.WriteLine($"  ! {message}"));
    }

    /// <summary>Reporter が紐付いていればバー領域を退避してログ出力、無ければ直接呼ぶ共通ヘルパ。</summary>
    private void WriteWithReporter(Action writeAction)
    {
        if (_reporter is null)
        {
            writeAction();
        }
        else
        {
            _reporter.RunSuspended(writeAction);
        }
    }
}
