using System.Globalization;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 未放送話（前話の予告がまだ放送されていない話数）のサブタイトルを、ブラウザ側 JS
/// （<c>wwwroot/assets/subtitle-embargo.js</c>）がぼかし表示できる形にラップする共有ヘルパー。
/// サーバー側（本ビルダー）は実サブタイトルをそのまま埋め込む。解禁時刻を過ぎているかどうかの
/// 判定・表示切り替えは一切ここでは行わず、常にクライアント側の現在時刻比較に委ねる
/// （<see cref="Utilities.SubtitleEmbargoCalculator"/> が算出した解禁時刻を data 属性に埋め込むだけ）。
/// 解禁時刻が算出できない（＝辞書に無い）話はガード自体を付けず、素の HTML をそのまま返す。
/// </summary>
public static class SubtitleGuardRenderer
{
    private const string GuardClass = "ep-subtitle-guard";

    /// <summary>episode_id から解禁時刻辞書を引く。無ければ null（＝ガード不要）。</summary>
    public static DateTimeOffset? RevealAtFor(int episodeId, IReadOnlyDictionary<int, DateTimeOffset> revealAtByEpisodeId)
        => revealAtByEpisodeId.TryGetValue(episodeId, out var at) ? at : null;

    /// <summary>
    /// プレーンテキストのサブタイトルをエスケープしたうえで、必要ならガード span で包む。
    /// h1 の「第N話「サブタイトル」」など、他の要素に埋め込む断片を作るのに使う。
    /// </summary>
    public static string GuardPlainText(string plainText, DateTimeOffset? revealAt)
        => Wrap(HtmlUtil.Escape(plainText), revealAt);

    /// <summary>
    /// 既に安全な（自前でエスケープ済み・ルビタグ等を含む）HTML 断片を、必要ならガード span で包む。
    /// 二重エスケープを避けるため、呼び出し側で組み立て済みの HTML をそのまま渡すこと。
    /// </summary>
    public static string GuardRichHtml(string safeHtml, DateTimeOffset? revealAt)
        => Wrap(safeHtml, revealAt);

    /// <summary>
    /// エピソード一覧行の定番パターン（<c>TitleRichHtml</c> があれば優先、無ければ
    /// <c>TitleText</c> のエスケープ平文）をガード込みで 1 回で組み立てる。
    /// series-detail / episodes-index / home の 3 箇所で同一だった分岐をここに集約する。
    /// </summary>
    public static string BuildEpisodeRowTitleHtml(string? richHtml, string plainText, DateTimeOffset? revealAt)
    {
        string inner = !string.IsNullOrEmpty(richHtml) ? richHtml! : HtmlUtil.Escape(plainText);
        return Wrap(inner, revealAt);
    }

    /// <summary>data-reveal-at 属性用に ISO 8601（+09:00 固定オフセット）へ整形する。</summary>
    public static string ToRevealAtIso(DateTimeOffset revealAt)
        => revealAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Wrap(string innerHtml, DateTimeOffset? revealAt)
    {
        if (revealAt is not { } at) return innerHtml;
        return $"<span class=\"{GuardClass}\" data-reveal-at=\"{ToRevealAtIso(at)}\">{innerHtml}</span>";
    }
}
