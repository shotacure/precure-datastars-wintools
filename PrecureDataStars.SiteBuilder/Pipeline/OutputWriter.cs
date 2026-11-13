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
/// <para>
/// 書いたファイルの中身の印（MD5）は、書く時に手元のバイト列から取って控え、ビルドの終わりに台帳
/// （<see cref="LedgerPathFor"/>。出力の隣のファイル）へ残す。sitemap の更新日の判定とデプロイの差分の判定は
/// この印を使い、出力のファイルを読み返さない。次のビルドは台帳を読み、ファイルの大きさと更新時刻が台帳と同じなら
/// 中身も台帳の印のとおりとみなして、既存のファイルを読まずに「変わらない」を決める。大きさか更新時刻が違う
/// ファイル（台帳の外で書き換えられたもの）は、台帳を信じずに読んで確かめる。
/// </para>
/// <para>
/// 差分だけを出すビルド（<c>--diff</c>）では、出力にも台帳にも書き込まない。増える・書き換わるファイルの中身は
/// 差分ディレクトリ（<see cref="DiffDirectoryFor"/>。出力の隣）へ同じ相対パスで置き、消えるファイルとあわせて
/// <see cref="Changes"/> に控える。今の出力と 1 ファイルずつ見比べられる。
/// </para>
/// </summary>
public sealed class OutputWriter
{
    /// <summary><see cref="File.WriteAllText(string, string)"/> と同じ UTF-8（BOM なし）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 今回の出力に属するファイル（絶対パス。Windows のパスなので大文字小文字を区別しない）と、その中身の印。
    /// 印が null のファイルは <see cref="Register"/> で記録しただけのもので、要るときに読んで求める。
    /// </summary>
    private readonly ConcurrentDictionary<string, OutputFileStamp?> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>前回までのビルドが残した台帳（絶対パス → 中身の印）。</summary>
    private readonly IReadOnlyDictionary<string, OutputFileStamp> _previous;

    private int _written;
    private int _unchanged;

    /// <summary>差分だけを出すビルドで、増える・書き換わるファイルの中身を置くディレクトリ。ふつうのビルドでは null。</summary>
    private readonly string? _diffDirectory;

    /// <summary>差分だけを出すビルドで控えた、今の出力との違い。</summary>
    private readonly ConcurrentBag<OutputChange> _changes = new();

    /// <param name="outputRoot">出力ディレクトリ。</param>
    /// <param name="diffOnly">出力に書き込まず、今の出力との差分だけを控えるか（<c>--diff</c>）。</param>
    public OutputWriter(string outputRoot, bool diffOnly = false)
    {
        Root = Path.GetFullPath(outputRoot);
        _previous = LoadLedger(Root);
        if (diffOnly)
        {
            // 前回の差分は残さない（今回の差分と混ざらないように）。
            _diffDirectory = DiffDirectoryFor(Root);
            if (Directory.Exists(_diffDirectory)) Directory.Delete(_diffDirectory, recursive: true);
        }
    }

    /// <summary>出力に書き込まず、今の出力との差分だけを控えるビルドか。</summary>
    public bool DiffOnly => _diffDirectory is not null;

    /// <summary>
    /// 出力ディレクトリに対応する差分ディレクトリのパス（出力の兄弟）。差分だけを出すビルドが、増える・書き換わる
    /// ファイルの中身を出力と同じ相対パスで置く。
    /// </summary>
    public static string DiffDirectoryFor(string outputDirectory)
        => Path.GetFullPath(outputDirectory).TrimEnd('/', '\\') + ".diff";

    /// <summary>差分だけを出すビルドで控えた、今の出力との違い（相対パスの順）。ふつうのビルドでは空。</summary>
    public IReadOnlyList<OutputChange> Changes
        => _changes.OrderBy(c => c.RelativePath, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 差分だけを出すビルドで、増える・書き換わるファイル 1 つを控え、その中身を差分ディレクトリへ置く。
    /// 出力には書かない。印の更新時刻は 0（台帳には残さないので使われない）。
    /// </summary>
    private void RecordDiff(string full, byte[] bytes, string md5, bool exists)
    {
        var relative = Path.GetRelativePath(Root, full).Replace('\\', '/');
        var target = Path.Combine(_diffDirectory!, relative.Replace('/', Path.DirectorySeparatorChar));
        PathUtil.EnsureParentDirectory(target);
        File.WriteAllBytes(target, bytes);
        _changes.Add(new OutputChange(relative, exists ? OutputChangeKind.Changed : OutputChangeKind.Added));
        _files[full] = new OutputFileStamp(md5, bytes.Length, 0);
        Interlocked.Increment(ref _written);
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
        var full = Path.GetFullPath(path);
        string md5 = Md5Hex(bytes);
        var info = new FileInfo(full);
        if (info.Exists && info.Length == bytes.Length)
        {
            // 台帳どおりのファイル（大きさと更新時刻が同じ）は印どうしを比べ、そうでなければ読んで比べる。
            long mtime = info.LastWriteTimeUtc.Ticks;
            bool same = _previous.TryGetValue(full, out var prev) && prev.Length == info.Length && prev.MtimeTicks == mtime
                ? string.Equals(prev.Md5, md5, StringComparison.Ordinal)
                : File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes);
            if (same)
            {
                _files[full] = new OutputFileStamp(md5, info.Length, mtime);
                Interlocked.Increment(ref _unchanged);
                return false;
            }
        }
        if (DiffOnly)
        {
            RecordDiff(full, bytes, md5, info.Exists);
            return true;
        }
        PathUtil.EnsureParentDirectory(full);
        File.WriteAllBytes(full, bytes);
        _files[full] = new OutputFileStamp(md5, bytes.Length, File.GetLastWriteTimeUtc(full).Ticks);
        Interlocked.Increment(ref _written);
        return true;
    }

