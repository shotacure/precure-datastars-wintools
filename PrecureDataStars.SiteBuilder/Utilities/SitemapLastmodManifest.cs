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
/// なり、以降のビルドで収束する。ハッシュは MD5（デプロイの ETag/MD5 差分判定と同じ理屈）。
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
    /// 各ページの出力ファイルのハッシュを前回マニフェストと突き合わせ、URL ごとの lastmod を解決する。
    /// 解決後の状態を新しいマニフェストとして保存する（今ビルドに存在するページのみを残すため、
    /// 生成されなくなったページのエントリは自然に脱落する）。
    /// </summary>
    /// <param name="pages">(URL パス, 出力ファイルの絶対パス) の列。書き出し順。</param>
    /// <param name="buildTimeIso">変化／新規ページに付ける当該ビルドの時刻（ISO-8601 / W3C Datetime）。</param>
    public LastmodResolveResult Resolve(
        IReadOnlyList<(string UrlPath, string OutputFilePath)> pages,
        string buildTimeIso)
    {
        var previous = LoadPrevious();
        var next = new Dictionary<string, ManifestEntry>(pages.Count, StringComparer.Ordinal);
        var lastmodByUrl = new Dictionary<string, string>(pages.Count, StringComparer.Ordinal);
        int unchanged = 0, changed = 0, added = 0;

        foreach (var (urlPath, outputFilePath) in pages)
        {
            string hash = ComputeFileHashOrEmpty(outputFilePath);
            string lastmod;
            // ハッシュが取得でき、かつ前回と一致したページだけ lastmod を据え置く。
            // ハッシュ取得不可（ファイル欠落など）は安全側で「変化扱い」＝当該ビルド時刻にする。
            if (hash.Length > 0 && previous.TryGetValue(urlPath, out var prev) && prev.Hash == hash)
            {
                lastmod = prev.Lastmod;
                unchanged++;
            }
            else
            {
                lastmod = buildTimeIso;
                if (previous.ContainsKey(urlPath)) changed++; else added++;
            }
            next[urlPath] = new ManifestEntry { Hash = hash, Lastmod = lastmod };
            lastmodByUrl[urlPath] = lastmod;
        }

        Save(next);
        return new LastmodResolveResult
        {
            LastmodByUrl = lastmodByUrl,
            Unchanged = unchanged,
            Changed = changed,
            Added = added
        };
    }

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

    private void Save(Dictionary<string, ManifestEntry> entries)
    {
        PathUtil.EnsureParentDirectory(_manifestPath);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(entries));
    }

    /// <summary>出力ファイルの MD5 を 16 進文字列で返す。読めない場合は空文字（呼び出し側で変化扱い）。</summary>
    private static string ComputeFileHashOrEmpty(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(MD5.HashData(stream));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>マニフェストの 1 エントリ。JSON を小さく保つため短いキー名（h / m）でシリアライズする。</summary>
    private sealed class ManifestEntry
    {
        [JsonPropertyName("h")] public string Hash { get; set; } = "";
        [JsonPropertyName("m")] public string Lastmod { get; set; } = "";
    }
}

/// <summary>lastmod 解決の結果（URL→lastmod とビルド内訳カウント）。</summary>
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
}
