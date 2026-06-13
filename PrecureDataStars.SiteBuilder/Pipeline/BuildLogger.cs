namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 標準出力ベースの簡易ログ出力。警告は <see cref="WarningCount"/> でカウントしておき、
/// ビルドサマリーで表示する。
/// ページレンダリングは並列実行されるためログ呼び出しもワーカースレッドから届き得るが、
/// <see cref="Console"/> の書き込み自体はスレッドセーフ、<see cref="ProgressReporter.RunSuspended"/>
/// 経由の経路は Reporter 内部のロックで直列化される。警告カウントだけは
/// <see cref="System.Threading.Interlocked"/> でインクリメントして取りこぼしを防ぐ。
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

    /// <summary>累計警告数の実体。並列レンダリング中の Warn 呼び出しでも取りこぼさないよう Interlocked で増やす。</summary>
    private int _warningCount;

    /// <summary>累計警告数（情報・成功は数えない）。</summary>
    public int WarningCount => _warningCount;

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
        System.Threading.Interlocked.Increment(ref _warningCount);
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