    /// <summary>
    /// 作り置きなどのファイルを出力へ写す。大きさと更新時刻が元のファイルと一致していれば写さない。戻り値は写したかどうか。
    /// 写したあとは更新時刻を元のファイルにそろえる（次回この判定で写さずに済ませるため）。
    /// </summary>
    public bool CopyFrom(string sourcePath, string path)
    {
        var full = Path.GetFullPath(path);
        var source = new FileInfo(sourcePath);
        var target = new FileInfo(full);
        long mtime = source.LastWriteTimeUtc.Ticks;
        if (target.Exists && target.Length == source.Length && target.LastWriteTimeUtc.Ticks == mtime)
        {
            // 写さずに済ませるときの印は台帳から引く。台帳に無いか、台帳と違うファイルなら読んで求める。
            string known = _previous.TryGetValue(full, out var prev) && prev.Length == target.Length && prev.MtimeTicks == mtime
                ? prev.Md5
                : Md5Hex(File.ReadAllBytes(full));
            _files[full] = new OutputFileStamp(known, target.Length, mtime);
            Interlocked.Increment(ref _unchanged);
            return false;
        }
        // 元のファイルを 1 度だけ読み、印を取ってから書く（写した後に読み返さない）。
        var bytes = File.ReadAllBytes(sourcePath);
        if (DiffOnly)
        {
            // 更新時刻だけが違って中身が同じファイルは、差分に数えない（ふつうのビルドは写し直すが、中身は変わらない）。
            string md5 = Md5Hex(bytes);
            if (target.Exists && target.Length == bytes.Length)
            {
                string current = _previous.TryGetValue(full, out var known) && known.Length == target.Length && known.MtimeTicks == target.LastWriteTimeUtc.Ticks
                    ? known.Md5
                    : Md5Hex(File.ReadAllBytes(full));
                if (string.Equals(current, md5, StringComparison.Ordinal))
                {
                    _files[full] = new OutputFileStamp(md5, target.Length, target.LastWriteTimeUtc.Ticks);
                    Interlocked.Increment(ref _unchanged);
                    return false;
                }
            }
            RecordDiff(full, bytes, md5, target.Exists);
            return true;
        }
        PathUtil.EnsureParentDirectory(full);
        File.WriteAllBytes(full, bytes);
        File.SetLastWriteTimeUtc(full, source.LastWriteTimeUtc);
        _files[full] = new OutputFileStamp(Md5Hex(bytes), bytes.Length, mtime);
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
        _files.TryAdd(full, null);
        return full;
    }

    /// <summary>
    /// 今回の出力として記録したファイルの中身の印（MD5。小文字の 16 進）。記録していないファイルは null。
    /// <see cref="Register"/> で記録しただけのファイルは、ここで読んで求める。
    /// </summary>
    public string? Md5Of(string path)
    {
        var full = Path.GetFullPath(path);
        if (!_files.TryGetValue(full, out var stamp)) return null;
        if (stamp is not null) return stamp.Md5;
        var info = new FileInfo(full);
        if (!info.Exists) return null;
        var read = new OutputFileStamp(Md5Hex(File.ReadAllBytes(full)), info.Length, info.LastWriteTimeUtc.Ticks);
        _files[full] = read;
        return read.Md5;
    }

    /// <summary>
    /// 出力ディレクトリに対応する台帳のパス（出力の兄弟。sitemap の lastmod の記録と同じ置き方）。
    /// 出力ディレクトリの中には置かない（S3 へ上がらないように）。
    /// </summary>
    public static string LedgerPathFor(string outputDirectory)
        => Path.GetFullPath(outputDirectory).TrimEnd('/', '\\') + ".files.json";

