using System.Text;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 劇伴の曲が使っている曲（<c>bgm_cue_source_parts</c>）の一覧 HTML を組み立てる。
/// 劇伴詳細（<c>/bgms/{作品}/</c>）の曲のカードと、商品詳細のトラックの枠で同じものを出す。
/// <para>
/// 一覧は開け閉めの枠（<c>&lt;details class="bgm-cue-sources"&gt;</c>、見出し「使われている曲（N 曲）」、既定は閉じる）に
/// 曲の中で流れる順の番号付きリスト（<c>&lt;ol class="medley-parts bgm-cue-source-parts"&gt;</c>。曲詳細のメドレーの「収録曲」と同じ見た目）を包む。
/// </para>
/// <list type="bullet">
///   <item><description>歌：曲名（曲詳細へのリンク）と、歌の作詞・作曲・編曲（商品詳細の歌のトラックと同じバッジの行。バッジは役職詳細へリンク）。</description></item>
///   <item><description>曲（著作物）：単独のページは持たないので、曲名はリンクせず、その場に作家（<c>musical_work_credits</c>）を出す。
///     名義は人物詳細へリンクし、バッジは劇伴の作曲・編曲と同じくリンクしない（役職詳細の担当曲数は歌だけを数えるため）。</description></item>
/// </list>
/// 表引きは <see cref="BuildContext"/> の辞書と <see cref="TrackCreditHtmlBuilder"/> だけで、DB には触れない
/// （読み取りだけなので並列レンダリング中に呼んでよい）。
/// </summary>
public sealed class BgmCueSourcePartsHtmlBuilder
{
    private readonly BuildContext _ctx;
    private readonly TrackCreditHtmlBuilder _creditHtml;

    public BgmCueSourcePartsHtmlBuilder(BuildContext ctx, TrackCreditHtmlBuilder creditHtml)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _creditHtml = creditHtml ?? throw new ArgumentNullException(nameof(creditHtml));
    }

    /// <summary>
    /// 劇伴の曲が使っている曲の一覧 HTML（開け閉めの枠ごと）を返す。使われている曲が無ければ空文字。
    /// </summary>
    public string BuildHtml(int seriesId, string mNoDetail)
    {
        if (!_ctx.BgmCueSourcePartsByCue.TryGetValue((seriesId, mNoDetail), out var parts) || parts.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append("<details class=\"bgm-cue-sources\">")
          .Append($"<summary>使われている曲（{parts.Count} 曲）</summary>")
          .Append("<ol class=\"medley-parts bgm-cue-source-parts\">");
        foreach (var p in parts)
        {
            sb.Append("<li>");
            if (p.SourceSongId is int songId)
                AppendSong(sb, songId);
            else if (p.SourceWorkId is int workId)
                AppendWork(sb, workId);
            else
                sb.Append("<span class=\"medley-part-title muted\">（曲名不明）</span>");
            sb.Append("</li>");
        }
        sb.Append("</ol></details>");
        return sb.ToString();
    }

    /// <summary>歌 1 曲分：曲名（曲詳細へのリンク）と作詞・作曲・編曲のバッジの行。</summary>
    private void AppendSong(StringBuilder sb, int songId)
    {
        if (!_ctx.SongById.TryGetValue(songId, out var song))
        {
            sb.Append("<span class=\"medley-part-title muted\">（曲名不明）</span>");
            return;
        }
        // 論理削除された歌は曲詳細が無いので、曲名だけを平文で出す。
        sb.Append(song.IsDeleted
            ? $"<span class=\"medley-part-title\">{TrackCreditHtmlBuilder.Escape(song.Title)}</span>"
            : $"<a class=\"medley-part-title\" href=\"{PathUtil.SongUrl(song.SongId)}\">{TrackCreditHtmlBuilder.Escape(song.Title)}</a>");

        string creditsHtml = _creditHtml.BuildMergedRoleSegmentsHtml(new (string, string, string, string?)[]
        {
            ("LYRICS",      "作詞", _creditHtml.BuildSongCreditNamesHtml(song, "LYRICS"),      _creditHtml.SongCreditLabelText(song, "LYRICS")),
            ("COMPOSITION", "作曲", _creditHtml.BuildSongCreditNamesHtml(song, "COMPOSITION"), _creditHtml.SongCreditLabelText(song, "COMPOSITION")),
            ("ARRANGEMENT", "編曲", _creditHtml.BuildSongCreditNamesHtml(song, "ARRANGEMENT"), _creditHtml.SongCreditLabelText(song, "ARRANGEMENT")),
            (SongCreditRoles.MedleyArrangement, "メドレー編曲", _creditHtml.BuildSongCreditNamesHtml(song, SongCreditRoles.MedleyArrangement), _creditHtml.SongCreditLabelText(song, SongCreditRoles.MedleyArrangement)),
        });
        if (creditsHtml.Length > 0)
            sb.Append("<div class=\"track-meta-line\">").Append(creditsHtml).Append("</div>");
    }

    /// <summary>曲（著作物）1 曲分：曲名（リンクなし）と作家のバッジの行。</summary>
    private void AppendWork(StringBuilder sb, int workId)
    {
        if (!_ctx.MusicalWorkById.TryGetValue(workId, out var work))
        {
            sb.Append("<span class=\"medley-part-title muted\">（曲名不明）</span>");
            return;
        }
        sb.Append($"<span class=\"medley-part-title\">{TrackCreditHtmlBuilder.Escape(work.Title)}</span>");

        string creditsHtml = BuildWorkCreditsHtml(workId);
        if (creditsHtml.Length > 0)
            sb.Append("<div class=\"track-meta-line\">").Append(creditsHtml).Append("</div>");
    }

    /// <summary>
    /// 曲（著作物）の作家を「役職バッジ + 連名」のセグメントの列にする（並びは作詞 → 作曲 → 編曲 → その他）。
    /// 連名の区切りは preceding_separator（空なら「、」）。隣り合う役職で連名が同じならバッジを並べて名義を 1 回だけ出す。
    /// </summary>
    private string BuildWorkCreditsHtml(int workId)
    {
        if (!_ctx.MusicalWorkCreditsByWork.TryGetValue(workId, out var credits) || credits.Count == 0) return "";

        var entries = new List<(string RoleCode, string FallbackLabel, string NamesHtml, string? LabelText)>();
        foreach (var g in credits.GroupBy(c => c.CreditRole, StringComparer.Ordinal))
        {
            var names = new StringBuilder();
            int i = 0;
            foreach (var c in g.OrderBy(c => c.CreditSeq))
            {
                if (i++ > 0)
                    names.Append(TrackCreditHtmlBuilder.Escape(string.IsNullOrEmpty(c.PrecedingSeparator) ? "、" : c.PrecedingSeparator));
                names.Append(_creditHtml.BuildPersonAliasLinkHtml(c.PersonAliasId));
            }
            entries.Add((g.Key, g.Key, names.ToString(), null));
        }
        return _creditHtml.BuildMergedRoleSegmentsHtml(entries, linkable: false);
    }
}
