using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// sitemap.xml の <c>lastmod</c> を「ページ内容が実際に変わったビルドの時刻」に揃えるための
/// 永続マニフェスト。URL → (内容ハッシュ / lastmod) を JSON で保持し、ビルドごとに各ページの
/// 出力ファイルのハッシュを前回ビルド時と突き合わせる。ハッシュが一致するページは前回の lastmod を
/// 据え置き、変化した／新規のページだけ当該ビルドの時刻へ進める。
/// これにより「毎ビルドで全 URL の lastmod が更新される（実際は数ファイルしか変わっていないのに）」
/// という状態を避け、Google に対して信頼できる再クロール優先度シグナルを返す。
/// マニフェストは出力ディレクトリの兄弟ファイル（出力ツリー外＝デプロイ対象外）に置き、出力先
/// （本番／テスト）ごとに独立させる。初回（マニフェスト無し）は全ページが新規扱い＝当該ビルド時刻に
/// なり、以降のビルドで収束する。ハッシュは MD5 で、中身の変化ではない部分（毎週変わる部分、CSS・JS・画像の指し先）を伏せて取る
/// （<see cref="StableContentHash"/>）。伏せ方の版を控えに残し、版の違う初回は一つ前の版のしかたで比べて印だけを取り直す。
/// </summary>
public sealed class SitemapLastmodManifest
{
    private readonly string _manifestPath;

    public SitemapLastmodManifest(string manifestPath) => _manifestPath = manifestPath;

    /// <summary>
    /// 出力ディレクトリからマニフェストの保存パス（兄弟ファイル）を導出する。
    /// 例: <c>D:\web\precure.tv\</c> → <c>D:\web\precure.tv.lastmod-manifest.json</c>。
    /// 出力ツリーの外側に置くため、デプロイ（出力ディレクトリ配下の同期）の対象にならない。
    /// </summary>
    public static string DeriveManifestPath(string outputDirectory)
        => outputDirectory.TrimEnd('/', '\\') + ".lastmod-manifest.json";

    /// <summary>
    /// 直前の解決で更新日が進んだページ（新規と変化）の一覧を置くパス（マニフェストと同じく出力ツリーの外）。
    /// 例: <c>D:\web\precure.tv\</c> → <c>D:\web\precure.tv.lastmod-changed.json</c>。IndexNow へ送るページを選ぶのに使う。
    /// </summary>
    public static string DeriveChangedListPath(string outputDirectory)
        => outputDirectory.TrimEnd('/', '\\') + ".lastmod-changed.json";

