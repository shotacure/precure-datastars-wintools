using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 左のもくじ（section-nav.js）に映画の題名を出すときのラベルを、上の段のシリーズ名の部分（Prefix）と
/// 下の段の副題（Main）に分ける。ナビの幅で「映画 プリキュアオール…」と切れて見分けられなくなるのを避ける。
/// 分けるのは映画（MOVIE）・春映画（SPRING）・併映の短編（MOVIE_SHORT）だけで、ほかの作品は題名のまま。
/// <list type="number">
///   <item><description>題名の頭の「映画」（と続く空白）は、切る所を探すときだけ脇に置き、上の段には題名どおり残す（「映画 HUGっと！プリキュア♡ふたりはプリキュア」）</description></item>
///   <item><description>親の作品の題名で始まっていれば（！？の全角・半角の違いは同じとみなす）その後ろから、始まっていなければ頭から、最初の空白を探して切る
///     （「HUGっと！プリキュア♡ふたりはプリキュア」「わんだふるぷりきゅあ!ざ・むーびー!」のように親の題名に続く語も上の段に入る。
///     「ふたりはプリキュアMax Heart」のように親の題名の中の空白では切らない）</description></item>
///   <item><description>副題の頭が数字だけの語（「Max Heart 2」の 2）なら上の段に入れる</description></item>
///   <item><description>空白が無ければ分けず、Prefix は空・Main は題名のまま。ただし春映画（SPRING）で「プリキュア」で始まるものは、
///     上の段を「映画 プリキュア」、下の段を残り（「ドリームスターズ！」）にする</description></item>
/// </list>
/// テンプレートからは <c>{{ nav_prefix 題名 }}</c> / <c>{{ nav_main 題名 }}</c> で引く（<see cref="Rendering.ScribanRenderer"/>）。
/// 作品の一覧は Pipeline がビルド開始時に <see cref="UseSeries"/> で 1 度だけ差し込み、以降は読み取り専用（並列レンダリングから引いても安全）。
/// </summary>
public static class MovieNavTitle
{
    private static readonly HashSet<string> SplitKinds = new(StringComparer.Ordinal) { "MOVIE", "SPRING", "MOVIE_SHORT" };

    /// <summary>題名 → 分けた結果（映画・春映画・併映の短編だけ）。未設定のあいだは空で、どの題名も分けない。</summary>
    private static IReadOnlyDictionary<string, (string Prefix, string Main)> _byTitle =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal);

    /// <summary>作品の一覧を差し込み、映画・春映画・併映の短編の題名を分けておく（ページ生成より前に 1 度だけ呼ぶ）。</summary>
    public static void UseSeries(IReadOnlyDictionary<int, Series> seriesById)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var s in seriesById.Values)
        {
            if (!SplitKinds.Contains(s.KindCode)) continue;
            map[s.Title] = Split(s, seriesById);
        }
        _byTitle = map;
    }

    /// <summary>題名の上の段（シリーズ名の部分）。映画・春映画・併映の短編でない題名や、分けられない題名は空。</summary>
    public static string PrefixPart(string title) => _byTitle.TryGetValue(title ?? "", out var r) ? r.Prefix : "";

    /// <summary>題名の下の段（副題）。映画・春映画・併映の短編でない題名や、分けられない題名は題名のまま。</summary>
    public static string MainPart(string title) => _byTitle.TryGetValue(title ?? "", out var r) ? r.Main : (title ?? "");

    /// <summary>1 作品の題名を分ける（決まりはクラスの説明のとおり）。</summary>
    public static (string Prefix, string Main) Split(Series m, IReadOnlyDictionary<int, Series> seriesById)
    {
        string body = m.Title.StartsWith("映画", StringComparison.Ordinal) ? m.Title.Substring(2).TrimStart(' ', '　') : m.Title;
        // 頭の「映画」と続く空白（題名の書き方のまま）。上の段の頭に付け直す
        string head = m.Title.Substring(0, m.Title.Length - body.Length);
        static string Norm(string s) => s.Replace('！', '!').Replace('？', '?');
        int start = 0;
        if (m.ParentSeriesId is int pid && seriesById.TryGetValue(pid, out var parent)
            && Norm(body).StartsWith(Norm(parent.Title), StringComparison.Ordinal))
        {
            start = parent.Title.Length;
        }
        int cut = body.IndexOfAny(new[] { ' ', '　' }, Math.Min(start, body.Length));
        if (cut < 0)
        {
            // 空白の無い春映画（「映画 プリキュアドリームスターズ！」など）は、頭の「映画 プリキュア」だけを上の段にする
            const string Precure = "プリキュア";
            if (string.Equals(m.KindCode, "SPRING", StringComparison.Ordinal)
                && body.StartsWith(Precure, StringComparison.Ordinal) && body.Length > Precure.Length)
            {
                return (head + Precure, body.Substring(Precure.Length));
            }
            return ("", m.Title);
        }
        string prefix = head + body.Substring(0, cut);
        string main = body.Substring(cut + 1).TrimStart(' ', '　');
        var num = System.Text.RegularExpressions.Regex.Match(main, @"^(\d+)[ 　]+(.+)$");
        if (num.Success)
        {
            prefix += " " + num.Groups[1].Value;
            main = num.Groups[2].Value;
        }
        return main.Length == 0 ? ("", m.Title) : (prefix, main);
    }
}
