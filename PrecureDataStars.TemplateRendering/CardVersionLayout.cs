using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 版の付いたカード（<c>credit_cards.media_version</c>：そのカードが出る媒体の組）の描き方と組の割り出し。
/// 共通のカード（版なし）はそのまま並べ、媒体によって違うところは 2 つの版のカードの 1 組ごとに 1 つの枠（<c>.card-versions</c>）を作って、
/// 組の前の版を上半分（<c>.card-version-upper</c>）、後ろの版を下半分（<c>.card-version-lower</c>）に描く。
/// 半分の見出しは版の表示名（<see cref="CreditMediaVersions.LabelOf"/>：「本放送」「Blu-ray・配信」「本放送・Blu-ray」「配信」など）。
/// 後ろの版が集計の本体（前の版のカードは、組の後ろの版のカードにいない人だけを数える）。
/// SiteBuilder のクレジット描画・集計と Catalog のプレビューで共有する。
/// </summary>
public static class CardVersionLayout
{
    /// <summary>
    /// カードの並び（card_seq 順）から、描く順と枠の開け閉めを割り出す。
    /// <para>
    /// 枠の区切り：版の付いたカードが続くところを 1 組ずつに分ける。枠の最初のカードと同じ版のカードが、もう片方の版のカードの後に
    /// また来たら、そこから次の枠にする（前 1・後 1・前 2・後 2 → [前 1・後 1]・[前 2・後 2]）。共通のカードで枠は終わる。
    /// </para>
    /// <para>
    /// 上下：クレジットの中で 2 つの版がそろう最初の枠で、先に現れた版を前（上）、もう片方を後ろ（下）とする。
    /// どの枠も片方の版しか無いときは、本放送を含む版を前、そうでない版を後ろとする。
    /// 枠の中は前の版のカードを先・後ろの版のカードを後に並べ替える（同じ版の中の順は保つ）。共通のカードの位置は動かさない。
    /// </para>
    /// </summary>
    public static CardVersionPlan<T> Plan<T>(IReadOnlyList<T> cardsInSeq, Func<T, string?> versionOf)
        => new(cardsInSeq, versionOf);
}

/// <summary>
/// <see cref="CardVersionLayout.Plan{T}"/> の結果。描く順のカード（<see cref="Cards"/>）と、カードの前後に出す枠の HTML、
/// 1 組ずつの前・後ろのカード（<see cref="Frames"/>）を持つ。
/// </summary>
public sealed class CardVersionPlan<T>
{
    private readonly Func<T, string?> _versionOf;
    private readonly string[] _before;
    private readonly string[] _after;
    private readonly bool[] _frameStart;
    private readonly bool[] _lowerStartAfterUpper;

    /// <summary>描く順のカード（枠の中は前の版 → 後ろの版）。</summary>
    public IReadOnlyList<T> Cards { get; }

    /// <summary>組の前（上）の版。版の付いたカードが無い、または前の版が決まらないときは null。</summary>
    public string? UpperVersion { get; }

    /// <summary>組の後ろ（下）の版。集計の本体。版の付いたカードが無い、または後ろの版が決まらないときは null。</summary>
    public string? LowerVersion { get; }

    /// <summary>1 組ずつの前の版のカードと後ろの版のカード（どちらも元の card_seq の順）。共通のカードは含めない。</summary>
    public IReadOnlyList<(IReadOnlyList<T> Upper, IReadOnlyList<T> Lower)> Frames { get; }

