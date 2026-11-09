using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 版の付いたカード（<c>credit_cards.media_version</c>：本放送 / Blu-ray・配信）の描き方。
/// 共通のカード（版なし）はそのまま並べ、本放送のカードとそれに当たる Blu-ray・配信のカードの 1 組ごとに 1 つの枠（<c>.card-versions</c>）を作って、
/// 上半分に本放送（<c>.card-version-broadcast</c>）、下半分に Blu-ray・配信（<c>.card-version-homevideo</c>）を描く。
/// 版の付いたカードが続くときは、入力で隣どうしに並べた 1 組（本放送 → Blu-ray・配信、または逆）ごとに枠を分ける。
/// 片方の版にしか無いところは、もう片方の半分に「（なし）」を出す。
/// SiteBuilder のクレジット描画と Catalog のプレビューで共有する。
/// </summary>
public static class CardVersionLayout
{
    /// <summary>
    /// カードの並び（card_seq 順）のうち、版の付いたカードが続くところを 1 組ずつの枠に分け、枠の中を本放送を先・Blu-ray・配信を後に並べ替える
    /// （同じ版の中の順は保つ）。共通のカードの位置は動かさない。
    /// 枠の区切り：枠の最初のカードと同じ版のカードが、もう片方の版のカードの後にまた来たら、そこから次の枠にする
    /// （本放送 1・Blu-ray 1・本放送 2・Blu-ray 2 → [本放送 1・Blu-ray 1]・[本放送 2・Blu-ray 2]）。
    /// </summary>
    public static List<T> OrderVersionRuns<T>(IReadOnlyList<T> cards, Func<T, string?> versionOf)
    {
        var result = new List<T>(cards.Count);
        var frame = new List<T>();
        void Flush()
        {
            result.AddRange(frame.Where(c => versionOf(c) == CreditMediaVersions.Broadcast));
            result.AddRange(frame.Where(c => versionOf(c) != CreditMediaVersions.Broadcast));
            frame.Clear();
        }
        foreach (var card in cards)
        {
            var v = versionOf(card);
            if (v is null)
            {
                Flush();
                result.Add(card);
                continue;
            }
            if (frame.Count > 0)
            {
                var first = versionOf(frame[0]);
                bool hasOther = frame.Any(c => versionOf(c) != first);
                if (v == first && hasOther) Flush();
            }
            frame.Add(card);
        }
        Flush();
        return result;
    }

    /// <summary>
    /// 版の付いたカードの 1 組（枠）ごとの並び（元の card_seq の順のまま）を返す。共通のカードは含めない。
    /// 区切り方は <see cref="OrderVersionRuns{T}"/> と同じ（描画の枠と集計の組を同じものにする）。
    /// </summary>
    public static List<List<T>> SplitVersionFrames<T>(IReadOnlyList<T> cards, Func<T, string?> versionOf)
    {
        var frames = new List<List<T>>();
        var frame = new List<T>();
        foreach (var card in cards)
        {
            var v = versionOf(card);
            if (v is null)
            {
                if (frame.Count > 0) { frames.Add(frame); frame = new List<T>(); }
                continue;
            }
            if (frame.Count > 0)
            {
                var first = versionOf(frame[0]);
                if (v == first && frame.Any(c => versionOf(c) != first)) { frames.Add(frame); frame = new List<T>(); }
            }
            frame.Add(card);
        }
        if (frame.Count > 0) frames.Add(frame);
        return frames;
    }

    /// <summary>
    /// カードを描く前に出す HTML（並べ替え済みの並びで、前のカードの版 <paramref name="previous"/> と
    /// このカードの版 <paramref name="current"/> から決める）。1 組の頭で枠を開き、版の半分を開く。
    /// </summary>
    public static string OpenBefore(string? previous, string? current)
    {
        if (current is null) return "";
        var sb = new System.Text.StringBuilder();
        // 枠の頭：前が共通のカードか、前の枠の Blu-ray・配信の後に次の組の本放送が来たところ（並べ替え済みなので枠の中は本放送 → Blu-ray・配信）
        if (previous is null || (previous == CreditMediaVersions.HomeVideo && current == CreditMediaVersions.Broadcast))
        {
            sb.Append("<div class=\"card-versions\">");
            // Blu-ray・配信から始まる続き（本放送に無いカード）は、上半分に「（なし）」を出す。
            if (current == CreditMediaVersions.HomeVideo) sb.Append(EmptySection(CreditMediaVersions.Broadcast));
        }
        if (previous != current) sb.Append(OpenSection(current));
        return sb.ToString();
    }

    /// <summary>
    /// カードを描いた後に出す HTML（このカードの版 <paramref name="current"/> と次のカードの版 <paramref name="next"/> から決める）。
    /// 版の半分の終わりで閉じ、1 組の終わりで枠を閉じる。
    /// </summary>
    public static string CloseAfter(string? current, string? next)
    {
        if (current is null || next == current) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("</div>"); // .card-version
        // 枠の終わり：次が共通のカードか、Blu-ray・配信の後に次の組の本放送が来るところ
        if (next is null || (current == CreditMediaVersions.HomeVideo && next == CreditMediaVersions.Broadcast))
        {
            // 本放送だけで終わる続き（Blu-ray・配信に無いカード）は、下半分に「（なし）」を出す。
            if (current == CreditMediaVersions.Broadcast) sb.Append(EmptySection(CreditMediaVersions.HomeVideo));
            sb.Append("</div>"); // .card-versions
        }
        return sb.ToString();
    }

    /// <summary>
    /// カードをまたいで持ち越す描画の状態（前のカードから続く声の出演の見出しを省くための、直前の声の出演の役職など）を、
    /// 1 組の枠の本放送の半分と Blu-ray・配信の半分で同じ出発点にそろえる。
    /// 枠の頭で状態を <paramref name="saved"/> に控え、本放送から Blu-ray・配信へ切り替わるところで控えた状態に戻す。
    /// 枠の後の共通のカードには、Blu-ray・配信の半分を描き終えた状態がそのまま続く。
    /// </summary>
    public static T CarryState<T>(string? previous, string? current, T state, ref T saved)
    {
        if (current is null) return state;
        if (previous is null || (previous == CreditMediaVersions.HomeVideo && current == CreditMediaVersions.Broadcast))
        {
            saved = state;
            return state;
        }
        if (previous == CreditMediaVersions.Broadcast && current == CreditMediaVersions.HomeVideo) return saved;
        return state;
    }

    /// <summary>版の半分を開く（見出しの札つき）。</summary>
    private static string OpenSection(string version)
        => $"<div class=\"card-version {SectionClass(version)}\"><p class=\"card-version-label\">{System.Net.WebUtility.HtmlEncode(CreditMediaVersions.LabelOf(version))}</p>";

    /// <summary>その版にカードが無いときの半分（「（なし）」だけ）。</summary>
    private static string EmptySection(string version)
        => OpenSection(version) + "<p class=\"card-version-none\">（なし）</p></div>";

    private static string SectionClass(string version)
        => version == CreditMediaVersions.Broadcast ? "card-version-broadcast" : "card-version-homevideo";
}