    /// <summary>直前の解決で更新日が進んだページの一覧を読む。無い・読めないときは null。</summary>
    public static ChangedPages? LoadChangedList(string outputDirectory)
    {
        try
        {
            var path = DeriveChangedListPath(outputDirectory);
            return File.Exists(path) ? JsonSerializer.Deserialize<ChangedPages>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 各ページの出力ファイルのハッシュを前回マニフェストと突き合わせ、URL ごとの lastmod を解決する。
    /// 解決後の状態を新しいマニフェストとして保存する（今ビルドに存在するページのみを残すため、
    /// 生成されなくなったページのエントリは自然に脱落する）。<paramref name="save"/> が false なら保存しない
    /// （出力に書き込まないビルド用）。
    /// </summary>
    /// <param name="pages">(URL パス, 出力ファイルの絶対パス) の列。書き出し順。</param>
    /// <param name="buildTimeIso">変化／新規ページに付ける当該ビルドの時刻（ISO-8601 / W3C Datetime）。</param>
    /// <param name="hashesOf">
    /// (URL パス, 出力ファイルの絶対パス) → 中身の印（大文字の 16 進）。今の判定のしかたの印（<c>Current</c>）と、
    /// 一つ前の版のしかたの印（<c>Legacy</c>）。取れないときは空文字（「変わった」扱い）。
    /// </param>
    /// <param name="version">今の判定のしかたの版（<see cref="StableContentHash.Version"/>）。控えに残す。</param>
    /// <param name="save">解決後の状態をマニフェストに保存するか。</param>
    public LastmodResolveResult Resolve(
        IReadOnlyList<(string UrlPath, string OutputFilePath)> pages,
        string buildTimeIso,
        Func<string, string, (string Current, string Legacy)> hashesOf,
        int version,
        bool save = true)
    {
        var previous = LoadPrevious();
        // 控えの判定のしかたの版（無い控えは版 1）。版が違う初回は、前回の印と一つ前の版のしかたの印で比べ（中身の変わったページだけ進める）、
        // 控えの印を今の版のしかたに取り直す。しかたを変えただけで全ページの更新日が進まないようにするため。
        int previousVersion = previous.Remove(VersionKey, out var versionEntry) && int.TryParse(versionEntry.Hash, out var v) ? v : 1;
        bool rebaseline = previous.Count > 0 && previousVersion != version;
        var next = new Dictionary<string, ManifestEntry>(pages.Count + 1, StringComparer.Ordinal);
        var lastmodByUrl = new Dictionary<string, string>(pages.Count, StringComparer.Ordinal);
        int unchanged = 0, changed = 0, added = 0;
        var addedUrls = new List<string>();
        var changedUrls = new List<string>();

        foreach (var (urlPath, outputFilePath) in pages)
        {
            // マニフェストの印は大文字の 16 進で持つ。
            var (current, legacy) = hashesOf(urlPath, outputFilePath);
            string compare = rebaseline ? legacy : current;
            string lastmod;
            // ハッシュが取得でき、かつ前回と一致したページだけ lastmod を据え置く。
            // ハッシュ取得不可（ファイル欠落など）は安全側で「変化扱い」＝当該ビルド時刻にする。
            if (compare.Length > 0 && previous.TryGetValue(urlPath, out var prev) && prev.Hash == compare)
            {
                lastmod = prev.Lastmod;
                unchanged++;
            }
            else
            {
                lastmod = buildTimeIso;
                if (previous.ContainsKey(urlPath)) { changed++; changedUrls.Add(urlPath); }
                else { added++; addedUrls.Add(urlPath); }
            }
            next[urlPath] = new ManifestEntry { Hash = current, Lastmod = lastmod };
            lastmodByUrl[urlPath] = lastmod;
        }
        next[VersionKey] = new ManifestEntry { Hash = version.ToString(System.Globalization.CultureInfo.InvariantCulture), Lastmod = "" };

        if (save)
        {
            Save(next);
            SaveChangedList(new ChangedPages { Added = addedUrls, Changed = changedUrls });
        }
        return new LastmodResolveResult
        {
            LastmodByUrl = lastmodByUrl,
            Unchanged = unchanged,
            Changed = changed,
            Added = added,
            Rebaselined = rebaseline,
            PreviousVersion = previousVersion,
        };
    }

    /// <summary>控えの中で、判定のしかたの版を持つ行の鍵（URL パスは「/」で始まるので重ならない）。</summary>
    private const string VersionKey = "$version";

    /// <summary>前回マニフェストを読み込む。未生成・破損時は空（＝全ページ新規扱い）で安全側に倒す。</summary>
    private Dictionary<string, ManifestEntry> LoadPrevious()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return new(StringComparer.Ordinal);
            var json = File.ReadAllText(_manifestPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(json);
            return loaded is null
                ? new(StringComparer.Ordinal)
                : new Dictionary<string, ManifestEntry>(loaded, StringComparer.Ordinal);
        }
        catch
        {
            // 破損していても全ページの lastmod が当該ビルド時刻になるだけで、害は無い。
            return new(StringComparer.Ordinal);
        }
    }

    /// <summary>更新日が進んだページの一覧を、マニフェストの隣（<c>….lastmod-changed.json</c>）に書く。</summary>
    private void SaveChangedList(ChangedPages pages)
    {
        const string manifestSuffix = ".lastmod-manifest.json";
        var path = _manifestPath.EndsWith(manifestSuffix, StringComparison.Ordinal)
            ? _manifestPath[..^manifestSuffix.Length] + ".lastmod-changed.json"
            : _manifestPath + ".changed.json";
        File.WriteAllText(path, JsonSerializer.Serialize(pages));
    }

    private void Save(Dictionary<string, ManifestEntry> entries)
    {
        PathUtil.EnsureParentDirectory(_manifestPath);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(entries));
    }

    /// <summary>マニフェストの 1 エントリ。JSON を小さく保つため短いキー名（h / m）でシリアライズする。</summary>
    private sealed class ManifestEntry
    {
        [JsonPropertyName("h")] public string Hash { get; set; } = "";
        [JsonPropertyName("m")] public string Lastmod { get; set; } = "";
    }
}

/// <summary>lastmod 解決の結果（URL→lastmod とビルド内訳カウント）。</summary>
/// <summary>直前の解決で更新日が進んだページの URL パス（新規・変化。それぞれ書き出し順）。</summary>
public sealed class ChangedPages
{
    [JsonPropertyName("added")] public List<string> Added { get; set; } = new();
    [JsonPropertyName("changed")] public List<string> Changed { get; set; } = new();
}

public sealed class LastmodResolveResult
{
    /// <summary>URL パス → 解決済み lastmod（ISO-8601 文字列）。</summary>
    public IReadOnlyDictionary<string, string> LastmodByUrl { get; init; } = new Dictionary<string, string>();

    /// <summary>前回から内容が変わらず lastmod を据え置いたページ数。</summary>
    public int Unchanged { get; init; }

    /// <summary>前回から内容が変化して lastmod を当該ビルド時刻へ進めたページ数。</summary>
    public int Changed { get; init; }

    /// <summary>前回マニフェストに無かった新規ページ数（当該ビルド時刻が入る）。</summary>
    public int Added { get; init; }

    /// <summary>判定のしかたの版が控えと違ったので、一つ前の版のしかたで比べ、印を今の版に取り直したか。</summary>
    public bool Rebaselined { get; init; }

    /// <summary>控えに残っていた判定のしかたの版（控えに版が無ければ 1）。</summary>
    public int PreviousVersion { get; init; }
}
