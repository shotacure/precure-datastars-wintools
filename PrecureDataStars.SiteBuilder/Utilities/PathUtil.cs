namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 出力パスと URL パスの組み立てヘルパー。
/// <para>
/// 全ページは末尾スラッシュ + <c>index.html</c> 運用とする。
/// 例: <c>/series/precure/1/index.html</c> がディスク上の出力で、参照 URL は <c>/series/precure/1/</c>。
/// </para>
/// </summary>
public static class PathUtil
{
    /// <summary>
    /// 「URL パス」（先頭スラッシュ付き、末尾スラッシュ付き）を「出力ファイルパス」に変換する。
    /// 末尾は <c>index.html</c> を付与。
    /// </summary>
    /// <param name="outputRoot">出力ルートディレクトリ。</param>
    /// <param name="urlPath">URL パス（例 "/series/precure/"）。先頭スラッシュは必須。</param>
    public static string ToOutputFilePath(string outputRoot, string urlPath)
    {
        if (string.IsNullOrEmpty(urlPath) || urlPath[0] != '/')
            throw new ArgumentException("urlPath must start with '/'.", nameof(urlPath));

        // 先頭スラッシュを除去 → OS 区切り文字に変換 → index.html を末尾につなぐ。
        // urlPath が "/" の場合はサイトトップなので、出力は <root>/index.html。
        var trimmed = urlPath.TrimStart('/').TrimEnd('/');
        var relativeDir = trimmed.Length == 0
            ? string.Empty
            : trimmed.Replace('/', Path.DirectorySeparatorChar);
        var fullDir = string.IsNullOrEmpty(relativeDir)
            ? outputRoot
            : Path.Combine(outputRoot, relativeDir);
        return Path.Combine(fullDir, "index.html");
    }

    /// <summary>
    /// シリーズページの URL パスを返す（末尾スラッシュ付き）。
    /// </summary>
    public static string SeriesUrl(string slug) => $"/series/{slug}/";

    /// <summary>
    /// エピソードページの URL パスを返す。
    /// </summary>
    public static string EpisodeUrl(string slug, int seriesEpNo) => $"/series/{slug}/{seriesEpNo}/";

    /// <summary>人物詳細ページの URL パス。</summary>
    public static string PersonUrl(int personId) => $"/persons/{personId}/";

    /// <summary>企業詳細ページの URL パス。</summary>
    public static string CompanyUrl(int companyId) => $"/companies/{companyId}/";

    /// <summary>プリキュア詳細ページの URL パス。</summary>
    public static string PrecureUrl(int precureId) => $"/precures/{precureId}/";

    /// <summary>キャラクター詳細ページの URL パス。</summary>
    public static string CharacterUrl(int characterId) => $"/characters/{characterId}/";

    /// <summary>商品詳細ページの URL パス（catalog_no を URL エンコードして安全に格納）。</summary>
    public static string ProductUrl(string productCatalogNo)
        => $"/products/{Uri.EscapeDataString(productCatalogNo)}/";

    /// <summary>楽曲詳細ページの URL パス。</summary>
    public static string SongUrl(int songId) => $"/songs/{songId}/";

    /// <summary>
    /// 全ファイルパスから親ディレクトリを再帰的に作成する。
    /// </summary>
    public static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// アセット（CSS など）の URL パス。
    /// </summary>
    public static string AssetUrl(string assetRelative) => "/assets/" + assetRelative.TrimStart('/');
}
