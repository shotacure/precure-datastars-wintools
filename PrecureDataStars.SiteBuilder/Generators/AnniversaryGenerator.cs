using PrecureDataStars.Data.Db;
using PrecureDataStars.SiteBuilder.Data;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 日付別の記念日ページ <c>/anniversary/{MM-DD}/</c>（366 日ぶん）と、その索引 <c>/anniversary/</c> の生成。
///
/// <para>
/// 「◯年前の今日、プリキュアでは何があったか」を 1 ページに集約する。載せる出来事は
/// <see cref="AnniversaryDataBuilder"/> が集めた 4 種類（エピソード放送 / 映画公開 /
/// キャラクター誕生日 / 人物誕生日）で、ホームのカレンダーと同じ集合を参照する。
/// </para>
/// <para>
/// 出来事が 1 件も無い日（実データで 12 日ある）も含めて 366 日すべてを生成する。
/// 前日・翌日ナビが途切れないこと、毎年同じ URL が生き続けること、
/// 将来データが増えたときに URL が後から生えないことを優先した判断。
/// </para>
/// <para>
/// 閏日（2 月 29 日）を含めるため、日付の総当たりには閏年である 2024 年を暦の基準に使う。
/// ページ自体は年を持たないので、この年は日付の存在判定にしか使わない。
/// </para>
/// </summary>
public sealed class AnniversaryGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    /// <summary>366 日の総当たりに使う暦の基準年（閏年）。ページ自体は年を持たない。</summary>
    private const int CalendarYear = 2024;

    public AnniversaryGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating anniversary");

        var entries = await AnniversaryDataBuilder.BuildAsync(_ctx, _factory, ct).ConfigureAwait(false);
        var byDate = entries
            .GroupBy(e => (e.Month, e.Day))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AnniversaryEntry>)g.ToList());

        var monthSections = new List<MonthSection>();
        int writtenDays = 0;

        for (int month = 1; month <= 12; month++)
        {
            var dayLinks = new List<DayLink>();
            int daysInMonth = DateTime.DaysInMonth(CalendarYear, month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                byDate.TryGetValue((month, day), out var dayEntries);
                dayEntries ??= Array.Empty<AnniversaryEntry>();

                RenderDayPage(month, day, dayEntries);
                writtenDays++;

                // 索引のカレンダーは種別ごとの件数チップを出すので、内訳まで持たせる。
                dayLinks.Add(new DayLink
                {
                    Day = day,
                    Url = PathUtil.AnniversaryUrl(month, day),
                    Count = dayEntries.Count,
                    EpisodeCount = dayEntries.Count(e => e.Kind == "ep"),
                    MovieCount = dayEntries.Count(e => e.Kind == "mv"),
                    BirthdayCount = dayEntries.Count(e => e.Kind == "cb" || e.Kind == "pb")
                });
            }

            monthSections.Add(new MonthSection { Month = month, Label = $"{month}月", Days = dayLinks });
        }

        RenderIndexPage(monthSections, entries.Count);
        _ctx.Logger.Success($"anniversary: {writtenDays} 日 + 索引 1 ページ");
    }

    // ════════════════════ 日付別ページ ════════════════════

    /// <summary>1 日ぶんの記念日ページを書き出す。</summary>
    private void RenderDayPage(int month, int day, IReadOnlyList<AnniversaryEntry> dayEntries)
    {
        string url = PathUtil.AnniversaryUrl(month, day);
        string dateLabel = $"{month}月{day}日";

        // 出来事は種別ごとに束ねる。エピソードと映画は「古い順」に並べて、
        // その日付でシリーズの歴史がどう積み重なってきたかを縦に読めるようにする。
        var episodes = dayEntries.Where(e => e.Kind == "ep").OrderBy(e => e.Year).ThenBy(e => e.EpisodeNo).ToList();
        var movies = dayEntries.Where(e => e.Kind == "mv").OrderBy(e => e.Year).ToList();
        var characterBirthdays = dayEntries.Where(e => e.Kind == "cb").ToList();
        var personBirthdays = dayEntries.Where(e => e.Kind == "pb").ToList();

        var content = new DayContentModel
        {
            DateLabel = dateLabel,
            Episodes = episodes.Select(e => new EpisodeRow
            {
                Year = e.Year ?? 0,
                SeriesTitle = e.SeriesTitle,
                SeriesUrl = PathUtil.SeriesUrl(e.SeriesSlug),
                EpisodeNo = e.EpisodeNo,
                EpisodeTitle = e.EpisodeTitle,
                EpisodeUrl = e.EpisodeUrl,
                IsFirstEpisode = e.IsFirstEpisode,
                IsLastEpisode = e.IsLastEpisode,
                SubtitleGuarded = !string.IsNullOrEmpty(e.RevealAtIso)
            }).ToArray(),
            Movies = movies.Select(e => new MovieRow
            {
                Year = e.Year ?? 0,
                Title = e.SeriesTitle,
                Url = e.SeriesUrl
            }).ToArray(),
            CharacterBirthdays = characterBirthdays.Select(e => new BirthdayRow
            {
                Name = e.CharacterDisplayName,
                SubName = string.Equals(e.CharacterDisplayName, e.CharacterName, StringComparison.Ordinal) ? "" : e.CharacterName,
                Url = e.CharacterUrl,
                SeriesTitle = e.SeriesTitle,
                SeriesUrl = e.SeriesUrl,
                KeyColorBackground = e.KeyColorBackground,
                KeyColorForeground = e.KeyColorForeground,
                KeyColorBorder = e.KeyColorBorder
            }).ToArray(),
            PersonBirthdays = personBirthdays.Select(e => new BirthdayRow
            {
                Name = e.PersonName,
                Url = e.PersonUrl,
                // 生年は公開設定が PUBLIC のときだけ入っている。
                SubName = e.BirthYear is { } y ? $"{y}年生まれ" : ""
            }).ToArray(),
            HasAnything = dayEntries.Count > 0
        };

        // 放送と公開は種別で分けず、古い順の 1 本の年表にまとめる。
        // その日付でシリーズの歴史がどう積み重なったかは、媒体をまたいで縦に読めた方が分かる。
        content.Timeline = content.Episodes
            .Select(e => new TimelineRow
            {
                Year = e.Year,
                Kind = "ep",
                SeriesTitle = e.SeriesTitle,
                Url = e.EpisodeUrl,
                DateLabel = $"{e.Year}.{month}.{day}",
                EpisodeNo = e.EpisodeNo,
                TitleDisplay = e.TitleDisplay,
                IsFirstEpisode = e.IsFirstEpisode,
                IsLastEpisode = e.IsLastEpisode
            })
            .Concat(content.Movies.Select(m => new TimelineRow
            {
                Year = m.Year,
                Kind = "mv",
                SeriesTitle = m.Title,
                Url = m.Url,
                DateLabel = $"{m.Year}.{month}.{day}"
            }))
            .OrderBy(t => t.Year)
            .ThenBy(t => t.EpisodeNo)
            .ToArray();

        // 前日・翌日は暦上の隣接日（12/31 の翌日は 1/1 に回る）。出来事の有無に関わらず全日ぶん
        // ページがあるので、ナビが行き止まりになることはない。
        var date = new DateOnly(CalendarYear, month, day);
        var prev = date.AddDays(-1);
        var next = date.AddDays(1);
        content.PrevUrl = PathUtil.AnniversaryUrl(prev.Month, prev.Day);
        content.PrevLabel = $"{prev.Month}月{prev.Day}日";
        content.NextUrl = PathUtil.AnniversaryUrl(next.Month, next.Day);
        content.NextLabel = $"{next.Month}月{next.Day}日";

        var layout = new LayoutModel
        {
            // 「◯月◯日のプリキュア」は日本語として据わりが悪いので、素直に出来事の面として名乗る。
            PageTitle = $"{dateLabel}のできごと",
            MetaDescription = BuildMetaDescription(dateLabel, episodes.Count, movies.Count, characterBirthdays.Count, personBirthdays.Count),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "記念日", Url = PathUtil.AnniversaryIndexUrl() },
                new BreadcrumbItem { Label = dateLabel, Url = "" }
            },
            OgCard = BuildDayOgCard(dateLabel, content.Episodes, content.Movies, content.CharacterBirthdays, content.PersonBirthdays)
        };

        _page.RenderAndWrite(url, "anniversary", "anniversary-day.sbn", content, layout);
    }

    /// <summary>日付別ページの meta description。件数の内訳をそのまま述べる。</summary>
    private static string BuildMetaDescription(string dateLabel, int episodes, int movies, int characterBirthdays, int personBirthdays)
    {
        var parts = new List<string>();
        if (episodes > 0) parts.Add($"エピソード{episodes}話の放送");
        if (movies > 0) parts.Add($"映画{movies}本の公開");
        if (characterBirthdays > 0) parts.Add($"キャラクター{characterBirthdays}人の誕生日");
        if (personBirthdays > 0) parts.Add($"クリエーター{personBirthdays}人の誕生日");

        return parts.Count == 0
            ? $"{dateLabel}にプリキュアで起きた出来事。この日付に該当する記録は現在のところ登録がありません。歴代シリーズの放送日・公開日・誕生日を日付から引ける記念日カレンダーです。"
            : $"{dateLabel}にプリキュアで起きた出来事。{string.Join("、", parts)}。歴代シリーズの放送日・公開日・誕生日を日付から引ける記念日カレンダーです。";
    }

    /// <summary>
    /// 日付別ページの OGP カード。
    /// 出来事そのものを年代順に並べる。件数のバッジは置かない——この日付で読み手が知りたいのは
    /// 「何件あったか」ではなく「何があったか」なので、限られた面積は出来事の中身に使う。
    /// 収まりきらない場合は最後の行を「ほか n件」に替えて、省いた事実があることを明示する。
    /// </summary>
    private static OgCardSpec BuildDayOgCard(
        string dateLabel,
        IReadOnlyList<EpisodeRow> episodes,
        IReadOnlyList<MovieRow> movies,
        IReadOnlyList<BirthdayRow> characterBirthdays,
        IReadOnlyList<BirthdayRow> personBirthdays)
    {
        // 出来事を年代順に 1 本の列へまとめる（誕生日は年を持たないので末尾へ回す）。
        var timeline = new List<OgCardFactLine>();
        timeline.AddRange(episodes
            .Concat<object>(movies)
            .Select(x => x switch
            {
                // TV エピソードは「作品名」と「話数・サブタイトル」を 2 段に割る。
                // 1 行に押し込むと長いサブタイトルが必ず切れるため。
                EpisodeRow e => (Year: e.Year, Line: new OgCardFactLine($"{e.Year}年", $"『{e.SeriesTitle}』")
                {
                    SubText = FormatEpisodeDetail(e)
                }),
                // 映画は作品名だけで公開だと分かるので「公開」は書かない。
                MovieRow m => (Year: m.Year, Line: new OgCardFactLine($"{m.Year}年", $"『{m.Title}』")),
                _ => (Year: 0, Line: new OgCardFactLine("", ""))
            })
            .OrderBy(x => x.Year)
            .Select(x => x.Line));

        foreach (var b in characterBirthdays)
            timeline.Add(new OgCardFactLine("誕生日", string.IsNullOrWhiteSpace(b.SeriesTitle) ? b.Name : $"{b.Name}（{b.SeriesTitle}）"));
        foreach (var b in personBirthdays)
            timeline.Add(new OgCardFactLine("誕生日", b.Name));

        // カードに積める行数はレンダラが余白から決めるが、生成側でも上限を設けて
        // 「その日の主だった出来事」に絞る。溢れる場合は残件数の行に替えて、隠した事実の存在を伝える。
        const int maxLines = 7;
        var facts = timeline.Count <= maxLines
            ? timeline
            : timeline.Take(maxLines - 1)
                .Append(new OgCardFactLine("", $"ほか {timeline.Count - (maxLines - 1)} 件"))
                .ToList();

        // 主役は日付。「◯月◯日のプリキュア」と言い換えず、日付を大きく置いて
        // 何があった日かは下の年表そのものに語らせる。
        return new OgCardSpec(Kicker: "", Title: dateLabel, Subtitle: "プリキュアのできごと")
        {
            Facts = facts
        };
    }

    /// <summary>
    /// カードの 2 段目に置く「話数・サブタイトル」。解禁前の話は題名を伏せる
    /// （カードは画像なのでサイト側のぼかしガードを効かせられない）。
    /// </summary>
    private static string FormatEpisodeDetail(EpisodeRow e)
        => e.SubtitleGuarded || string.IsNullOrWhiteSpace(e.EpisodeTitle)
            ? $"第{e.EpisodeNo}話"
            : $"第{e.EpisodeNo}話「{e.EpisodeTitle}」";

    // ════════════════════ 索引ページ ════════════════════

    /// <summary>月別に全日を並べた索引を書き出す。</summary>
    private void RenderIndexPage(IReadOnlyList<MonthSection> months, int totalEntries)
    {
        var content = new IndexContentModel { Months = months, TotalEntries = totalEntries };
        var layout = new LayoutModel
        {
            PageTitle = "プリキュア記念日カレンダー",
            MetaDescription = $"歴代プリキュアの放送日・映画公開日・キャラクターとクリエーターの誕生日を、{totalEntries} 件ぶん日付から引ける記念日カレンダー。今日は何の日かを 1 月 1 日から 12 月 31 日まで日付別にたどれます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "記念日", Url = "" }
            },
            OgCard = new OgCardSpec(Kicker: "", Title: "プリキュア記念日カレンダー")
            {
                Badges = new[]
                {
                    new OgCardBadge("収録", $"{totalEntries}件"),
                    new OgCardBadge("日付", "366日")
                },
                InlineFacts = new[]
                {
                    new OgCardFactLine("", "放送日・公開日・誕生日を日付から引ける")
                }
            }
        };

        _page.RenderAndWrite(PathUtil.AnniversaryIndexUrl(), "anniversary", "anniversary-index.sbn", content, layout);
    }

    // ════════════════════ テンプレ用モデル ════════════════════

    private sealed class DayContentModel
    {
        public string DateLabel { get; set; } = "";
        public IReadOnlyList<EpisodeRow> Episodes { get; set; } = Array.Empty<EpisodeRow>();
        /// <summary>放送と公開を古い順に 1 本へまとめた年表。テンプレはこちらを描く。</summary>
        public IReadOnlyList<TimelineRow> Timeline { get; set; } = Array.Empty<TimelineRow>();
        public IReadOnlyList<MovieRow> Movies { get; set; } = Array.Empty<MovieRow>();
        public IReadOnlyList<BirthdayRow> CharacterBirthdays { get; set; } = Array.Empty<BirthdayRow>();
        public IReadOnlyList<BirthdayRow> PersonBirthdays { get; set; } = Array.Empty<BirthdayRow>();
        /// <summary>出来事が 1 件でもあるか。false のときテンプレは「登録がありません」の案内に切り替える。</summary>
        public bool HasAnything { get; set; }
        public string PrevUrl { get; set; } = "";
        public string PrevLabel { get; set; } = "";
        public string NextUrl { get; set; } = "";
        public string NextLabel { get; set; } = "";
    }

    private sealed class EpisodeRow
    {
        public int Year { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public int EpisodeNo { get; set; }
        public string EpisodeTitle { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public bool IsFirstEpisode { get; set; }
        public bool IsLastEpisode { get; set; }
        /// <summary>
        /// サブタイトルが解禁前かどうか。true の話は題名を出さない。
        /// サイト本体は解禁時刻で自動解除されるぼかしを掛けているが、記念日ページは過去年と
        /// 未放送年が同じ日付に同居しうるため、ここでも同じ判断を通す。
        /// </summary>
        public bool SubtitleGuarded { get; set; }

        /// <summary>表示用のサブタイトル。解禁前は伏せ字ラベルに差し替わる。</summary>
        public string TitleDisplay => SubtitleGuarded ? "（サブタイトル解禁前）" : EpisodeTitle;
    }

    /// <summary>
    /// 日付ページの年表 1 行。放送（ep）と公開（mv）を同じ形に均して並べる。
    /// 表組みではなくカード列で見せるため、行の中身は「年・媒体・遷移先・表示文字列」だけを持つ。
    /// </summary>
    private sealed class TimelineRow
    {
        public int Year { get; set; }
        /// <summary>ep（TV 放送）か mv（劇場公開）か。テンプレの分岐に使う。</summary>
        public string Kind { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>「2004.10.31」形式。<c>/episodes/</c> 一覧と同じ書式に揃える。</summary>
        public string DateLabel { get; set; } = "";
        public int EpisodeNo { get; set; }
        public string TitleDisplay { get; set; } = "";
        public bool IsFirstEpisode { get; set; }
        public bool IsLastEpisode { get; set; }
    }

    private sealed class MovieRow
    {
        public int Year { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    private sealed class BirthdayRow
    {
        public string Name { get; set; } = "";
        /// <summary>補助表記（キャラクターなら変身後名義、人物なら生年）。空なら出さない。</summary>
        public string SubName { get; set; } = "";
        public string Url { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string KeyColorBackground { get; set; } = "";
        public string KeyColorForeground { get; set; } = "";
        public string KeyColorBorder { get; set; } = "";
    }

    private sealed class IndexContentModel
    {
        public IReadOnlyList<MonthSection> Months { get; set; } = Array.Empty<MonthSection>();
        public int TotalEntries { get; set; }
    }

    private sealed class MonthSection
    {
        public int Month { get; set; }
        public string Label { get; set; } = "";
        public IReadOnlyList<DayLink> Days { get; set; } = Array.Empty<DayLink>();
    }

    private sealed class DayLink
    {
        public int Day { get; set; }
        public string Url { get; set; } = "";
        /// <summary>その日の出来事件数。0 の日は索引で薄く出す。</summary>
        public int Count { get; set; }
        public int EpisodeCount { get; set; }
        public int MovieCount { get; set; }
        public int BirthdayCount { get; set; }
    }
}
