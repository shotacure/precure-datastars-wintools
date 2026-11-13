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

    /// <summary>インストール済み書体を名前で引く。まず書体ファイルの索引（日本語名・英語名とも）、次に Windows の書体一覧。見つからなければ null。</summary>
    private static SKTypeface? MatchInstalledTypeface(string name)
    {
        if (FontIndex.Value.Find(name) is { } hit)
        {
            var fromFile = SKTypeface.FromFile(hit.Path, hit.Index);
            if (fromFile is not null) return fromFile;
        }

        var manager = SKFontManager.Default;
        var direct = manager.MatchFamily(name);
        if (direct is not null) return direct;

        int cut = name.LastIndexOf(' ');
        if (cut <= 0) return null;
        string family = name[..cut];
        string styleName = name[(cut + 1)..];
        using var styles = manager.GetFontStyles(family);
        if (styles is null || styles.Count == 0) return null;
        for (int i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles.GetStyleName(i), styleName, StringComparison.OrdinalIgnoreCase))
                return styles.CreateTypeface(i);
        }
        return null;
    }
}
