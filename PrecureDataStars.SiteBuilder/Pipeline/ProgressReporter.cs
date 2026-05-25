using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// サイト生成進捗の二段プログレスバー表示器。
/// <para>
/// SiteBuilder のフルビルドはセクション（Generator 単位の処理ブロック）に区切られて順次実行される。
/// 本クラスはセクション一覧と各セクションの予想ページ数を保持し、現在進行中のセクション内進捗と
/// 全セクション通算の進捗をコンソール末尾 2 行に固定表示する。
/// </para>
/// <para>
/// 描画方式：
/// <list type="bullet">
///   <item><description>ANSI escape 対応端末（標準出力がリダイレクトされていない）：
///     最下行 2 行を上書きする形でプログレスバーを描画し、1 秒ごとに自動再描画する。
///     <see cref="BuildLogger"/> 等が通常ログを出力する際は <see cref="RunSuspended"/> 経由で
///     バー領域を一旦消去 → 通常出力 → バー再描画 の順序で割り込み制御する。</description></item>
///   <item><description>非対応環境（リダイレクト先がファイル等）：上書き描画は行わず、セクション切替時のみ
///     1 行追記でセクション開始通知を出す軽量モードに自動フォールバックする。</description></item>
/// </list>
/// </para>
/// <para>
/// 予想ページ数（Expected）は事前に確定できないセクションでは <c>null</c> を許容する。
/// その場合セクション内バーは「?」表記で出し、セクション完了時に実際の Completed 数で確定させる。
/// 全体プログレスバーの母数は「完了済みセクションの Expected 総和 + 進行中＋未開始セクションの Expected 総和
/// （null は 0 扱い）」で算出するため、未確定セクションが残る初期段階は母数が小さめに見えるが、
/// セクションが進むにつれて誤差が解消される。
/// </para>
/// </summary>
public sealed class ProgressReporter : IDisposable
{
    /// <summary>セクション一覧と現在状態へのアクセスを直列化するためのロック。</summary>
    private readonly object _lock = new();

    /// <summary>登録済みセクション一覧（追加順 = 実行順）。</summary>
    private readonly List<SectionState> _sections = new();

    /// <summary>現在進行中のセクションのインデックス（<see cref="_sections"/> 上）。未開始のとき -1。</summary>
    private int _currentIndex = -1;

    /// <summary>ANSI escape による上書き描画が利用可能か。</summary>
    private readonly bool _ansiEnabled;

    /// <summary>定期再描画用タイマ。ANSI 対応時のみ生成。</summary>
    private Timer? _renderTimer;

    /// <summary>現在画面末尾にプログレスバー 2 行が描画済みか。</summary>
    private bool _barDrawn;

    /// <summary><see cref="Finish"/> が呼ばれた後の再描画を抑止するためのフラグ。</summary>
    private bool _finished;

