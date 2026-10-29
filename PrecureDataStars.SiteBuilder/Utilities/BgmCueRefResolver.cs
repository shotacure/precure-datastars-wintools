using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 劇伴の曲（series_id + m_no_detail）を、他のページから指すときの表記とリンク先に解決する。
/// 歌の曲詳細の「この劇伴に使われています」と、人物詳細の音楽クレジットの曲（著作物）の行で使う。
/// <para>
/// 表記は劇伴詳細のカードに合わせる：M 番号が分かっている曲は「M 番号 メニュー」、
/// 仮 M 番号の曲はメニュー、メニューも無ければ収録盤のトラック名、それも無ければ「(Mナンバー不明)」。
/// リンク先は劇伴詳細のその曲のカード（<see cref="PathUtil.BgmCueAnchorUrl"/>）。
/// </para>
/// 対象は使われている曲を持つ劇伴の曲（<see cref="BuildContext.BgmCueSourcePartsByCue"/> のキー）だけで、
/// 必要な表引きはコンストラクタで済ませる（以後は読み取りだけなので並列レンダリング中に呼んでよい）。
/// </summary>
public sealed class BgmCueRefResolver
{
    private readonly BuildContext _ctx;

    /// <summary>使われている曲を持つ劇伴の曲 → 劇伴の曲の行。</summary>
    private readonly IReadOnlyDictionary<(int SeriesId, string MNoDetail), BgmCue> _cueByKey;

    /// <summary>仮 M 番号でメニューも無い劇伴の曲 → 収録盤のトラック名（品番 → トラック番号 → 枝番の順で最初に見つかったもの）。</summary>
    private readonly IReadOnlyDictionary<(int SeriesId, string MNoDetail), string> _fallbackTitleByKey;

    public BgmCueRefResolver(BuildContext ctx)
    {
        _ctx = ctx;
        var cueByKey = new Dictionary<(int, string), BgmCue>();
        foreach (var key in ctx.BgmCueSourcePartsByCue.Keys)
        {
            if (ctx.BgmCuesBySeries.TryGetValue(key.SeriesId, out var cues)
                && cues.FirstOrDefault(c => string.Equals(c.MNoDetail, key.MNoDetail, StringComparison.Ordinal)) is { } cue)
            {
                cueByKey[key] = cue;
            }
        }
        _cueByKey = cueByKey;

        // 仮 M 番号でメニューの無い曲だけ、収録盤のトラック名を拾っておく（劇伴詳細のカードの見出しと同じ考え方）。
        var needFallback = cueByKey
            .Where(kv => kv.Value.IsTempMNo && string.IsNullOrEmpty(kv.Value.MenuTitle))
            .Select(kv => kv.Key)
            .ToHashSet();
        var fallback = new Dictionary<(int, string), string>();
        if (needFallback.Count > 0)
        {
            foreach (var (_, tracks) in ctx.TracksByCatalogNo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                foreach (var t in tracks.OrderBy(t => t.TrackNo).ThenBy(t => t.SubOrder))
                {
                    if (t.BgmSeriesId is not int sid || t.BgmMNoDetail is not string mno) continue;
                    if (string.IsNullOrEmpty(t.TrackTitleOverride)) continue;
                    var key = (sid, mno);
                    if (needFallback.Contains(key) && !fallback.ContainsKey(key)) fallback[key] = t.TrackTitleOverride!;
                }
            }
        }
        _fallbackTitleByKey = fallback;
    }

    /// <summary>
    /// 劇伴の曲の表記とリンク先を返す。シリーズか劇伴の曲が引けなければ null。
    /// </summary>
    public BgmCueRef? Describe(int seriesId, string mNoDetail)
    {
        if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) return null;
        if (!_cueByKey.TryGetValue((seriesId, mNoDetail), out var cue)) return null;

        string label;
        if (!cue.IsTempMNo)
        {
            label = string.IsNullOrEmpty(cue.MenuTitle) ? cue.MNoDetail : $"{cue.MNoDetail} {cue.MenuTitle}";
        }
        else if (!string.IsNullOrEmpty(cue.MenuTitle))
        {
            label = cue.MenuTitle!;
        }
        else
        {
            label = _fallbackTitleByKey.TryGetValue((seriesId, mNoDetail), out var t) ? t : "(Mナンバー不明)";
        }

        return new BgmCueRef(
            seriesId,
            series.Title,
            label,
            PathUtil.BgmCueAnchorUrl(series.Slug, mNoDetail));
    }
}

/// <summary>劇伴の曲を他のページから指すときの表記とリンク先。</summary>
/// <param name="SeriesId">劇伴のシリーズ。</param>
/// <param name="SeriesTitle">シリーズの正式タイトル。</param>
/// <param name="CueLabel">劇伴の曲の表記（「M 番号 メニュー」など。<see cref="BgmCueRefResolver"/> 参照）。</param>
/// <param name="Url">劇伴詳細のその曲のカードへのアンカー URL。</param>
public sealed record BgmCueRef(int SeriesId, string SeriesTitle, string CueLabel, string Url);