    internal CardVersionPlan(IReadOnlyList<T> cardsInSeq, Func<T, string?> versionOf)
    {
        _versionOf = versionOf;

        // 枠に分ける（共通のカードは null の印で残す）。
        var segments = new List<(T? Common, List<T>? Frame)>();
        List<T>? frame = null;
        foreach (var card in cardsInSeq)
        {
            var v = versionOf(card);
            if (v is null)
            {
                if (frame is not null) { segments.Add((default, frame)); frame = null; }
                segments.Add((card, null));
                continue;
            }
            if (frame is not null)
            {
                var first = versionOf(frame[0]);
                if (v == first && frame.Any(c => versionOf(c) != first)) { segments.Add((default, frame)); frame = null; }
            }
            frame ??= new List<T>();
            frame.Add(card);
        }
        if (frame is not null) segments.Add((default, frame));

        // 前の版・後ろの版を決める。
        var frames = segments.Where(s => s.Frame is not null).Select(s => s.Frame!).ToList();
        var paired = frames.FirstOrDefault(f => f.Select(versionOf).Distinct().Count() >= 2);
        if (paired is not null)
        {
            UpperVersion = versionOf(paired[0]);
            LowerVersion = paired.Select(versionOf).First(v => v != UpperVersion);
        }
        else
        {
            var versions = cardsInSeq.Select(versionOf).Where(v => v is not null).Distinct().ToList();
            UpperVersion = versions.FirstOrDefault(CreditMediaVersions.IncludesBroadcast);
            LowerVersion = versions.FirstOrDefault(v => !CreditMediaVersions.IncludesBroadcast(v));
        }

        // 描く順と、カードの前後に出す HTML。
        var cards = new List<T>(cardsInSeq.Count);
        var before = new List<string>();
        var after = new List<string>();
        var frameStart = new List<bool>();
        var lowerStartAfterUpper = new List<bool>();
        var frameList = new List<(IReadOnlyList<T> Upper, IReadOnlyList<T> Lower)>();
        foreach (var (common, f) in segments)
        {
            if (f is null)
            {
                cards.Add(common!);
                before.Add(""); after.Add(""); frameStart.Add(false); lowerStartAfterUpper.Add(false);
                continue;
            }
            var upper = f.Where(IsUpperVersion).ToList();
            var lower = f.Where(c => !IsUpperVersion(c)).ToList();
            frameList.Add((upper, lower));
            for (int i = 0; i < upper.Count; i++)
            {
                cards.Add(upper[i]);
                string open = i == 0 ? "<div class=\"card-versions\">" + OpenSection(UpperVersion, "upper") : "";
                string close = "";
                if (i == upper.Count - 1)
                {
                    close = "</div>";   // .card-version（上）
                    // 後ろの版に当たるカードが無い組は、下半分に「（なし）」を出して枠を閉じる。
                    if (lower.Count == 0) close += EmptySection(LowerVersion, "lower") + "</div>";
                }
                before.Add(open); after.Add(close); frameStart.Add(i == 0); lowerStartAfterUpper.Add(false);
            }
            for (int i = 0; i < lower.Count; i++)
            {
                cards.Add(lower[i]);
                string open = "";
                if (i == 0)
                {
                    // 前の版に当たるカードが無い組は、上半分に「（なし）」を出してから下半分を開く。
                    if (upper.Count == 0) open = "<div class=\"card-versions\">" + EmptySection(UpperVersion, "upper");
                    open += OpenSection(LowerVersion, "lower");
                }
                string close = i == lower.Count - 1 ? "</div></div>" : "";   // .card-version（下）と .card-versions
                before.Add(open); after.Add(close);
                frameStart.Add(i == 0 && upper.Count == 0);
                lowerStartAfterUpper.Add(i == 0 && upper.Count > 0);
            }
        }
        Cards = cards;
        _before = before.ToArray();
        _after = after.ToArray();
        _frameStart = frameStart.ToArray();
        _lowerStartAfterUpper = lowerStartAfterUpper.ToArray();
        Frames = frameList;
    }

    /// <summary>そのカードが組の前（上）の版か。共通のカードは false。</summary>
    public bool IsUpper(T card) => _versionOf(card) is not null && IsUpperVersion(card);

    private bool IsUpperVersion(T card)
    {
        var v = _versionOf(card);
        // 前の版が決まらない（後ろの版しか無い）ときは、前の版でも後ろの版でもないカードを前に置く
        return UpperVersion is not null ? v == UpperVersion : v != LowerVersion;
    }

    /// <summary>描く順の <paramref name="index"/> 番目のカードの前に出す HTML（枠・版の半分を開く）。</summary>
    public string OpenBefore(int index) => _before[index];

    /// <summary>描く順の <paramref name="index"/> 番目のカードの後に出す HTML（版の半分・枠を閉じる）。</summary>
    public string CloseAfter(int index) => _after[index];

    /// <summary>
    /// カードをまたいで持ち越す描画の状態（前のカードから続く声の出演の見出しを省くための、直前の声の出演の役職など）を、
    /// 1 組の枠の上半分と下半分で同じ出発点にそろえる。枠の頭で状態を <paramref name="saved"/> に控え、上半分から下半分へ
    /// 切り替わるところで控えた状態に戻す。枠の後の共通のカードには、下半分を描き終えた状態がそのまま続く。
    /// </summary>
    public TState CarryState<TState>(int index, TState state, ref TState saved)
    {
        if (_frameStart[index]) { saved = state; return state; }
        if (_lowerStartAfterUpper[index]) return saved;
        return state;
    }

    /// <summary>版の半分を開く（見出しの札つき）。版が決まらない半分は札を出さない。</summary>
    private static string OpenSection(string? version, string position)
    {
        string label = CreditMediaVersions.LabelOf(version);
        string head = label.Length == 0 ? "" : $"<p class=\"card-version-label\">{System.Net.WebUtility.HtmlEncode(label)}</p>";
        return $"<div class=\"card-version card-version-{position}\">{head}";
    }

    /// <summary>その版にカードが無いときの半分（「（なし）」だけ）。</summary>
    private static string EmptySection(string? version, string position)
        => OpenSection(version, position) + "<p class=\"card-version-none\">（なし）</p></div>";
}
