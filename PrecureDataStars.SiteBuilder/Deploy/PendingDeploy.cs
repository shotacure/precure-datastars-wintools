using System.Text.Json;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// ドライラン（<c>--deploy --dry-run</c>）で作った出力を、作り直さずに本番へ上げる（<c>--upload-only</c>）ための控え。
/// 出力の隣の <c>{出力ディレクトリ}.pending-deploy.json</c> に、その出力を作ったときの人物・キャラ・企業・ユニットの URL
/// （公開記録 <c>published_entity_slugs</c> に書く分）を残す。
/// <list type="bullet">
///   <item>全体ビルドは始めに控えを消し、全体のドライランだけが終わりに控えを書く。控えがあるのは
///     「いまの出力がドライランで作ったまま」のときだけになる。</item>
///   <item><c>--upload-only</c> は控えが無ければ止まり、上げ終えたら控えの URL を公開記録に書いて控えを消す。</item>
/// </list>
/// 出力ディレクトリの中には置かない（S3 へ上がらないように）。
/// </summary>
/// <param name="Persons">人物 ID → URL のスラッグ。</param>
/// <param name="Characters">キャラクター ID → URL のスラッグ。</param>
/// <param name="Companies">企業 ID → URL のスラッグ。</param>
/// <param name="Units">ユニット名義の ID → URL のスラッグ。</param>
public sealed record PendingDeploy(
    IReadOnlyDictionary<int, string> Persons,
    IReadOnlyDictionary<int, string> Characters,
    IReadOnlyDictionary<int, string> Companies,
    IReadOnlyDictionary<int, string> Units)
{
    /// <summary>出力ディレクトリに対応する控えのパス（出力の兄弟。sitemap の lastmod の記録と同じ置き方）。</summary>
    public static string PathFor(string outputDirectory)
        => outputDirectory.TrimEnd('/', '\\') + ".pending-deploy.json";

    /// <summary>控えを書く。</summary>
    public void Save(string outputDirectory)
        => File.WriteAllText(PathFor(outputDirectory), JsonSerializer.Serialize(this));

    /// <summary>控えを読む。無ければ null。</summary>
    public static PendingDeploy? Load(string outputDirectory)
    {
        var path = PathFor(outputDirectory);
        return File.Exists(path) ? JsonSerializer.Deserialize<PendingDeploy>(File.ReadAllText(path)) : null;
    }

    /// <summary>控えを消す（無ければ何もしない）。</summary>
    public static void Delete(string outputDirectory)
    {
        var path = PathFor(outputDirectory);
        if (File.Exists(path)) File.Delete(path);
    }
}
