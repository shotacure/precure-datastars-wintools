using System.Collections.Concurrent;
using System.Text;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 出力ディレクトリへの書き出しの窓口。ビルドは出力を空にしてから作り直すのではなく、前回の出力を残したまま
/// 「中身が変わるファイルだけ書き、今回書かなかったファイルを最後に消す」。
/// <list type="bullet">
///   <item><description>文字列・バイト列は、同じ場所に同じ中身のファイルがあれば書かない（読んで比べる）。
///     作り置きから写す PNG は、大きさと更新時刻が作り置きと一致すれば写さない（写すときは更新時刻も
///     作り置きにそろえる）。ファイルの作成はウイルススキャン等で 1 件ごとの待ちが大きく、
///     変わらない数千件を書かずに済ませるだけでビルドが大きく縮む。</description></item>
///   <item><description>書いたかどうかに関わらず、今回の出力に属するファイルは全部ここに記録する。全体ビルドの最後に
///     <see cref="PruneOrphans"/> が記録に無いファイル（生成されなくなったページや画像）を消し、空になった
///     ディレクトリも消す。これで出力は「空にしてから作り直した」のと同じ集合になり、<c>--deploy</c> の
///     孤児削除（ローカルに無い S3 オブジェクトの削除）がそのまま効く。</description></item>
/// </list>
/// ページの並列レンダリングから同時に呼ばれる。出力先はページごとに別なので、記録だけを並行辞書で持てばよい。
/// </summary>
public sealed class OutputWriter
{
    /// <summary><see cref="File.WriteAllText(string, string)"/> と同じ UTF-8（BOM なし）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>今回の出力に属するファイル（絶対パス。Windows のパスなので大文字小文字を区別しない）。</summary>
    private readonly ConcurrentDictionary<string, byte> _files = new(StringComparer.OrdinalIgnoreCase);

    private int _written;
    private int _unchanged;

    public OutputWriter(string outputRoot)
    {
        Root = Path.GetFullPath(outputRoot);
    }

    /// <summary>出力ディレクトリの絶対パス。</summary>
    public string Root { get; }

    /// <summary>今回のビルドで中身を書いた（写した）ファイル数。</summary>
    public int WrittenCount => _written;

    /// <summary>同じ中身がすでにあって書かずに済ませたファイル数。</summary>
    public int UnchangedCount => _unchanged;

    /// <summary>UTF-8（BOM なし）の文字列を書く。同じ中身のファイルがあれば書かない。戻り値は書いたかどうか。</summary>
    public bool WriteText(string path, string text) => WriteBytes(path, Utf8NoBom.GetBytes(text));

    /// <summary>バイト列を書く。同じ中身のファイルがあれば書かない。戻り値は書いたかどうか。</summary>
    public bool WriteBytes(string path, byte[] bytes)
    {
        var full = Register(path);
        if (HasSameContent(full, bytes))
        {
            Interlocked.Increment(ref _unchanged);
            return false;
        }
        PathUtil.EnsureParentDirectory(full);
        File.WriteAllBytes(full, bytes);
        Interlocked.Increment(ref _written);
        return true;
    }

    /// <summary>
    /// 作り置きなどのファイルを出力へ写す。大きさと更新時刻が元のファイルと一致していれば写さない。戻り値は写したかどうか。
    /// 写したあとは更新時刻を元のファイルにそろえる（次回この判定で写さずに済ませるため）。
    /// </summary>
    public bool CopyFrom(string sourcePath, string path)
    {
        var full = Register(path);
        var source = new FileInfo(sourcePath);
        var target = new FileInfo(full);
        if (target.Exists && target.Length == source.Length && target.LastWriteTimeUtc == source.LastWriteTimeUtc)
        {
            Interlocked.Increment(ref _unchanged);
            return false;
        }
        PathUtil.EnsureParentDirectory(full);
        File.Copy(sourcePath, full, overwrite: true);
        File.SetLastWriteTimeUtc(full, source.LastWriteTimeUtc);
        Interlocked.Increment(ref _written);
        return true;
    }