    public ProgressReporter()
    {
        // ANSI 上書き描画は標準出力が端末で、かつ NO_COLOR 環境変数が未設定の場合のみ有効化する。
        // リダイレクトされた標準出力に上書き制御文字を流すとログファイルに制御コードが残ってしまうため避ける。
        _ansiEnabled = !Console.IsOutputRedirected
                       && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        if (_ansiEnabled)
        {
            // Windows のコマンドプロンプト / PowerShell では既定のコンソール出力エンコーディングが
            // システム既定（日本語環境では CP932）になっており、本クラスがバー描画に使う Unicode
            // ブロック文字 U+2588（█）・U+2591（░）は CP932 にマップが無いため "?" に置換されて表示される。
            // OS 側コンソールコードページと .NET 側 Console.OutputEncoding の両方を UTF-8 に揃えるため
            // ここで一度だけ調整を行う。失敗しても描画自体は続行する（最悪バーが "?" のまま表示されるだけで
            // ビルドの本筋には影響しない）。
            TrySetUtf8Console();

            // 1 秒ごとの定期再描画タイマ。PageWritten が高頻度で来てもこのタイマが間引いて描画する。
            _renderTimer = new Timer(_ => TickRender(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// コンソール出力を UTF-8 に揃える。Windows では Win32 API <c>SetConsoleOutputCP(65001)</c> で
    /// OS 側のコンソールコードページを切り替えた上で、.NET 側の <see cref="Console.OutputEncoding"/> も
    /// BOM 無し UTF-8 に設定する。Linux / macOS では .NET ランタイムが既定で UTF-8 を使うため
    /// <see cref="Console.OutputEncoding"/> のみ明示する。例外は握りつぶす（プロセス権限や
    /// 特殊なリダイレクト構成等で API 呼び出しが失敗しても、進捗バー以外の動作には影響させない）。
    /// </summary>
    private static void TrySetUtf8Console()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 65001 = UTF-8 のコードページ番号。OS 側のコンソールコードページを切り替えないと
                // .NET 側だけ UTF-8 にしてもターミナルが受け取った UTF-8 バイト列を CP932 として
                // 解釈してしまい、結局化ける。
                _ = NativeMethods.SetConsoleOutputCP(65001);
            }
            // BOM 無し UTF-8 を指定する。BOM 付きだとログの行頭に余分なバイトが乗ってリダイレクト時に
            // 解析するツールが混乱することがあるため。
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch
        {
            // 設定失敗は無視。本クラスは進捗表示の補助役であり、ここで例外を伝播させるとビルド全体が
            // 落ちてしまうため、化けたままでも処理を続行させる方が望ましい。
        }
    }

    /// <summary>セクション一覧を登録する。順序＝実行順。Expected は null（未確定）も可。</summary>
    public void RegisterSections(IEnumerable<(string Id, string Label, int? Expected)> sections)
    {
        lock (_lock)
        {
            _sections.Clear();
            int idx = 0;
            foreach (var (id, label, exp) in sections)
            {
                idx++;
                _sections.Add(new SectionState
                {
                    Index = idx,
                    Id = id,
                    Label = label,
                    Expected = exp,
                    Completed = 0,
                    IsActive = false,
                    IsDone = false
                });
            }
        }
    }

    /// <summary>セクション開始を通知する。<paramref name="id"/> は <see cref="RegisterSections"/> で登録した ID と一致させる。</summary>
    /// <param name="id">セクション ID。</param>
    /// <param name="expectedOverride">登録時に未確定（null）だった場合に、開始時点で算出できた予想ページ数で上書きする。</param>
    public void BeginSection(string id, int? expectedOverride = null)
    {
        lock (_lock)
        {
            int i = _sections.FindIndex(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            if (i < 0) return;

            ClearBar();
            _currentIndex = i;
            _sections[i].IsActive = true;
            _sections[i].Sw.Restart();
            if (expectedOverride.HasValue) _sections[i].Expected = expectedOverride.Value;

            if (_ansiEnabled)
            {
                DrawBar();
            }
            else
            {
                // ANSI 非対応時：セクション開始のみを 1 行追記して進捗を伝える。
                Console.WriteLine($"[{_sections[i].Index}/{_sections.Count}] {_sections[i].Label} 開始");
            }
        }
    }

    /// <summary>現在セクションの完了を通知する。Expected が null のままなら Completed で確定する。</summary>
    public void EndSection()
    {
        lock (_lock)
        {
            if (_currentIndex < 0 || _currentIndex >= _sections.Count) return;
            var s = _sections[_currentIndex];
            s.IsActive = false;
            s.IsDone = true;
            s.Sw.Stop();
            // 事前に予想件数が決まっていなかったセクションは、ここで実数を母数として確定する。
            // 以後の全体プログレスバー算出で母数の一部として加算される。
            if (!s.Expected.HasValue) s.Expected = s.Completed;

            ClearBar();
            if (_ansiEnabled) DrawBar();
        }
    }

    /// <summary>現在セクション内でページが 1 件書き出されたことを通知する。バー再描画はタイマに任せる。</summary>
    public void PageWritten()
    {
        lock (_lock)
        {
            if (_currentIndex < 0 || _currentIndex >= _sections.Count) return;
            _sections[_currentIndex].Completed++;
        }
        // 高頻度呼び出しを想定し、ここでは描画しない（定期タイマで間引いて描画）。
    }

    /// <summary>
    /// バー描画を一時停止して通常ログ出力を行う。バー領域は一旦消去され、<paramref name="action"/> 完了後に再描画される。
    /// <see cref="BuildLogger"/> が <see cref="Console.WriteLine(string)"/> を呼ぶ前後で本メソッドを使い、
    /// 通常ログとプログレスバーが視覚的に競合しないよう調停する。
    /// </summary>
    public void RunSuspended(Action action)
    {
        if (action is null) return;

        if (!_ansiEnabled)
        {
            // ANSI 非対応時はバー描画自体が無いので、単純にロックを取らず通常実行で良い。
            action();
            return;
        }

        lock (_lock)
        {
            ClearBar();
            try
            {
                action();
            }
            finally
            {
                DrawBar();
            }
        }
    }

    /// <summary>パイプライン終了時に呼ぶ。最終的なバー描画を消去し、定期タイマを止める。</summary>
    public void Finish()
    {
        lock (_lock)
        {
            _finished = true;
            ClearBar();
        }

        // タイマの破棄はロック外で行う（タイマコールバックが _lock を取りに来るデッドロック回避）。
        var t = _renderTimer;
        _renderTimer = null;
        t?.Dispose();
    }

    public void Dispose() => Finish();

    /// <summary>1 秒ごとの定期再描画コールバック。</summary>
    private void TickRender()
    {
        // ロックがすでに取れている場合（メイン処理中）はスキップして次のチックを待つ。
        // これにより RunSuspended 中の競合や、長時間ロック保持時にタイマが待たされ続けるのを防ぐ。
        if (!Monitor.TryEnter(_lock)) return;
        try
        {
            if (_finished) return;
            if (_currentIndex < 0) return;
            ClearBar();
            DrawBar();
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>
    /// 最下行 2 行のバー領域を消去する。呼び出し時点でカーソルは「バー 1 行目の先頭」にいる前提
    /// （<see cref="DrawBar"/> 完了後と同じ位置）。消去後もカーソル位置は同じ場所に戻す。
    /// </summary>
    private void ClearBar()
    {
        if (!_ansiEnabled || !_barDrawn) return;

        // 1 行目をクリア → 改行で 2 行目に移動 → 2 行目をクリア → 1 行上に戻る → 行頭へ。
        Console.Out.Write("\x1b[2K\n\x1b[2K\x1b[1A\r");
        Console.Out.Flush();
        _barDrawn = false;
    }

    /// <summary>
    /// 現在状態に基づいてバー 2 行を描画する。呼び出し時点でカーソルは「これから描画する 1 行目の先頭」にいる前提。
    /// 描画後もカーソルは 1 行目の先頭に戻す（次回の <see cref="ClearBar"/> がそれを前提に動く）。
    /// </summary>
    private void DrawBar()
    {
        if (!_ansiEnabled) return;
        if (_currentIndex < 0 || _sections.Count == 0) return;

        var current = _sections[_currentIndex];
        string line1 = FormatSectionLine(current);
        string line2 = FormatOverallLine();

        Console.Out.Write(line1);
        Console.Out.Write("\n");
        Console.Out.Write(line2);
        Console.Out.Write("\x1b[1A\r");
        Console.Out.Flush();
        _barDrawn = true;
    }

    /// <summary>セクション内プログレスバー（1 行目）を組み立てる。</summary>
    private string FormatSectionLine(SectionState s)
    {
        const int barWidth = 24;
        int? exp = s.Expected;
        double frac = exp.HasValue && exp.Value > 0
            ? Math.Min(1.0, (double)s.Completed / exp.Value)
            : 0.0;

        string bar = BuildBar(barWidth, frac);
        string expStr = exp.HasValue ? exp.Value.ToString("N0", CultureInfo.InvariantCulture) : "?";
        string completedStr = s.Completed.ToString("N0", CultureInfo.InvariantCulture);
        string pct = exp.HasValue
            ? (frac * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "  -  ";

        // 「[12/22] 楽曲          : [██████░░░] 71.2%  (532 / 747 ページ)」
        // 行末まで消去する \x1b[K を最後に入れて、前回より短い文字列を描いたときの残骸を消す。
        return $"\x1b[2K[{s.Index,2}/{_sections.Count}] {PadDisplay(s.Label, 14)} : {bar} {pct,6}  ({completedStr} / {expStr} ページ)\x1b[K";
    }

    /// <summary>全体プログレスバー（2 行目）を組み立てる。</summary>
    private string FormatOverallLine()
    {
        const int barWidth = 24;
        int totalCompleted = 0;
        int totalExpected = 0;
        int doneCount = 0;
        foreach (var s in _sections)
        {
            totalCompleted += s.Completed;
            totalExpected += s.Expected ?? 0;
            if (s.IsDone) doneCount++;
        }

        // 実生成数が予想合計を超えたら、その時点で母数を実生成数に引き上げる（バーが 100% を超えないように）。
        if (totalCompleted > totalExpected) totalExpected = totalCompleted;

        double frac = totalExpected > 0
            ? Math.Min(1.0, (double)totalCompleted / totalExpected)
            : 0.0;

        string bar = BuildBar(barWidth, frac);
        string pct = (frac * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        string completedStr = totalCompleted.ToString("N0", CultureInfo.InvariantCulture);
        string expectedStr = totalExpected.ToString("N0", CultureInfo.InvariantCulture);

        return $"\x1b[2K        {PadDisplay("全体", 14)} : {bar} {pct,6}  ({completedStr} / {expectedStr} ページ, {doneCount}/{_sections.Count} セクション完了)\x1b[K";
    }

    /// <summary>進捗率からバー文字列を組み立てる。</summary>
    private static string BuildBar(int width, double frac)
    {
        int filled = (int)Math.Round(width * frac);
        if (filled < 0) filled = 0;
        if (filled > width) filled = width;
        return "[" + new string('█', filled) + new string('░', width - filled) + "]";
    }

    /// <summary>
    /// 全角・半角混在のラベルを表示幅で揃えるためのパディング。
    /// 端末上の表示幅を厳密に算出するのは複雑なので、半角=1・それ以外=2 の簡易換算で見積もる。
    /// 多くの日本語ラベルで揃った見た目になる。
    /// </summary>
    private static string PadDisplay(string s, int targetDisplayWidth)
    {
        int width = 0;
        foreach (var ch in s)
        {
            width += (ch < 0x80) ? 1 : 2;
        }
        if (width >= targetDisplayWidth) return s;
        return s + new string(' ', targetDisplayWidth - width);
    }

    /// <summary>セクション 1 つ分の動的状態。</summary>
    private sealed class SectionState
    {
        public int Index;
        public required string Id { get; set; }
        public required string Label { get; set; }
        public int? Expected;
        public int Completed;
        public bool IsActive;
        public bool IsDone;
        /// <summary>セクション内の経過時間計測。<see cref="BeginSection"/> で Restart、<see cref="EndSection"/> で Stop する。</summary>
        public Stopwatch Sw { get; } = new Stopwatch();
    }

    /// <summary>セクション別所要時間の Build Summary 出力用スナップショット。 (Id, Label, Completed, ElapsedSeconds) を完了順に返す（IsDone=true の行のみ）。</summary>
    public IReadOnlyList<(string Id, string Label, int Completed, double ElapsedSeconds)> GetSectionTimings()
    {
        lock (_lock)
        {
            return _sections
                .Where(s => s.IsDone)
                .Select(s => (s.Id, s.Label, s.Completed, s.Sw.Elapsed.TotalSeconds))
                .ToList();
        }
    }

    /// <summary>
    /// Win32 API への P/Invoke 宣言。Windows のコンソールコードページを UTF-8（65001）に
    /// 切り替えるために <c>SetConsoleOutputCP</c> を使う。
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// コンソール出力に使うコードページを設定する。<c>true</c> を返したとき成功。
        /// 失敗時は <see cref="Marshal.GetLastWin32Error"/> でエラーコードが取得できるが、本クラスでは
        /// 戻り値を確認せずに握りつぶす（化けるだけで動作には影響しないため）。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleOutputCP(uint wCodePageID);
    }
}
