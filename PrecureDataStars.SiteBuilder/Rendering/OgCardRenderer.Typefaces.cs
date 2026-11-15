using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 書体名からこの PC の書体を引く部分。カードの見出しと、テロップの体裁で描く画像（サブタイトル・場面・書体見本）が同じ引き方を使う。
/// テロップの描画コードの印（<c>TelopRenderSourceStamp</c>）に入るファイルなので、カードの組み方のコードは置かない。
/// </summary>
public sealed partial class OgCardRenderer
{
    /// <summary>
    /// 設定の見出し書体のファイル名（空なら同梱の書体）。書体名が空のテロップはこの書体で描くので、テロップの作り置きの鍵に入れる。
    /// </summary>
    private readonly string _headingFontFile;

    /// <summary>
    /// 見出しの書体を決める。<see cref="OgCardSpec.TitleFontFamily"/>（作品の本編テロップの書体名）が指定されていれば
    /// インストール済み書体から引く。見つからなければ既定の見出し書体で組み、警告（1 枚につき 1 つ）を返す。
    /// 書体名は「FOT-ハミング ProN B」のように重さまで含む名前なので、まずその名前で引き、
    /// 無ければ末尾の重さを切り離して「書体の族 ＋ スタイル名」で引く（Windows が族名と重さを分けて見せる書体のため）。
    /// </summary>
    private SKTypeface ResolveTypeface(string fontFamily, SKTypeface fallback, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(fontFamily)) return fallback;

        var typeface = _familyTypefaces.GetOrAdd(fontFamily, family =>
        {
            var found = MatchInstalledTypeface(family);
            if (found is null) return null;
            lock (_familyLock)
            {
                _shapers.TryAdd(found.Handle, new OgTextShaper(found));
                _ownedTypefaces.Add(found);
            }
            return found;
        });
        if (typeface is null)
        {
            warning = $"書体「{fontFamily}」がこの PC に見つからないため、既定の書体で描画しました";
            return fallback;
        }
        return typeface;
    }

    /// <summary>この PC の書体ファイルの索引（name テーブルから引く）。初めて要るときに 1 度だけ作る。</summary>
    private static readonly Lazy<InstalledFontIndex> FontIndex = new(InstalledFontIndex.Build, isThreadSafe: true);

    /// <summary>書体名がこの PC で引けるか。描くときと同じ引き方（<see cref="MatchInstalledTypeface"/>）で確かめる。</summary>
    public static bool IsTypefaceInstalled(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        using var typeface = MatchInstalledTypeface(name);
        return typeface is not null;
    }

    /// <summary>
    /// インストール済み書体を名前で引く。まず書体ファイルの索引（日本語名・英語名とも）、次に Windows の書体一覧。見つからなければ null。
    /// どの経路で引いた書体も、その name テーブルに頼んだ名前があるかを照らし、無ければ使わない
    /// （書体一覧は、同じ族の別の書体を返すことがある。頼んだ書体と違う書体で描かないため）。
    /// </summary>
    private static SKTypeface? MatchInstalledTypeface(string name) => MatchInstalledTypeface(name, out _);

    /// <summary>
    /// <see cref="MatchInstalledTypeface(string)"/> と同じ。<paramref name="route"/> に、引けた経路か、引けなかった理由を返す（点検のログ用）。
    /// </summary>
    private static SKTypeface? MatchInstalledTypeface(string name, out string route)
    {
        bool mismatched = false;
        SKTypeface? Verified(SKTypeface? typeface)
        {
            if (typeface is null) return null;
            if (HasName(typeface, name)) return typeface;
            mismatched = true;
            typeface.Dispose();
            return null;
        }

        if (FontIndex.Value.Find(name) is { } hit && Verified(SKTypeface.FromFile(hit.Path, hit.Index)) is { } fromFile)
        {
            route = "書体ファイルの索引";
            return fromFile;
        }

        var manager = SKFontManager.Default;
        if (Verified(manager.MatchFamily(name)) is { } direct)
        {
            route = "Windows の書体一覧";
            return direct;
        }

        int cut = name.LastIndexOf(' ');
        if (cut > 0)
        {
            string family = name[..cut];
            string styleName = name[(cut + 1)..];
            using var styles = manager.GetFontStyles(family);
            for (int i = 0; styles is not null && i < styles.Count; i++)
            {
                if (!string.Equals(styles.GetStyleName(i), styleName, StringComparison.OrdinalIgnoreCase)) continue;
                if (Verified(styles.CreateTypeface(i)) is { } styled)
                {
                    route = "Windows の書体一覧（族名＋スタイル名）";
                    return styled;
                }
            }
        }
        route = mismatched ? "名前で引けた書体が、頼んだ名前の書体ではない" : "見つからない";
        return null;
    }

    /// <summary>書体の name テーブルに、その名前（全名称・族名・族名＋スタイル名のどれか）があるか。</summary>
    private static bool HasName(SKTypeface typeface, string name)
        => typeface.TryGetTableData(NameTableTag, out var table) && InstalledFontIndex.NameTableHasName(table, name);

    /// <summary>name テーブルのタグ（'name'）。</summary>
    private const uint NameTableTag = 0x6E616D65;

    /// <summary>書体名がこの PC でどう引けるか（引けた経路か、引けなかった理由）。点検のログ用。</summary>
    public static (bool Found, string Route) ProbeTypeface(string name)
    {
        using var typeface = MatchInstalledTypeface(name, out string route);
        return (typeface is not null, route);
    }

    /// <summary>
    /// カードの材料が名前で指す書体（見出し・タグライン・流し込みの事実行・エピソードのサブタイトルと振り仮名・書体見本）のうち、
    /// この PC で引けないものの名前。空でなければ、このカードは描かない（既定の書体に差し替えて描かないため）。
    /// </summary>
    public IReadOnlyList<string> MissingTypefaces(OgCardSpec spec)
    {
        var names = new[]
        {
            spec.TitleFontFamily, spec.StatementFontFamily, spec.InlineFactsFontFamily, spec.EpisodeTitleFontFamily,
            spec.Specimen?.FontFamily, spec.TitleRubyProfile?.RubyFontFamily, spec.EpisodeTitleRubyProfile?.RubyFontFamily
        };
        var missing = new List<string>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || missing.Contains(name)) continue;
            ResolveTypeface(name, _boldTypeface, out string? warning);
            if (warning is not null) missing.Add(name);
        }
        return missing;
    }
}