    /// <summary>
    /// 自分で書いたファイルを今回の出力として記録する（書き出しの要否は判定しない）。戻り値は正規化した絶対パス。
    /// <see cref="PruneOrphans"/> で消されないために、出力ディレクトリに置くファイルは必ず記録を通す。
    /// </summary>
    public string Register(string path)
    {
        var full = Path.GetFullPath(path);
        _files.TryAdd(full, 0);
        return full;
    }

    /// <summary>そのファイルが今回の出力として記録済みか（前回の出力の残りではないか）。</summary>
    public bool IsTracked(string path) => _files.ContainsKey(Path.GetFullPath(path));

    /// <summary>
    /// 出力ディレクトリにあって今回の出力に属さないファイルを消し、空になったディレクトリも消す。戻り値は消したファイル数。
    /// 全体ビルドの最後に呼ぶ。ピンポイントビルド（<c>--page</c>）では呼ばない（対象外のページを消してしまう）。
    /// </summary>
    public int PruneOrphans()
    {
        if (!Directory.Exists(Root)) return 0;

        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (_files.ContainsKey(Path.GetFullPath(file))) continue;
            File.Delete(file);
            removed++;
        }

        // 深いディレクトリから見て、空になったものを消す（子を消してから親を見る順になる）。
        foreach (var dir in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        return removed;
    }

    /// <summary>
    /// 出力ディレクトリにあるファイル・ディレクトリの名前の大文字小文字を、今回の出力として記録したパスにそろえる。
    /// 戻り値は名前を付け替えたファイル・ディレクトリの数。全体ビルドの最後に <see cref="PruneOrphans"/> の前に呼ぶ。
    /// <list type="bullet">
    ///   <item><description>Windows のファイルシステムは大文字小文字を区別しないので、URL が大文字小文字だけ違う形に変わった
    ///     ページ（人物の表示名義が「Ryan Prajinog」から「RYAN PRAJINOG」に変わったときなど）は、前回のディレクトリ名のまま
    ///     中身だけが書き換わる。S3 のキーとサイトの URL は大文字小文字を区別するので、そのままデプロイするとリンク先が 404 になる。</description></item>
    ///   <item><description>記録したパスと実際の名前が大文字小文字だけ違うものを探し、ルートに近い階層から順に一時名を経由して
    ///     付け替える（大文字小文字だけの改名は直接できない場合があるため）。</description></item>
    /// </list>
    /// </summary>
    public int AlignNameCase()
    {
        if (!Directory.Exists(Root)) return 0;

        // 記録したパス（大文字小文字は記録した形のまま）を、大文字小文字を区別しない引き当て用に持つ。
        var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _files.Keys) tracked[key] = key;

        var mismatched = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            var actual = Path.GetFullPath(file);
            if (tracked.TryGetValue(actual, out var desired) && !string.Equals(actual, desired, StringComparison.Ordinal))
                mismatched.Add(desired);
        }

        int renamed = 0;
        foreach (var desired in mismatched)
        {
            // ルート直下から 1 階層ずつ、実際の名前を引いて記録した名前と比べる（先に付け替えた親の結果を引き継ぐ）。
            var segments = Path.GetRelativePath(Root, desired).Split(Path.DirectorySeparatorChar);
            var current = Root;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isFile = i == segments.Length - 1;
                var want = segments[i];
                var entry = new DirectoryInfo(current).EnumerateFileSystemInfos()
                    .FirstOrDefault(e => string.Equals(e.Name, want, StringComparison.OrdinalIgnoreCase));
                if (entry is null) break;
                var target = Path.Combine(current, want);
                if (!string.Equals(entry.Name, want, StringComparison.Ordinal))
                {
                    var temp = Path.Combine(current, $"~case-{Guid.NewGuid():N}");
                    if (isFile)
                    {
                        File.Move(entry.FullName, temp);
                        File.Move(temp, target);
                    }
                    else
                    {
                        Directory.Move(entry.FullName, temp);
                        Directory.Move(temp, target);
                    }
                    renamed++;
                }
                current = target;
            }
        }
        return renamed;
    }

    /// <summary>同じ場所に同じ中身のファイルがあるか。大きさが違えば読まずに不一致とする。</summary>
    private static bool HasSameContent(string full, byte[] bytes)
    {
        var info = new FileInfo(full);
        if (!info.Exists || info.Length != bytes.Length) return false;
        return File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes);
    }
}