    /// <summary>
    /// 台帳を読む（絶対パス → 中身の印）。無いか読めなければ空（すべてのファイルを読んで確かめることになるだけで、害は無い）。
    /// </summary>
    public static IReadOnlyDictionary<string, OutputFileStamp> LoadLedger(string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        var result = new Dictionary<string, OutputFileStamp>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = LedgerPathFor(root);
            if (!File.Exists(path)) return result;
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, OutputFileStamp>>(File.ReadAllBytes(path));
            if (loaded is null) return result;
            foreach (var (relative, stamp) in loaded)
            {
                if (stamp is null || string.IsNullOrEmpty(stamp.Md5)) continue;
                result[Path.GetFullPath(Path.Combine(root, relative))] = stamp;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>
    /// 今回の出力の印を台帳へ残す。全体ビルドでは今回の出力に属するファイルだけを残す（<see cref="PruneOrphans"/> の後に呼ぶ）。
    /// ピンポイントビルド（<c>--page</c>）では、前回までの台帳に今回書いた分を重ねる（<paramref name="keepPrevious"/>）。
    /// 一時ファイルに書いてから置き換えるので、途中で止まっても台帳は壊れない。
    /// 差分だけを出すビルドでは何もしない（出力を書いていないので、台帳は前回のまま）。
    /// </summary>
    public void SaveLedger(bool keepPrevious)
    {
        if (DiffOnly) return;
        var entries = new SortedDictionary<string, OutputFileStamp>(StringComparer.Ordinal);
        string Relative(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');
        if (keepPrevious)
            foreach (var (full, stamp) in _previous)
                entries[Relative(full)] = stamp;
        foreach (var (full, stamp) in _files)
        {
            // 印を取っていないファイルは台帳に載せない（次のビルドで読んで確かめる）。
            if (stamp is null) entries.Remove(Relative(full));
            else entries[Relative(full)] = stamp;
        }

        var path = LedgerPathFor(Root);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entries));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>バイト列の MD5（小文字の 16 進。S3 の ETag と比べる形）。</summary>
    private static string Md5Hex(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// 今回の出力として記録したファイルのうち、<paramref name="directory"/> の下にあって拡張子が <paramref name="extension"/>
    /// （<c>.png</c> の形）のものの絶対パス。出力のディレクトリを走査しないので、差分だけを出すビルドでも
    /// （ファイルを書いていなくても）今回の出力の顔ぶれが分かる。
    /// </summary>
    public IReadOnlyList<string> TrackedFilesUnder(string directory, string extension)
    {
        var prefix = Path.GetFullPath(directory).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        return _files.Keys
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>そのファイルが今回の出力として記録済みか（前回の出力の残りではないか）。</summary>
    public bool IsTracked(string path) => _files.ContainsKey(Path.GetFullPath(path));

    /// <summary>
    /// 出力ディレクトリにあって今回の出力に属さないファイルを消し、空になったディレクトリも消す。戻り値は消したファイル数。
    /// 全体ビルドの最後に呼ぶ。ピンポイントビルド（<c>--page</c>）では呼ばない（対象外のページを消してしまう）。
    /// 差分だけを出すビルドでは消さず、消えるファイルとして控えて数だけ返す。
    /// </summary>
    public int PruneOrphans()
    {
        if (!Directory.Exists(Root)) return 0;

        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (_files.ContainsKey(Path.GetFullPath(file))) continue;
            if (DiffOnly) _changes.Add(new OutputChange(Path.GetRelativePath(Root, file).Replace('\\', '/'), OutputChangeKind.Removed));
            else File.Delete(file);
            removed++;
        }
        if (DiffOnly) return removed;

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
    /// 差分だけを出すビルドでは何もしない。
    /// </summary>
    public int AlignNameCase()
    {
        if (DiffOnly || !Directory.Exists(Root)) return 0;

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

}

/// <summary>差分だけを出すビルドで控える、今の出力との違いの種類。</summary>
public enum OutputChangeKind
{
    /// <summary>今の出力に無く、増えるファイル。</summary>
    Added,
    /// <summary>今の出力にあり、中身が書き換わるファイル。</summary>
    Changed,
    /// <summary>今の出力にあり、今回の出力に属さないので消えるファイル。</summary>
    Removed,
}

/// <summary>差分だけを出すビルドで控える、今の出力との違い 1 件。</summary>
/// <param name="RelativePath">出力ディレクトリからの相対パス（区切りは <c>/</c>）。</param>
/// <param name="Kind">違いの種類。</param>
public sealed record OutputChange(string RelativePath, OutputChangeKind Kind);

/// <summary>出力のファイル 1 つの中身の印。台帳（<see cref="OutputWriter.LedgerPathFor"/>）に残す。</summary>
/// <param name="Md5">中身の MD5（小文字の 16 進）。</param>
/// <param name="Length">ファイルの大きさ（バイト）。</param>
/// <param name="MtimeTicks">ファイルの更新時刻（UTC の Ticks）。</param>
public sealed record OutputFileStamp(
    [property: System.Text.Json.Serialization.JsonPropertyName("h")] string Md5,
    [property: System.Text.Json.Serialization.JsonPropertyName("l")] long Length,
    [property: System.Text.Json.Serialization.JsonPropertyName("t")] long MtimeTicks);
