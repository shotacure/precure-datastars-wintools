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

                dayLinks.Add(new DayLink
                {
                    Day = day,
                    Url = PathUtil.AnniversaryUrl(month, day),
                    Count = dayEntries.Count
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
                IsLastEpisode = e.IsLastEpisode
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
            PageTitle = $"{dateLabel}のプリキュア",
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
            ? $"{dateLabel}のプリキュア。この日付に該当する出来事は現在のところ登録がありません。歴代シリーズの放送日・公開日・誕生日を日付から引ける記念日カレンダーです。"
            : $"{dateLabel}のプリキュア。{string.Join("、", parts)}。歴代シリーズの放送日・公開日・誕生日を日付から引ける記念日カレンダーです。";
    }

    /// <summary>
    /// 日付別ページの OGP カード。日付を識別子に据え、その日に積み上がった出来事の件数をバッジで見せる。
    /// 本文行には最初と最新のエピソードを置き、「この日付が何年から何年まで使われてきたか」を示す。
    /// </summary>
    private static OgCardSpec BuildDayOgCard(
        string dateLabel,
        IReadOnlyList<EpisodeRow> episodes,
        IReadOnlyList<MovieRow> movies,
        IReadOnlyList<BirthdayRow> characterBirthdays,
        IReadOnlyList<BirthdayRow> personBirthdays)
    {
        var badges = new List<OgCardBadge>();
        if (episodes.Count > 0) badges.Add(new OgCardBadge("放送", $"{episodes.Count}話"));
        if (movies.Count > 0) badges.Add(new OgCardBadge("公開", $"{movies.Count}本"));
        int birthdays = characterBirthdays.Count + personBirthdays.Count;
        if (birthdays > 0) badges.Add(new OgCardBadge("誕生日", $"{birthdays}人"));

        var facts = new List<OgCardFactLine>();
        if (episodes.Count > 0)
        {
            var oldest = episodes[0];
            facts.Add(new OgCardFactLine($"{oldest.Year}年", $"『{oldest.SeriesTitle}』第{oldest.EpisodeNo}話"));
            if (episodes.Count > 1)
            {
                var newest = episodes[^1];
                facts.Add(new OgCardFactLine($"{newest.Year}年", $"『{newest.SeriesTitle}』第{newest.EpisodeNo}話"));
            }
        }
        if (characterBirthdays.Count > 0)
            facts.Add(new OgCardFactLine("誕生日", string.Join("、", characterBirthdays.Select(b => b.Name))));

        // 見出しに日付が入るため、識別子（Headline）は置かない（同じ日付を 2 度出さない）。
        return new OgCardSpec(Kicker: "記念日", Title: $"{dateLabel}のプリキュア")
        {
            Badges = badges,
            InlineFacts = facts
        };
    }

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
            OgCard = new OgCardSpec(Kicker: "記念日", Title: "プリキュア記念日カレンダー")
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
    }
}
