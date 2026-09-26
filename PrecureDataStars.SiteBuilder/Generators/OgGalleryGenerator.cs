using System.Text;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 生成済み OGP カードを一覧する確認用ページ（<c>/og-gallery/</c>）の生成。
/// <para>
/// カードはページを共有したときにしか表に出ないため、崩れや情報量の不足に気づきにくい。
/// 全カードを 1 ページに並べて、ページ種別ごとに見比べられるようにする。
/// </para>
/// <para>
/// <b>テストモード専用</b>。本番出力には書き出さないので、サイトマップにも載らず外からは辿れない。
/// 出力は素の HTML で、サイトのレイアウト（ヘッダ・フッタ・カード）を通さない。
/// 確認対象そのものを確認器具の中に持ち込まないためで、サイト側の不具合で
/// ギャラリーまで巻き込まれて読めなくなるのを避ける。
/// </para>
/// <para>
/// 画像はビルド済みの出力ディレクトリを走査して集める。カード生成は各ジェネレータの中で
/// ページ書き出しと同時に行われるので、全ページの書き出しが終わったあとに実行する必要がある。
/// </para>
/// </summary>
public sealed class OgGalleryGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;

    /// <summary><see cref="OgGalleryGenerator"/> の新しいインスタンスを生成する。</summary>
    public OgGalleryGenerator(BuildContext ctx, BuildConfig config)
    {
        _ctx = ctx;
        _config = config;
    }

    /// <summary>ギャラリーを書き出す。本番モードでは何もしない。</summary>
    public void Generate()
    {
        if (_config.IsProductionMode) return;

        string ogRoot = Path.Combine(_config.OutputDirectory, "og");
        if (!Directory.Exists(ogRoot)) return;

        // og/{section}/{slug}.png と og/{slug}.png（ホーム等）の 2 段。
        // セクション名で束ねて、ページ種別ごとに見比べられる並びにする。
        var groups = Directory
            .EnumerateFiles(ogRoot, "*.png", SearchOption.AllDirectories)
            .Select(f => new
            {
                Path = f,
                Rel = Path.GetRelativePath(ogRoot, f).Replace('\\', '/')
            })
            .GroupBy(x => x.Rel.Contains('/') ? x.Rel[..x.Rel.IndexOf('/')] : "(ルート)")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        int total = groups.Sum(g => g.Count());

        var sb = new StringBuilder();
        AppendHead(sb, total, groups.Count);

        foreach (var group in groups)
        {
            var items = group.OrderBy(x => x.Rel, StringComparer.Ordinal).ToList();
            sb.Append("<section class=\"grp\" id=\"g-").Append(HtmlUtil.Escape(group.Key)).Append("\">");
            sb.Append("<h2>").Append(HtmlUtil.Escape(group.Key))
              .Append(" <span class=\"n\">").Append(items.Count).Append(" 枚</span></h2>");
            sb.Append("<div class=\"grid\">");

            foreach (var item in items)
            {
                // カード画像のパスから元ページの URL を逆算する（og/people/高橋任治.png → /people/高橋任治/）。
                string slug = item.Rel[..^4];
                // 名前ベースのページ（og/people/高橋任治.png）は URL 側をパーセントエンコードする。
                string pageUrl = slug == "home" ? "/" : $"/{PathUtil.EncodePath(slug)}/";

                sb.Append("<figure>");
                sb.Append("<a href=\"").Append(HtmlUtil.Escape(pageUrl)).Append("\">");
                sb.Append("<img loading=\"lazy\" src=\"/og/").Append(HtmlUtil.Escape(PathUtil.EncodePath(item.Rel)))
                  .Append("\" alt=\"").Append(HtmlUtil.Escape(item.Rel)).Append("\">");
                sb.Append("</a>");
                sb.Append("<figcaption>").Append(HtmlUtil.Escape(pageUrl)).Append("</figcaption>");
                sb.Append("</figure>");
            }

            sb.Append("</div></section>");
        }

        sb.Append("</main></body></html>");

        string outDir = Path.Combine(_config.OutputDirectory, "og-gallery");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "index.html"), sb.ToString(), new UTF8Encoding(false));

        _ctx.Logger.Success($"og gallery: {total} 枚 / {groups.Count} 区分（テスト出力のみ）");
    }

    /// <summary>
    /// ギャラリーの前置き。カードは 1200×630 なので原寸では並べられない。
    /// 既定は 3 列の縮小表示で、崩れを疑った 1 枚はクリックで原寸を開いて確かめる。
    /// フッタ線（実寸 y=500）の位置に赤い目安線を重ね、サイト名の領域へ内容がはみ出していないか
    /// 一覧のまま目視できるようにする。
    /// </summary>
    private void AppendHead(StringBuilder sb, int total, int groupCount)
    {
        sb.Append("<!DOCTYPE html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"robots\" content=\"noindex,nofollow\">");
        sb.Append("<title>OGP カード一覧（確認用）</title><style>");
        sb.Append("body{font-family:system-ui,sans-serif;margin:0;background:#20202a;color:#eee}");
        sb.Append("header{position:sticky;top:0;background:#16161d;padding:14px 20px;border-bottom:1px solid #444;z-index:5}");
        sb.Append("h1{font-size:16px;margin:0 0 6px}");
        sb.Append("p.note{margin:0;font-size:12px;color:#aaa;line-height:1.7}");
        sb.Append("main{padding:20px}");
        sb.Append("h2{font-size:14px;margin:28px 0 10px;color:#ffb3d1}");
        sb.Append("h2 .n{font-weight:400;color:#888;font-size:12px}");
        sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(380px,1fr));gap:14px}");
        sb.Append("figure{margin:0;background:#2a2a35;border-radius:6px;overflow:hidden}");
        // 画像の上にフッタ線の位置（630 分の 500 ＝ 79.37%）を赤線で重ねる。
        sb.Append("figure a{display:block;position:relative;line-height:0}");
        sb.Append("figure a::after{content:\"\";position:absolute;left:0;right:0;top:79.37%;");
        sb.Append("border-top:1px solid rgba(255,0,0,.55);pointer-events:none}");
        sb.Append("img{width:100%;height:auto;display:block}");
        sb.Append("figcaption{padding:6px 10px;font-size:11px;color:#bbb;word-break:break-all}");
        sb.Append("</style></head><body>");
        sb.Append("<header><h1>OGP カード一覧（確認用・テスト出力のみ）</h1>");
        sb.Append("<p class=\"note\">全 ").Append(total).Append(" 枚 / ").Append(groupCount).Append(" 区分。");
        sb.Append("赤い線はカード下部のフッタ罫線（実寸 y=500）の位置です。");
        sb.Append("この線より下はサイト名の領域なので、内容が線を越えていたらはみ出しです。");
        sb.Append("画像をクリックすると元ページを開きます。</p></header><main>");
    }
}
