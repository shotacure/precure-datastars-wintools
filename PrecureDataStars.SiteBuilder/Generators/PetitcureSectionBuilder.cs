using System.Net;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// ショートアニメ「ぷちきゅあ」（<see cref="PetitcureIndex"/>）の表示の行を組み立てる。
/// 作品詳細の「エピソード一覧」「スタッフ」と、人物・企業の詳細の「ぷちきゅあ」の節で使う。
/// <para>
/// 役職は概要欄の役職名を「・」で分けた文字で出し、役職詳細へはリンクしない（ぷちきゅあは役職の一覧・集計の対象外）。
/// 名前は名義につながっていれば人物・企業の詳細へリンクし（表記は名義のもの）、つながっていなければ書かれたままの文字で出す。
/// ここで組んだ行はどの数にも入れない（担当量の札・OGP カード・初参加などは本編クレジットの関与索引だけから作る）。
/// </para>
/// 依存は読み取り専用の <see cref="BuildContext"/> だけで、並列レンダリングから呼んでも安全。
/// </summary>
internal static class PetitcureSectionBuilder
{
    /// <summary>作品にぷちきゅあの話があるか。</summary>
    internal static bool HasEpisodes(BuildContext ctx, int seriesId)
        => ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps) && eps.Count > 0;

    /// <summary>
    /// 作品詳細の「エピソード一覧」の行。1 本の動画を 1 行にする（1 本に 2 話入る動画は「#1・#2」の 1 行）。
    /// 動画は話ごとに通常の動画を先に、同じ種類なら早く公開されたものを使う。並びは最初の話数の順。
    /// </summary>
    internal static IReadOnlyList<PetitcureEpisodeRow> BuildEpisodeRows(BuildContext ctx, int seriesId)
    {
        if (!ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return Array.Empty<PetitcureEpisodeRow>();
        var rows = new List<PetitcureEpisodeRow>(eps.Count);
        var doneVideos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in eps)
        {
            var video = ctx.Petitcure.VideosByEpisode.TryGetValue(e.PetitcureEpisodeId, out var videos)
                ? videos.OrderBy(v => v.VideoKind == "MAIN" ? 0 : 1).ThenBy(v => v.PublishedAt).FirstOrDefault()
                : null;
            if (video is not null && !doneVideos.Add(video.VideoId)) continue;

            // 同じ動画に入る話（話数の順）。動画が無い話はその話だけ。
            var together = video is not null && ctx.Petitcure.EpisodesByVideo.TryGetValue(video.VideoId, out var inVideo)
                ? inVideo
                : (IReadOnlyList<PetitcureEpisode>)new[] { e };
            rows.Add(new PetitcureEpisodeRow
            {
                EpLabel = string.Join("・", together.Select(x => $"#{x.EpNo}")),
                // TV の作品詳細の一覧と同じく日付だけ（時刻は出さない）。
                PublishedLabel = video is null ? "" : JpDateFormat.DotDate(video.PublishedAt),
                DurationLabel = video?.DurationSeconds is uint sec ? DurationLabel(sec) : "",
                VideoUrl = video is null ? "" : $"https://www.youtube.com/watch?v={video.VideoId}",
                Decoration = string.Join(" ", together.Select(x => x.TitleDecoration).Where(d => !string.IsNullOrEmpty(d))),
            });
        }
        return rows;
    }

    /// <summary>
    /// 作品詳細の基本情報の「配信」の欄（「毎週木曜 18:30」）。話の通常の動画の公開の時刻のうち、いちばん多い曜日と時刻（分まで）を使う
    /// （遅れて公開された回が少しあっても、ふだんの配信の枠を出す）。動画が無ければ空。
    /// </summary>
    internal static string ScheduleLabel(BuildContext ctx, int seriesId)
    {
        if (!ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return "";
        var slot = eps
            .Where(e => ctx.Petitcure.VideosByEpisode.ContainsKey(e.PetitcureEpisodeId))
            .SelectMany(e => ctx.Petitcure.VideosByEpisode[e.PetitcureEpisodeId])
            .Where(v => v.VideoKind == "MAIN")
            .DistinctBy(v => v.VideoId)
            .GroupBy(v => (v.PublishedAt.DayOfWeek, v.PublishedAt.Hour, v.PublishedAt.Minute))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (slot is null) return "";
        string[] dayNames = { "日", "月", "火", "水", "木", "金", "土" };
        return $"毎週{dayNames[(int)slot.Key.DayOfWeek]}曜 {slot.Key.Hour}:{slot.Key.Minute:00}";
    }

    /// <summary>
    /// ぷちきゅあのメインスタッフに出す役職（概要欄の役職名を「・」で分けたもの）と札の色。並びと色は、TV の作品の
    /// メインスタッフの同じ系統の役職（シリーズ構成・シリーズディレクター・デザイン・音楽）にそろえる。
    /// </summary>
    internal static readonly IReadOnlyList<(string Label, string Color)> KeyStaffSpecs = new[]
    {
        ("シリーズ構成", "#3b82c4"),
        ("ディレクター", "#e91e63"),
        ("コンセプトアート", "#4ca36b"),
        ("音楽", "#e67832"),
    };

    /// <summary>
    /// 作品詳細と作品一覧のカードのメインスタッフ（<see cref="KeyStaffSpecs"/> の役職ごと）。人物の名義につながった名前だけを、
    /// 最初に出た話 → その回の並び順の順に並べる。全話を担当していない人には担当した話数を添える。役職詳細へはリンクしない。
    /// </summary>
    internal static IReadOnlyList<PetitcureKeyStaffLine> BuildKeyStaff(BuildContext ctx, int seriesId)
    {
        if (!ctx.Petitcure.StaffBySeries.TryGetValue(seriesId, out var staff)) return Array.Empty<PetitcureKeyStaffLine>();
        int episodeCount = ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps) ? eps.Count : 0;
        var lines = new List<PetitcureKeyStaffLine>();
        foreach (var (label, color) in KeyStaffSpecs)
        {
            var members = staff
                .Where(r => RoleParts(r.RoleLabelText).Contains(label)
                    && r.PersonAliasId is int pa && ctx.PersonIdByAlias.ContainsKey(pa) && ctx.PersonAliasById.ContainsKey(pa))
                .GroupBy(r => ctx.PersonIdByAlias[r.PersonAliasId!.Value])
                .Select(g =>
                {
                    var epNos = g.SelectMany(ctx.Petitcure.EpisodeNosOf).Distinct().ToList();
                    bool all = episodeCount > 0 && epNos.Count == episodeCount;
                    return new PetitcureKeyStaffMember
                    {
                        PersonId = g.Key,
                        Name = ctx.PersonAliasById[g.First().PersonAliasId!.Value].GetDisplayName(),
                        AffiliationLabel = g.Select(r => r.AffiliationText).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? "",
                        RangeLabel = all ? "" : EpisodeRangeCompressor.Compress(epNos),
                    };
                })
                .ToList();
            if (members.Count > 0) lines.Add(new PetitcureKeyStaffLine { RoleLabel = label, Color = color, Members = members });
        }
        return lines;
    }

    /// <summary>作品のいちばん新しい話の動画（作品詳細のエピソード一覧の頭に 1 本だけ埋め込む）。無ければ null。</summary>
    internal static PetitcureVideo? LatestVideo(BuildContext ctx, int seriesId)
    {
        if (!ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        return eps
            .Where(e => ctx.Petitcure.VideosByEpisode.ContainsKey(e.PetitcureEpisodeId))
            .SelectMany(e => ctx.Petitcure.VideosByEpisode[e.PetitcureEpisodeId])
            .Where(v => v.VideoKind == "MAIN")
            .OrderByDescending(v => v.PublishedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// 作品詳細の「スタッフ」の行。役職ごとに 1 行で、名前と担当した話数を並べる。役職は概要欄の役職名を「・」で分けたもの
    /// （「コンテ・演出」は「コンテ」と「演出」の 2 つの役職に入る。<see cref="RoleParts"/>）。
    /// 役職の並びは、各動画の概要欄の中の位置（並び順 ÷ 行数）の平均の順。名前の並びは、最初に出た話 → その回の並び順の順。
    /// 作品のすべての話を担当した名前は話数を添えない。
    /// </summary>
    internal static IReadOnlyList<PetitcureStaffLine> BuildStaffLines(BuildContext ctx, int seriesId)
    {
        if (!ctx.Petitcure.StaffBySeries.TryGetValue(seriesId, out var staff)) return Array.Empty<PetitcureStaffLine>();
        int episodeCount = ctx.Petitcure.EpisodesBySeries.TryGetValue(seriesId, out var eps) ? eps.Count : 0;

        // 動画ごとの行数（概要欄の中の相対の位置を出すため）。
        var rowsPerVideo = staff.GroupBy(s => s.VideoId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(s => (int)s.DisplayOrder), StringComparer.Ordinal);

        var lines = new List<(double Position, PetitcureStaffLine Line)>();
        // 1 行を役職ごとに分ける（並びは元の行の順のまま）。
        var expanded = staff.SelectMany(s => RoleParts(s.RoleLabelText).Select(role => (Role: role, Row: s))).ToList();
        foreach (var byRole in expanded.GroupBy(x => x.Role, StringComparer.Ordinal))
        {
            double position = byRole.Average(x => rowsPerVideo[x.Row.VideoId] <= 1 ? 0.0 : (x.Row.DisplayOrder - 1.0) / (rowsPerVideo[x.Row.VideoId] - 1.0));
            var members = new List<PetitcureStaffMember>();
            // staff は動画の公開の時刻 → 並び順の順なので、GroupBy は最初に出た順を保つ。
            foreach (var byName in byRole.Select(x => x.Row).GroupBy(NameKey, StringComparer.Ordinal))
            {
                var first = byName.First();
                var epNos = byName.SelectMany(ctx.Petitcure.EpisodeNosOf).Distinct().ToList();
                bool all = episodeCount > 0 && epNos.Count == episodeCount;
                members.Add(new PetitcureStaffMember
                {
                    NameHtml = NameHtml(ctx, first),
                    AffiliationLabel = byName.Select(s => s.AffiliationText).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? "",
                    RangeLabel = all ? "" : EpisodeRangeCompressor.Compress(epNos),
                });
            }
            lines.Add((position, new PetitcureStaffLine { RoleLabel = byRole.Key, Members = members }));
        }
        return lines.OrderBy(x => x.Position).Select(x => x.Line).ToList();
    }

    /// <summary>
    /// 人物・企業/団体の詳細の役職の欄に混ぜる、ぷちきゅあの行。作品ごと（作品の開始日の順）に、役職（概要欄の役職名を「・」で分けたもの）ごとの
    /// 担当した話数を並べる。
    /// 数（担当量の札など）には入れない。
    /// </summary>
    /// <param name="ctx">ビルド共有コンテキスト。</param>
    /// <param name="rows">その名義（人物の名義・企業の名義・所属の企業の名義）のスタッフの行。</param>
    internal static IReadOnlyList<PetitcureInvolvementRow> BuildInvolvementRows(BuildContext ctx, IEnumerable<PetitcureVideoStaff> rows)
    {
        var result = new List<PetitcureInvolvementRow>();
        foreach (var bySeries in rows
            .Select(r => (Row: r, SeriesId: ctx.Petitcure.SeriesIdOf(r)))
            .Where(x => x.SeriesId.HasValue && ctx.SeriesById.ContainsKey(x.SeriesId.Value))
            .GroupBy(x => x.SeriesId!.Value)
            .OrderBy(g => ctx.SeriesById[g.Key].StartDate))
        {
            var series = ctx.SeriesById[bySeries.Key];
            int episodeCount = ctx.Petitcure.EpisodesBySeries.TryGetValue(bySeries.Key, out var eps) ? eps.Count : 0;
            var roles = bySeries
                .SelectMany(x => RoleParts(x.Row.RoleLabelText).Select(role => (Role: role, x.Row)))
                .GroupBy(x => x.Role, StringComparer.Ordinal)
                .Select(g =>
                {
                    var epNos = g.SelectMany(x => ctx.Petitcure.EpisodeNosOf(x.Row)).Distinct().ToList();
                    bool all = episodeCount > 0 && epNos.Count == episodeCount;
                    return new PetitcureInvolvementRole
                    {
                        RoleLabel = g.Key,
                        RangeLabel = all ? "全話" : EpisodeRangeCompressor.Compress(epNos),
                        IsAllEpisodes = all,
                        FirstEpNo = epNos.Count == 0 ? int.MaxValue : epNos.Min(),
                        EpisodeCount = epNos.Count,
                    };
                })
                // 担当の多い役職を先に、同じなら早く担当した役職を先に。
                .OrderByDescending(r => r.EpisodeCount).ThenBy(r => r.FirstEpNo).ThenBy(r => r.RoleLabel, StringComparer.Ordinal)
                .ToList();
            result.Add(new PetitcureInvolvementRow
            {
                SeriesId = series.SeriesId,
                SeriesTitle = series.Title,
                SeriesUrl = PathUtil.SeriesUrl(series.Slug),
                Roles = roles,
            });
        }
        return result;
    }

    /// <summary>
    /// 人物・企業/団体の詳細の本編クレジットの役職の欄（<see cref="InvolvementGroup"/>）に、ぷちきゅあの行を混ぜる。
    /// 役職（概要欄の役職名を「・」で分けたもの）が欄の役職名と同じ字なら、その欄の作品のカードに作品の開始日の順で差し込む。
    /// 同じ名前の欄が無ければ、同じ形の欄を後ろに足す（役職詳細へのリンクは持たない）。
    /// 欄の担当の数（<see cref="InvolvementGroup.EpisodeCount"/> など）・初担当・年表には入れない。
    /// 渡された欄は書き換えず、差し込む欄だけ写しを作って返す（元の欄は年表・説明文の組み立てにそのまま使われる）。
    /// </summary>
    /// <param name="ctx">ビルド共有コンテキスト。</param>
    /// <param name="groups">本編クレジットから組んだ役職の欄（並びはそのまま）。</param>
    /// <param name="rows">その名義のぷちきゅあのスタッフの行。</param>
    internal static IReadOnlyList<InvolvementGroup> MergeIntoGroups(
        BuildContext ctx, IReadOnlyList<InvolvementGroup> groups, IEnumerable<PetitcureVideoStaff> rows)
    {
        var involvements = BuildInvolvementRows(ctx, rows);
        if (involvements.Count == 0) return groups;

        var seriesBySlug = ctx.SeriesById.Values.GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        DateOnly StartOf(InvolvementSeriesRow r)
            => seriesBySlug.TryGetValue(r.SeriesSlug, out var series) ? series.StartDate : DateOnly.MaxValue;

        var result = groups.ToList();
        var copied = new HashSet<InvolvementGroup>(ReferenceEqualityComparer.Instance);
        foreach (var inv in involvements)
        {
            var series = ctx.SeriesById[inv.SeriesId];
            foreach (var role in inv.Roles)
            {
                var row = new InvolvementSeriesRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    IsAllEpisodes = role.IsAllEpisodes,
                    RangeLabel = role.IsAllEpisodes ? "" : role.RangeLabel,
                };
                // 声の出演の欄（役ごとのくくりを持つ欄）には混ぜない。
                int index = result.FindIndex(g => g.CharacterSections.Count == 0
                    && string.Equals(g.RoleLabel, role.RoleLabel, StringComparison.Ordinal));
                InvolvementGroup group;
                if (index < 0)
                {
                    group = new InvolvementGroup { RoleCode = "", RoleLabel = role.RoleLabel, RoleUrl = "" };
                    result.Add(group);
                    copied.Add(group);
                }
                else
                {
                    group = result[index];
                    if (copied.Add(group))
                    {
                        group = group.ShallowCopy();
                        copied.Add(group);
                        result[index] = group;
                    }
                }
                var seriesRows = group.SeriesRows.ToList();
                int at = seriesRows.FindIndex(r => StartOf(r) > series.StartDate);
                seriesRows.Insert(at < 0 ? seriesRows.Count : at, row);
                group.SeriesRows = seriesRows;
            }
        }
        return result;
    }

    /// <summary>
    /// 概要欄の役職名を役職に分ける。「・」でつないだ役職名（「コンテ・演出」「背景美術・編集・音響」「音響・MA」）は、
    /// つないだ役職のそれぞれに分ける。DB の文字は書かれたままにしておく。
    /// </summary>
    internal static IEnumerable<string> RoleParts(string roleLabelText)
        => roleLabelText.Split('・', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>名前のまとめの鍵：名義につながっていれば名義、つながっていなければ書かれたままの文字。</summary>
    private static string NameKey(PetitcureVideoStaff s)
        => s.PersonAliasId is int pa ? $"P{pa}" : s.CompanyAliasId is int ca ? $"C{ca}" : $"T{s.NameText}";

    /// <summary>
    /// 名前の HTML。人物・企業の名義につながっていれば詳細ページへのリンク（表記は名義）、
    /// つながっていなければ書かれたままの文字（リンクの無い span。下線を出さない）。
    /// </summary>
    private static string NameHtml(BuildContext ctx, PetitcureVideoStaff s)
    {
        if (s.PersonAliasId is int pa && ctx.PersonAliasById.TryGetValue(pa, out var alias) && ctx.PersonIdByAlias.TryGetValue(pa, out int personId))
        {
            return $"<a class=\"key-staff-name\" href=\"{WebUtility.HtmlEncode(PathUtil.PersonUrl(personId))}\">{WebUtility.HtmlEncode(alias.Name)}</a>";
        }
        if (s.CompanyAliasId is int ca && ctx.CompanyAliasById.TryGetValue(ca, out var companyAlias) && companyAlias.CompanyId > 0)
        {
            return $"<a class=\"key-staff-name\" href=\"{WebUtility.HtmlEncode(PathUtil.CompanyUrl(companyAlias.CompanyId))}\">{WebUtility.HtmlEncode(companyAlias.Name)}</a>";
        }
        return $"<span class=\"staff-name\">{WebUtility.HtmlEncode(s.NameText)}</span>";
    }

    /// <summary>長さの表示（「50秒」「1分29秒」「1時間2分40秒」）。</summary>
    private static string DurationLabel(uint seconds)
    {
        uint h = seconds / 3600, m = seconds % 3600 / 60, sec = seconds % 60;
        if (h > 0) return $"{h}時間{m}分{sec}秒";
        return m > 0 ? $"{m}分{sec}秒" : $"{sec}秒";
    }
}

/// <summary>作品詳細のぷちきゅあの話の 1 行。</summary>
internal sealed class PetitcureEpisodeRow
{
    /// <summary>話数（「#3」。1 本に 2 話入る動画は「#1・#2」）。</summary>
    public string EpLabel { get; set; } = "";
    /// <summary>公開の日付（「2025.4.3」）。動画が無ければ空。</summary>
    public string PublishedLabel { get; set; } = "";
    /// <summary>長さ（「1分29秒」）。分からなければ空。</summary>
    public string DurationLabel { get; set; } = "";
    /// <summary>YouTube の動画の URL。動画が無ければ空。</summary>
    public string VideoUrl { get; set; } = "";
    /// <summary>題に付く飾りの言葉。無ければ空。</summary>
    public string Decoration { get; set; } = "";
}

/// <summary>ぷちきゅあのメインスタッフの 1 行（役職ごと）。</summary>
internal sealed class PetitcureKeyStaffLine
{
    public string RoleLabel { get; set; } = "";
    /// <summary>札の色（#rrggbb）。</summary>
    public string Color { get; set; } = "";
    public IReadOnlyList<PetitcureKeyStaffMember> Members { get; set; } = Array.Empty<PetitcureKeyStaffMember>();
}

/// <summary>ぷちきゅあのメインスタッフの 1 名（人物の名義につながった名前だけ）。</summary>
internal sealed class PetitcureKeyStaffMember
{
    public int PersonId { get; set; }
    /// <summary>概要欄の名前につないだ名義の表示名。</summary>
    public string Name { get; set; } = "";
    /// <summary>所属（書かれたまま）。無ければ空。</summary>
    public string AffiliationLabel { get; set; } = "";
    /// <summary>担当した話数（「#1～27」）。全話なら空。</summary>
    public string RangeLabel { get; set; } = "";
}

/// <summary>作品詳細のぷちきゅあのスタッフの 1 行（役職ごと）。</summary>
internal sealed class PetitcureStaffLine
{
    /// <summary>役職名（概要欄の役職名を「・」で分けたもの）。</summary>
    public string RoleLabel { get; set; } = "";
    public IReadOnlyList<PetitcureStaffMember> Members { get; set; } = Array.Empty<PetitcureStaffMember>();
}

/// <summary>ぷちきゅあのスタッフの行の 1 名。</summary>
internal sealed class PetitcureStaffMember
{
    /// <summary>名前の HTML（リンクか、リンクの無い span）。</summary>
    public string NameHtml { get; set; } = "";
    /// <summary>所属（書かれたまま。括弧は除く）。無ければ空。</summary>
    public string AffiliationLabel { get; set; } = "";
    /// <summary>担当した話数（「#3～5, 8」）。全話なら空。</summary>
    public string RangeLabel { get; set; } = "";
}

/// <summary>人物・企業の詳細の「ぷちきゅあ」の節の作品ごとの行。</summary>
internal sealed class PetitcureInvolvementRow
{
    public int SeriesId { get; set; }
    public string SeriesTitle { get; set; } = "";
    public string SeriesUrl { get; set; } = "";
    public IReadOnlyList<PetitcureInvolvementRole> Roles { get; set; } = Array.Empty<PetitcureInvolvementRole>();
}

/// <summary>ぷちきゅあの作品ごとの行の、役職ごとの担当。</summary>
internal sealed class PetitcureInvolvementRole
{
    /// <summary>役職名（概要欄の役職名を「・」で分けたもの）。</summary>
    public string RoleLabel { get; set; } = "";
    /// <summary>担当した話数（「#3～5, 8」か「全話」）。</summary>
    public string RangeLabel { get; set; } = "";
    /// <summary>作品のすべての話を担当したか。</summary>
    public bool IsAllEpisodes { get; set; }
    /// <summary>並べ替え用：最初に担当した話数。</summary>
    public int FirstEpNo { get; set; }
    /// <summary>並べ替え用：担当した話の数（表示には使わない）。</summary>
    public int EpisodeCount { get; set; }
}
