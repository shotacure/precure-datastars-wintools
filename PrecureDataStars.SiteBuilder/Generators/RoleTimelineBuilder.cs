using System.Globalization;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 「年表」タブの線表（担当・出演・参加の移り変わり）を組み立てる。役職詳細・声の出演一覧・歌唱一覧・
/// 作詞作曲編曲や音楽の役職詳細で共通に使う。
/// <list type="bullet">
///   <item><description>横軸は時期。最初の TV シリーズの放送開始から、クレジットを収録した最新の TV の話・映画まで
///     （歌唱一覧のように、それより後の歌まで描くページでは最後の歌まで伸ばす）。TV シリーズ 1 作を 1 本の帯にし、
///     帯の左端に放送開始年の目盛りを置く。</description></item>
///   <item><description>行は人物・企業/団体・キャラクター。参加（TV の 1 話・映画 1 本・歌 1 曲・劇伴の録音回 1 回・盤 1 点）を日付順に並べ、
///     隣り合う参加の差が <see cref="RoleTimelineRules.MaxGapDays"/> 日以内ならつないで「続けて参加した期間」とする。
///     載せる行はページごとの決まり（<see cref="RoleTimelineRules"/>）で決める。「1 年間に 4 回」は、どこでもよい
///     連続する <see cref="RoleTimelineRules.WindowDays"/> 日（52 週）のあいだに参加（TV の話数・映画の本数）が 4 件以上あること
///     （シリーズや暦年の区切りによらず、期間をずらして数える）。
///     <list type="number">
///       <item><description>役職詳細：役職を問わずオープニングのクレジット（TV・映画とも）に出たことがある（メインスタッフ）か、
///         その役職を 1 年間に 4 回以上か、映画で通算 3 本以上担当したことがある。続けて担当した期間は 13 週以内の間隔でつなぐ。</description></item>
///       <item><description>声の出演：1 年間に 4 回以上出演したことがある。続けて出演した期間は 4 週以内の間隔でつなぐ。</description></item>
///       <item><description>作詞・作曲・編曲の役職詳細：1 年間に 2 曲以上担当したことがある。</description></item>
///       <item><description>歌唱・音楽の役職詳細：ページに載る行をすべて載せる。</description></item>
///     </list>
///     描ける参加のある候補はすべて行にする。役職詳細・声の出演・作詞作曲編曲で候補が <see cref="RoleTimelineRules.ShowAllUpTo"/>（10）を
///     超えるページは、決まりを満たす行を「主な方」とし、それが 10 に満たなければ残りから参加の多い順（同じ数なら最初の参加の早い順 →
///     その話のクレジットで先に出ている順）に 10 まで補う。初めは主な方だけを見せ、「主な方のみ」のスイッチを切るとすべての行を
///     本来の並びの位置に見せる（role-timeline.js）。候補が 10 以下のページはスイッチを出さず全員を見せる。
///     行には単発の参加も含めてすべての参加を描く。</description></item>
///   <item><description>並びは、最初の参加の日付の早い順。同じ日に始めた行は、団体を先に置き、その中で最後の参加の
///     日付の早い順（最初に抜けた順）、それも同じなら呼び出し側が渡す並びのキー（役職詳細・声の出演はその話のクレジットで
///     上に出ている順）→ 名前。役職詳細で「すべて」を見せているときは、その役職を会社の所属としてクレジットされたことのある
///     人物を、会社の行のすぐ下に（同じ並びの順で）字下げしてまとめる（<see cref="RoleTimelineEntity.MemberOf"/>）。
///     事業の引き継ぎでつながった会社（分社化・会社分割など）は系譜としてひとまとめにし、系譜で最初に参加した会社の位置に
///     並べて置いて、所属の人はその下にまとめる。</description></item>
///   <item><description>描くものは、続けて参加した期間（2 件以上つながったものの細線）・TV の話（同じシリーズで話数が
///     続く間はひと続きの帯。1 話は放送日から <see cref="EpisodeSpanDays"/> 日の幅）・映画（公開日の点）・
///     歌・劇伴（初めて収められた盤の発売日の点。日付の決め方は <see cref="MusicTimelineDates"/>）・盤（発売日の四角）。</description></item>
///   <item><description>役職詳細では、ページの役職と関連（段階・並列）でつながった役職の担当（<see cref="RoleTimelineEntity.RelatedRoles"/>）も、
///     主の帯の下に細い帯（映画は白抜きの輪）で重ねる。色は役職ごとで、関連の種類（前段階・並列・後段階）の色相を基準に、
///     同じ種類の役職どうしは色相を回して分ける（<see cref="AssignRelatedColors"/>）。主の帯と同じ回は隠れ、主の役職の無い回だけが
///     見える。凡例は色と役職名（役職詳細へのリンク）だけを出す。関連する役職の担当は、行の並び・主な方の判定・続けて参加した期間には使わない。</description></item>
/// </list>
/// 軸は同じ期間のページで共通なので、インスタンスを 1 つ作ってページ（役職）ごとに <see cref="Build"/> を呼ぶ。
/// <para>
/// 人物詳細・企業/団体詳細の年表（<see cref="ForParticipation"/>）は、軸をその参加のある期間だけに絞る。TV シリーズの帯のうち、参加
/// （TV の話・映画・歌・劇伴・盤）の日付が 1 つでも入る帯だけを並べ、最初の参加より前・最後の参加より後と、途中の参加の無い帯は
/// 軸に入れない。続かない帯のあいだには <see cref="ScopedGapDays"/> 日ぶんの切れ目（<see cref="RoleTimelineModel.Gaps"/>）を置く。
/// </para>
/// </summary>
internal sealed class RoleTimelineBuilder
{
    /// <summary>TV の 1 話が線表上で占める幅（日）。週 1 回の放送枠ぶん。</summary>
    private const int EpisodeSpanDays = 7;

    /// <summary>
    /// 年の目盛りを 4 桁で出せる帯の数の上限。
    /// これを超えると狭い画面（768px 以下）で年を下 2 桁にし、<see cref="AllTickBandsMaxWide"/> を超えると常に下 2 桁にする。
    /// </summary>
    private const int AllTickBandsMaxNarrow = 10;

    /// <summary>広い画面でも年の目盛りを 4 桁で出せる帯の数の上限。</summary>
    private const int AllTickBandsMaxWide = 22;

    /// <summary>参加のある期間だけの軸（<see cref="ForParticipation"/>）で、続かない帯のあいだに置く切れ目の幅（日）。</summary>
    private const int ScopedGapDays = 91;

    /// <summary>作品ごとの帯の軸（<see cref="ForSeriesBands"/>）で、作品の帯のあいだに置く切れ目の幅（日）。</summary>
    private const int SeriesBandGapDays = 14;

    /// <summary>映画の公開日が TV の話の帯にこの日数（前後 3 か月。幅 180 日）まで近ければ、重なるとみて映画を別の段に分ける（線表の幅では 3 か月ほどで点と帯が重なる）。</summary>
    private const int MovieLaneMarginDays = 90;

    /// <summary>
    /// 映画の丸（自分の映画の点・関連する役職の映画の輪）どうしが重なるとみる間隔（軸の幅に対する百分率）。
    /// 丸の幅 9px が、ふつうの画面の線表の幅のおよそ 1.3% にあたる。
    /// </summary>
    private const double MovieOverlapPct = 1.3;

    private readonly BuildContext _ctx;
    private readonly DateOnly _axisStart;
    private readonly int _axisDays;
    private readonly IReadOnlyList<RoleTimelineBand> _bands;
    private readonly IReadOnlyList<RoleTimelineTick> _ticks;
    private readonly string _tickDensityClass;
    private readonly IReadOnlyList<RoleTimelineGap> _gaps = Array.Empty<RoleTimelineGap>();

    /// <summary>
    /// 参加のある期間だけの軸の区間（日付の範囲と、軸の左端からの位置〔日〕）。null なら軸は最初の TV シリーズから
    /// 続けて取る（<see cref="Ratio"/> は日付をそのまま比で置く）。
    /// </summary>
    private readonly IReadOnlyList<AxisPiece>? _pieces;

    /// <summary>参加のある期間だけの軸の 1 区間（続いて並ぶ帯をひとまとめにしたもの）。</summary>
    /// <param name="Start">区間の始まり（最初の帯の放送開始日）。</param>
    /// <param name="End">区間の終わり（最後の帯の終わり。次のシリーズの放送開始日か軸の終わり）。</param>
    /// <param name="Offset">区間の始まりの、軸の左端からの位置（日。前の区間と切れ目の幅を足したもの）。</param>
    private readonly record struct AxisPiece(DateOnly Start, DateOnly End, int Offset);

    /// <summary>
    /// 軸（期間・シリーズの帯・年の目盛り）を確定させる。収録しているクレジットが無ければ軸を持たず、<see cref="Build"/> は常に null を返す。
    /// </summary>
    /// <param name="ctx">ビルド全体の共有データ。</param>
    /// <param name="extendTo">軸の終わりをこの日まで伸ばす（歌唱一覧で、クレジットの収録範囲より後の歌まで描くとき）。null なら伸ばさない。</param>
    public RoleTimelineBuilder(BuildContext ctx, DateOnly? extendTo = null)
        : this(ctx, extendTo, scopeDates: null)
    {
    }

    /// <summary>
    /// 人物詳細・企業/団体詳細の年表のための、参加のある期間だけの軸を作る。<paramref name="dates"/> はその人・その会社の参加の日付
    /// （TV の話の放送日・映画の公開日・歌・劇伴・盤の日付）。日付が無ければ軸を持たず、<see cref="Build"/> は常に null を返す。
    /// </summary>
    public static RoleTimelineBuilder ForParticipation(BuildContext ctx, IReadOnlyCollection<DateOnly> dates)
    {
        DateOnly? last = dates.Count > 0 ? dates.Max() : null;
        return new RoleTimelineBuilder(ctx, last?.AddDays(1), dates);
    }

    /// <summary>
    /// 作品ごとの帯の軸を作る（役職詳細・声の出演一覧で、オトナシリーズだけを見せているときの年表）。帯は渡した作品 1 本につき 1 本で、幅はその作品の第 1 話の放送日から
    /// 最終話の放送枠の終わりまで。年度（本編の TV シリーズ）の帯は使わない。作品が 2 本以上なら放送順に並べ、帯のあいだに
    /// <see cref="SeriesBandGapDays"/> 日ぶんの切れ目を置く。話の無い作品は帯にしない。帯が 1 本も無ければ <see cref="Build"/> は常に null を返す。
    /// </summary>
    public static RoleTimelineBuilder ForSeriesBands(BuildContext ctx, IEnumerable<Series> series)
        => new RoleTimelineBuilder(ctx, series.OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId).ToList());

    private RoleTimelineBuilder(BuildContext ctx, IReadOnlyList<Series> bandSeries)
    {
        _ctx = ctx;
        _bands = Array.Empty<RoleTimelineBand>();
        _ticks = Array.Empty<RoleTimelineTick>();
        _tickDensityClass = "";

        var pieces = new List<AxisPiece>();
        var kept = new List<(Series S, int Left, int Days)>();
        var gapStarts = new List<int>();
        int offset = 0;
        foreach (var s in bandSeries)
        {
            if (!ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps) || eps.Count == 0) continue;
            var start = eps.Min(e => e.OnAirDate);
            var end = eps.Max(e => e.OnAirDate).AddDays(EpisodeSpanDays);
            if (pieces.Count > 0)
            {
                gapStarts.Add(offset);
                offset += SeriesBandGapDays;
            }
            pieces.Add(new AxisPiece(start, end, offset));
            int days = end.DayNumber - start.DayNumber;
            kept.Add((s, offset, days));
            offset += days;
        }
        if (pieces.Count == 0) return;

        _axisStart = pieces[0].Start;
        _axisDays = offset;
        _pieces = pieces;
        string P(int day) => Num((double)day / offset * 100);
        _bands = kept.Select((k, i) => new RoleTimelineBand { Left = P(k.Left), Width = P(k.Days), IsAlt = i % 2 == 1 }).ToList();
        _ticks = kept.Select(k => new RoleTimelineTick
        {
            Left = P(k.Left),
            YearLabel = k.S.StartDate.Year.ToString(CultureInfo.InvariantCulture),
            SeriesTitle = k.S.Title,
            SeriesUrl = PathUtil.SeriesUrl(k.S.Slug),
            ShowLabel = true
        }).ToList();
        _gaps = gapStarts.Select(g => new RoleTimelineGap { Left = P(g), Width = P(SeriesBandGapDays) }).ToList();
    }

    /// <param name="ctx">ビルド全体の共有データ。</param>
    /// <param name="extendTo">軸の終わりをこの日まで伸ばす。null なら伸ばさない。</param>
    /// <param name="scopeDates">参加のある期間だけの軸にするときの参加の日付（<see cref="ForParticipation"/>）。null なら軸を続けて取る。</param>
    private RoleTimelineBuilder(BuildContext ctx, DateOnly? extendTo, IReadOnlyCollection<DateOnly>? scopeDates)
    {
        _ctx = ctx;
        _bands = Array.Empty<RoleTimelineBand>();
        _ticks = Array.Empty<RoleTimelineTick>();
        _tickDensityClass = "";

        if (ctx.CreditCoverageEpisode is not { } coverage) return;

        // 軸の終わり：クレジットを収録した最新の TV の話の放送枠の終わり。それより後に公開された映画の
        // クレジットを収録していれば、その公開日まで伸ばす。
        var axisEnd = coverage.Episode.OnAirDate.AddDays(EpisodeSpanDays);
        foreach (var sid in ctx.CreditsBySeries.Keys)
        {
            if (!ctx.IsMovieKindSeries(sid) || !ctx.SeriesById.TryGetValue(sid, out var mv)) continue;
            var mvEnd = mv.StartDate.AddDays(1);
            if (mvEnd > axisEnd) axisEnd = mvEnd;
        }
        if (extendTo is DateOnly ext && ext > axisEnd) axisEnd = ext;

        var tvSeries = ctx.SeriesById.Values
            .Where(s => string.Equals(s.KindCode, "TV", StringComparison.Ordinal) && s.StartDate < axisEnd)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();
        if (tvSeries.Count == 0) return;

        if (scopeDates is not null)
        {
            if (scopeDates.Count == 0) return;
            (_axisStart, _axisDays, _bands, _ticks, _tickDensityClass, _pieces, _gaps) = BuildScopedAxis(tvSeries, axisEnd, scopeDates);
            return;
        }

        _axisStart = tvSeries[0].StartDate;
        _axisDays = axisEnd.DayNumber - _axisStart.DayNumber;
        if (_axisDays <= 0) return;

        var bands = new List<RoleTimelineBand>(tvSeries.Count);
        var ticks = new List<RoleTimelineTick>(tvSeries.Count);
        bool manyBands = tvSeries.Count > AllTickBandsMaxNarrow;
        for (int i = 0; i < tvSeries.Count; i++)
        {
            var s = tvSeries[i];
            var bandEnd = i + 1 < tvSeries.Count ? tvSeries[i + 1].StartDate : axisEnd;
            if (bandEnd > axisEnd) bandEnd = axisEnd;
            bands.Add(new RoleTimelineBand
            {
                Left = Pct(s.StartDate),
                Width = PctWidth(s.StartDate, bandEnd),
                IsAlt = i % 2 == 1
            });
            // 軸の右端で幅の足りない帯（収録が始まったばかりのシリーズ）は、目盛りの文字がはみ出すので出さない。
            bool showLabel = (bandEnd.DayNumber - s.StartDate.DayNumber) >= 120;
            ticks.Add(new RoleTimelineTick
            {
                Left = Pct(s.StartDate),
                YearLabel = s.StartDate.Year.ToString(CultureInfo.InvariantCulture),
                SeriesTitle = s.Title,
                SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                ShowLabel = showLabel
            });
        }
        _bands = bands;
        _ticks = ticks;
        _tickDensityClass = tvSeries.Count > AllTickBandsMaxWide ? "rtl-ticks-sparse"
            : manyBands ? "rtl-ticks-medium"
            : "";
    }

    /// <summary>
    /// 参加のある期間だけの軸を組む。TV シリーズの帯（放送開始日から次のシリーズの放送開始日まで。最後の帯は
    /// <paramref name="axisEnd"/> まで）のうち、参加の日付が入る帯だけを残す（最初のシリーズより前の日付は最初の帯に入れる）。
    /// 続いて残った帯は 1 つの区間にし、続かない区間のあいだに <see cref="ScopedGapDays"/> 日ぶんの切れ目を置く。
    /// 帯の塗り分けと目盛りの間引きは、残った帯の並びで決める。
    /// </summary>
    private static (DateOnly AxisStart, int AxisDays, IReadOnlyList<RoleTimelineBand> Bands, IReadOnlyList<RoleTimelineTick> Ticks,
        string TickDensityClass, IReadOnlyList<AxisPiece> Pieces, IReadOnlyList<RoleTimelineGap> Gaps)
        BuildScopedAxis(IReadOnlyList<Series> tvSeries, DateOnly axisEnd, IReadOnlyCollection<DateOnly> dates)
    {
        DateOnly BandEnd(int i) => i + 1 < tvSeries.Count ? tvSeries[i + 1].StartDate : axisEnd;
        var kept = new SortedSet<int>();
        foreach (var d in dates)
        {
            int idx = 0;
            for (int i = tvSeries.Count - 1; i >= 0; i--)
            {
                if (tvSeries[i].StartDate <= d) { idx = i; break; }
            }
            kept.Add(idx);
        }

        // 続いて残った帯をひとまとめの区間にし、区間ごとに軸の位置（日）を割り当てる。
        var pieces = new List<AxisPiece>();
        var gapStarts = new List<int>();
        int offset = 0;
        int runFirst = -1, prev = -1;
        void Close()
        {
            if (runFirst < 0) return;
            var start = tvSeries[runFirst].StartDate;
            var end = BandEnd(prev);
            if (pieces.Count > 0)
            {
                gapStarts.Add(offset);
                offset += ScopedGapDays;
            }
            pieces.Add(new AxisPiece(start, end, offset));
            offset += end.DayNumber - start.DayNumber;
        }
        foreach (int i in kept)
        {
            if (prev >= 0 && i != prev + 1)
            {
                Close();
                runFirst = -1;
            }
            if (runFirst < 0) runFirst = i;
            prev = i;
        }
        Close();
        int axisDays = offset;

        string P(int day) => Num((double)day / axisDays * 100);
        int Place(DateOnly d)
        {
            foreach (var pc in pieces)
                if (d >= pc.Start && d <= pc.End) return pc.Offset + (d.DayNumber - pc.Start.DayNumber);
            return 0;
        }

        var bands = new List<RoleTimelineBand>(kept.Count);
        var ticks = new List<RoleTimelineTick>(kept.Count);
        int n = 0;
        foreach (int i in kept)
        {
            var s = tvSeries[i];
            var end = BandEnd(i);
            int left = Place(s.StartDate);
            bands.Add(new RoleTimelineBand
            {
                Left = P(left),
                Width = P(end.DayNumber - s.StartDate.DayNumber),
                IsAlt = n % 2 == 1
            });
            ticks.Add(new RoleTimelineTick
            {
                Left = P(left),
                YearLabel = s.StartDate.Year.ToString(CultureInfo.InvariantCulture),
                SeriesTitle = s.Title,
                SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                ShowLabel = (end.DayNumber - s.StartDate.DayNumber) >= 120
            });
            n++;
        }
        var gaps = gapStarts.Select(g => new RoleTimelineGap { Left = P(g), Width = P(ScopedGapDays) }).ToList();
        string density = kept.Count > AllTickBandsMaxWide ? "rtl-ticks-sparse"
            : kept.Count > AllTickBandsMaxNarrow ? "rtl-ticks-medium"
            : "";
        return (pieces[0].Start, axisDays, bands, ticks, density, pieces, gaps);
    }

    /// <summary>
    /// 1 ページ（役職）分の線表を組み立てる。載せる行が 1 つも無ければ null（テンプレは年表タブを出さない）。
    /// </summary>
    /// <param name="entities">線表に載せる候補（人物・企業/団体・キャラクター）。</param>
    /// <param name="rules">続けて参加したとみなす間隔・載せる決まり・凡例の言い回し。</param>
    public RoleTimelineModel? Build(IEnumerable<RoleTimelineEntity> entities, RoleTimelineRules rules)
    {
        if (_axisDays <= 0) return null;

        // 描ける参加のある候補はすべて行にする。その数が ShowAllUpTo を超えるページでは、決まりを満たす行と、
        // それが ShowAllUpTo に満たないときに参加の多い順で補った行を「主な方」とし、ほかの行は「主な方のみ」の
        // スイッチを切ったときだけ見せる。
        var entityList = entities.ToList();
        var relatedColors = AssignRelatedColors(entityList);
        var rows = new List<BuiltRow>();
        foreach (var e in entityList)
        {
            var built = BuildRow(e, rules, relatedColors);
            if (built is not null) rows.Add(built);
        }
        if (rows.Count == 0) return null;

        bool filterable = !rules.IncludeAll && rows.Count > rules.ShowAllUpTo;
        if (filterable)
        {
            foreach (var r in rows) r.Row.IsMain = r.Qualifies;
            int lack = rules.ShowAllUpTo - rows.Count(r => r.Qualifies);
            if (lack > 0)
            {
                // 補う順：参加の多い順 → 最初の参加の早い順 → 同じ話ならクレジットで先に出ている順 → 名前。
                foreach (var r in rows.Where(r => !r.Qualifies)
                             .OrderByDescending(r => r.CreditCount)
                             .ThenBy(r => r.First)
                             .ThenBy(r => r.FirstPos)
                             .ThenBy(r => r.Row.EntityName, StringComparer.Ordinal)
                             .Take(lack))
                    r.Row.IsMain = true;
            }
        }

        var ordered = rows
            .OrderBy(r => r.First)
            .ThenBy(r => string.Equals(r.Row.EntityKind, "company", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(r => r.Last)
            .ThenBy(r => r.FirstPos)
            .ThenBy(r => r.Row.EntityName, StringComparer.Ordinal)
            .ToList();
        bool grouped = GroupMembers(ordered, filterable);
        var lineageLegend = AssignLineageColors(ordered);

        var legend = new List<RoleTimelineLegendItem>();
        if (ordered.Any(r => r.Row.Segments.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "tv", Label = "TV" });
        // 凡例の並びは TV、映画、スピンオフ（オトナ）の順。
        if (ordered.Any(r => r.Row.Movies.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "movie", Label = "映画" });
        // スピンオフの帯の項目は、帯がどれもオトナシリーズ（大人向けスピンオフ）の話なら「オトナ」、
        // それ以外のスピンオフ（ショート・スピンオフ）の話が 1 本でもあれば「スピンオフ」。
        if (ordered.Any(r => r.Row.SpinOffSegments.Count > 0))
            legend.Add(new RoleTimelineLegendItem
            {
                Kind = "spinoff",
                Label = ordered.Any(r => r.Row.HasNonOtonaSpinOff) ? "スピンオフ" : "オトナ",
            });
        if (ordered.Any(r => r.Row.Songs.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "song", Label = "歌（初めて盤に収められた日）" });
        if (ordered.Any(r => r.Row.Bgms.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "bgm", Label = "劇伴（初めて盤に収められた日）" });
        if (ordered.Any(r => r.Row.Products.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "disc", Label = "盤（発売日）" });
        // 関連する役職は、前段階 → 並列 → 後段階の順、同じ種類の中は役職マスタの表示順に、役職ごとの色と役職名だけを出す。
        // 見本の形は描いた印に合わせる（TV の話があれば細い帯、映画があれば白抜きの輪。両方あれば並べる）。
        foreach (var g in ordered
                     .SelectMany(r => r.RelatedRoles)
                     .GroupBy(x => x.RoleCode, StringComparer.Ordinal)
                     .Select(g => (Role: g.First(), Items: g.ToList()))
                     .OrderBy(x => x.Role.Lane)
                     .ThenBy(x => x.Role.SortOrder)
                     .ThenBy(x => x.Role.RoleCode, StringComparer.Ordinal))
        {
            bool hasTv = g.Items.Any(x => x.Episodes.Any(ep => ep.EpisodeId != 0));
            bool hasMovie = g.Items.Any(x => x.MovieSeriesIds.Count > 0);
            legend.Add(new RoleTimelineLegendItem
            {
                Kind = hasTv ? "rel" : "rel-movie",
                ExtraKind = hasTv && hasMovie ? "rel-movie" : "",
                Color = relatedColors.TryGetValue(g.Role.RoleCode, out var c) ? c : "",
                Label = g.Role.RoleName,
                Url = (rules.RelatedRoleUrl ?? PathUtil.CreatorsRoleUrl)(g.Role.RoleCode)
            });
        }
        legend.Add(new RoleTimelineLegendItem { Kind = "span", Label = $"続けて{rules.Verb}した期間" });
        legend.AddRange(lineageLegend);

        return new RoleTimelineModel
        {
            // 絞り込みの条件はページの種類ごとの決まりをそのまま書く（登録状況で変わる出し分けはしない）。
            IsFilterable = filterable,
            FilterTip = filterable ? rules.FilterTip : Array.Empty<string>(),
            MainCount = rows.Count(r => r.Row.IsMain),
            Legend = legend,
            Bands = _bands,
            Gaps = _gaps,
            Ticks = _ticks,
            TickDensityClass = _tickDensityClass,
            HasGroups = grouped,
            Rows = ordered.Select(r => r.Row).ToList()
        };
    }

    /// <summary>
    /// 会社とその所属の人を、「すべて」を見せているときのひとまとめ（ゾーン）にする。会社の外（会社と、どこにも
    /// 所属していない人）は <paramref name="ordered"/> の並びのまま置き、会社の行のすぐ下に所属の人を同じ並びの順で続ける。
    /// <paramref name="ordered"/> はその並び（ゾーンの並び）に組み替え、行ごとに <see cref="RoleTimelineRow.FlatOrder"/>
    /// （個人・団体で絞り込んだときの元の並びの位置）を書き込む。所属先の会社が行に無い人は会社の外に置く。
    /// 主な方を選び分けるページでは、所属の人に主な方がいれば会社の行も主な方にする（見出しの会社の無いまま所属の人を見せない）。
    /// 事業の引き継ぎでつながった会社（同じ <see cref="RoleTimelineEntity.GroupKey"/> を持つ会社の行）は、系譜で最初に参加した
    /// 会社の位置に並べて置き（2 つ目からは <see cref="RoleTimelineRow.IsGroupSuccessor"/>）、所属の人はその最後の会社の行の下に続ける。
    /// 個人・団体で絞り込んだときの並びでも、系譜の会社は並べて置く。系譜の会社のどれかか所属の人に主な方がいれば、系譜の会社の行をすべて主な方にする。
    /// まとめる会社が 1 つも無ければ何もせず false。
    /// </summary>
    private static bool GroupMembers(List<BuiltRow> ordered, bool filterable)
    {
        var heads = ordered
            .Where(r => r.Entity.GroupKey != "")
            .GroupBy(r => r.Entity.GroupKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var membersByHead = ordered
            .Where(r => r.Entity.MemberOf != "" && heads.ContainsKey(r.Entity.MemberOf))
            .GroupBy(r => r.Entity.MemberOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        // まとめるのは、所属の人がいる会社と、系譜の会社が 2 つ以上行にあるもの。
        var zoneKeys = heads
            .Where(kv => kv.Value.Count > 1 || membersByHead.ContainsKey(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (zoneKeys.Count == 0) return false;

        // 個人・団体で絞り込んだときの並び：元の並びのまま、系譜の会社だけを最初の会社の位置に寄せる。
        var flat = new List<BuiltRow>(ordered.Count);
        var flatPlaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in ordered)
        {
            if (r.Entity.GroupKey != "" && heads[r.Entity.GroupKey].Count > 1)
            {
                if (flatPlaced.Add(r.Entity.GroupKey)) flat.AddRange(heads[r.Entity.GroupKey]);
                continue;
            }
            flat.Add(r);
        }
        for (int i = 0; i < flat.Count; i++) flat[i].Row.FlatOrder = i;

        var zoned = new List<BuiltRow>(ordered.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in flat)
        {
            if (r.Entity.MemberOf != "" && membersByHead.ContainsKey(r.Entity.MemberOf)) continue;
            if (r.Entity.GroupKey == "" || !zoneKeys.Contains(r.Entity.GroupKey))
            {
                zoned.Add(r);
                continue;
            }
            if (!placed.Add(r.Entity.GroupKey)) continue;

            var companies = heads[r.Entity.GroupKey];
            var members = membersByHead.TryGetValue(r.Entity.GroupKey, out var ms) ? ms : new List<BuiltRow>();
            bool zoneMain = filterable && companies.Concat(members).Any(m => m.Row.IsMain);
            for (int i = 0; i < companies.Count; i++)
            {
                var c = companies[i];
                c.Row.IsGroupHead = true;
                c.Row.IsGroupSuccessor = i > 0;
                if (zoneMain) c.Row.IsMain = true;
                // TV のみ・映画のみで絞り込んだときも、所属の人か系譜のほかの会社が見えていれば見出しの会社の行を残す。
                c.Row.ZoneHasTv = members.Any(m => m.Row.HasTv) || companies.Any(o => o != c && o.Row.HasTv);
                c.Row.ZoneHasMovie = members.Any(m => m.Row.HasMovie) || companies.Any(o => o != c && o.Row.HasMovie);
                c.Row.ZoneHasOtona = members.Any(m => m.Row.HasOtona) || companies.Any(o => o != c && o.Row.HasOtona);
                zoned.Add(c);
            }
            foreach (var m in members)
            {
                m.Row.IsGroupMember = true;
                zoned.Add(m);
            }
        }
        ordered.Clear();
        ordered.AddRange(zoned);
        return true;
    }

    /// <summary>
    /// 事業の引き継ぎでつながった会社（同じ鍵の会社の行が 2 つ以上並んだゾーン）の所属線を、会社ごとの色で描き分ける。
    /// 系譜の最初の会社はふだんの所属線の色のまま、2 つ目からの会社に <see cref="LineageAffColors"/> の色をページの上から順に割り当てる。
    /// 会社の行の名前に同じ色の下線（<see cref="RoleTimelineRow.AffColor"/>）を引き、所属の人の所属線
    /// （<see cref="RoleTimelineAffiliationMark.CompanyKey"/> が一致するもの）をその色にする。
    /// 凡例の項目（「所属：会社名」と線の見本）を、所属線を描いた会社の分だけ返す。
    /// </summary>
    private static List<RoleTimelineLegendItem> AssignLineageColors(List<BuiltRow> ordered)
    {
        var legend = new List<RoleTimelineLegendItem>();
        var zones = ordered
            .Where(r => r.Row.IsGroupHead)
            .GroupBy(r => r.Entity.GroupKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (zones.Count == 0) return legend;

        var drawn = ordered
            .SelectMany(r => r.Row.Affiliations)
            .Select(m => m.CompanyKey)
            .ToHashSet(StringComparer.Ordinal);
        var colorByCompany = new Dictionary<string, string>(StringComparer.Ordinal);
        int next = 0;
        foreach (var zone in zones)
        {
            int i = 0;
            foreach (var c in zone)
            {
                string color = i++ == 0 ? "" : LineageAffColors[next++ % LineageAffColors.Length];
                colorByCompany[c.Entity.CompanyKey] = color;
                c.Row.HasAffChip = true;
                c.Row.AffColor = color;
                if (drawn.Contains(c.Entity.CompanyKey))
                    legend.Add(new RoleTimelineLegendItem { Kind = "aff", Color = color, Label = $"所属：{c.Row.EntityName}" });
            }
        }
        foreach (var r in ordered)
            foreach (var m in r.Row.Affiliations)
                if (colorByCompany.TryGetValue(m.CompanyKey, out var color)) m.Color = color;
        return legend;
    }

    /// <summary>
    /// 系譜の 2 つ目からの会社の所属線の色。主の青・映画の橙・関連する役職の色（若葉・ティール・藤色）と紛れない色を選ぶ。
    /// </summary>
    private static readonly string[] LineageAffColors = { "#e0703a", "#3f9b5a", "#c0508f", "#8d6e3f" };

    /// <summary>組み立てた 1 行と、並び・主な方の選び出しに使う値。</summary>
    /// <param name="Qualifies">ページの決まり（<see cref="RoleTimelineRules"/>）を満たすか。</param>
    /// <param name="CreditCount">描ける参加の数（TV の話数・映画の本数・歌の曲数など）。主な方を補う順に使う。</param>
    /// <param name="Entity">行の元にした候補（会社とその所属の人のまとめに使う）。</param>
    /// <param name="RelatedRoles">行に重ねた関連する役職（凡例に出す分）。</param>
    private sealed record BuiltRow(RoleTimelineEntity Entity, RoleTimelineRow Row, DateOnly First, DateOnly Last, long FirstPos, int CreditCount, bool Qualifies,
        IReadOnlyList<RoleTimelineRelatedRole> RelatedRoles);

    /// <summary>
    /// 関連する役職の色を、ページに出る役職ごとに決める（role_code → CSS の色）。関連の種類ごとの基準の色（前段階＝若葉、並列＝ティール、
    /// 後段階＝藤色。いずれも主の青より淡い）から、同じ種類の役職どうしは色相を <see cref="RelatedHueStep"/> 度ずつ回して分ける
    /// （役職マスタの表示順に並べ、基準の色相を中心に左右へ振る。種類ごとの振れ幅は <see cref="RelatedHueSpread"/> 度まで）。
    /// 描ける担当（話の付いた TV の担当か映画）の無い役職には色を割り当てない。
    /// </summary>
    private Dictionary<string, string> AssignRelatedColors(IEnumerable<RoleTimelineEntity> entities)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var byLane in entities
                     .SelectMany(e => e.RelatedRoles)
                     .Where(x => x.Episodes.Any(ep => ep.EpisodeId != 0) || x.MovieSeriesIds.Count > 0)
                     .GroupBy(x => x.Lane))
        {
            var roles = byLane
                .GroupBy(x => x.RoleCode, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RoleCode, StringComparer.Ordinal)
                .ToList();
            var (hue, sat, light) = byLane.Key switch
            {
                RoleRelationLane.Before => (86.0, 50, 72),
                RoleRelationLane.Parallel => (182.0, 45, 70),
                _ => (262.0, 50, 80)
            };
            double step = roles.Count > 1 ? Math.Min(RelatedHueStep, RelatedHueSpread / (roles.Count - 1)) : 0;
            for (int i = 0; i < roles.Count; i++)
            {
                double h = hue + (i - (roles.Count - 1) / 2.0) * step;
                colors[roles[i].RoleCode] = FormattableString.Invariant($"hsl({Math.Round((h % 360 + 360) % 360)}, {sat}%, {light}%)");
            }
        }
        return colors;
    }

    /// <summary>同じ種類の関連する役職どうしの色相の差（度）。</summary>
    private const double RelatedHueStep = 36;

    /// <summary>同じ種類の関連する役職の色相の振れ幅の上限（度）。ほかの種類の色相に届かないようにする。</summary>
    private const double RelatedHueSpread = 72;

    /// <summary>
    /// 1 エンティティの参加を日付順に並べて「続けて参加した期間」に区切り、描く行を作る。描ける参加が無いものは null。
    /// </summary>
    /// <param name="relatedColors">関連する役職の色（<see cref="AssignRelatedColors"/>）。</param>
    private BuiltRow? BuildRow(RoleTimelineEntity e, RoleTimelineRules rules, IReadOnlyDictionary<string, string> relatedColors)
    {
        var credits = new List<Credit>(e.Episodes.Count + e.MovieSeriesIds.Count + e.Songs.Count + e.Bgms.Count + e.Products.Count);
        foreach (var (sid, eid) in e.Episodes)
        {
            // シリーズ全体に付いた TV 系のクレジット（話の無いもの）は日付を持たないので描かない。
            if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
            credits.Add(new Credit(ep.OnAirDate, CreditKind.Tv, sid, ep.SeriesEpNo, ""));
        }
        foreach (var sid in e.MovieSeriesIds)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            credits.Add(new Credit(s.StartDate, CreditKind.Movie, sid, 0, ""));
        }
        foreach (var p in e.Songs) credits.Add(new Credit(p.Date, CreditKind.Song, 0, 0, p.Title));
        foreach (var p in e.Bgms) credits.Add(new Credit(p.Date, CreditKind.Bgm, 0, 0, p.Title));
        foreach (var p in e.Products) credits.Add(new Credit(p.Date, CreditKind.Product, 0, 0, p.Title));
        if (credits.Count == 0) return null;
        credits.Sort((a, b) =>
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.SeriesId.CompareTo(b.SeriesId);
            return c != 0 ? c : a.EpNo.CompareTo(b.EpNo);
        });

        // 隣り合う参加の日付の差が MaxGapDays 以内なら同じ期間（単発の参加は 1 件だけの期間になる）。
        var chains = new List<List<Credit>>();
        var cur = new List<Credit> { credits[0] };
        for (int i = 1; i < credits.Count; i++)
        {
            if (credits[i].Date.DayNumber - cur[^1].Date.DayNumber <= rules.MaxGapDays)
            {
                cur.Add(credits[i]);
            }
            else
            {
                chains.Add(cur);
                cur = new List<Credit> { credits[i] };
            }
        }
        chains.Add(cur);

        // 期間の終わり：最後の参加の放送枠の終わり（映画・歌・劇伴・盤は点の真ん中＝その日付）。
        static DateOnly ChainEnd(List<Credit> chain)
            => chain.Max(c => c.Kind == CreditKind.Tv ? c.Date.AddDays(EpisodeSpanDays) : c.Date);
        bool qualifies = rules.IncludeAll
            || (rules.IncludeOpeningCredit && e.HasOpeningCredit)
            || MaxCreditsInWindow(credits, rules.WindowDays) >= rules.MinCreditsInWindow
            || (rules.MinMovies > 0 && credits.Count(c => c.Kind == CreditKind.Movie) >= rules.MinMovies);

        var spans = new List<RoleTimelineMark>(chains.Count);
        var segments = new List<RoleTimelineMark>();
        var spinOffSegments = new List<RoleTimelineMark>();
        // スピンオフの帯に、オトナシリーズ以外のスピンオフの話が入ったか（凡例の文字を決める）。
        bool hasNonOtonaSpinOff = false;
        var movies = new List<RoleTimelineMark>();
        // 映画の段を分けるかの判定に使う、本線の TV の話の帯（関連する役職の帯を含む）の期間と映画（関連する役職の映画を含む）の公開日。
        var tvRanges = new List<(DateOnly Start, DateOnly End)>();
        var movieDates = new List<DateOnly>();
        var songs = new List<RoleTimelineMark>();
        var bgms = new List<RoleTimelineMark>();
        var products = new List<RoleTimelineMark>();
        var periodLabels = new List<string>(chains.Count);
        var tvEpNos = new Dictionary<int, SortedSet<int>>();
        var movieIds = new HashSet<int>();

        foreach (var chain in chains)
        {
            var start = chain[0].Date;
            periodLabels.Add(PeriodLabel(start, chain[^1].Date));

            // TV の話は、同じシリーズで話数が続く間をひと続きの帯にまとめる。スピンオフを分けて描く線表
            // （RoleTimelineRules.SeparateSpinOffs）では、スピンオフの話の帯を別の色の帯にする。
            foreach (var bySeries in chain.Where(c => c.Kind == CreditKind.Tv).GroupBy(c => c.SeriesId))
            {
                var segTarget = rules.SeparateSpinOffs && _ctx.IsSpinOffSeries(bySeries.Key) ? spinOffSegments : segments;
                if (ReferenceEquals(segTarget, spinOffSegments) && !_ctx.IsOtonaSeries(bySeries.Key)) hasNonOtonaSpinOff = true;
                var eps = bySeries.OrderBy(c => c.EpNo).ToList();
                int runStart = 0;
                for (int i = 1; i <= eps.Count; i++)
                {
                    if (i < eps.Count && eps[i].EpNo == eps[i - 1].EpNo + 1) continue;
                    var segStart = eps[runStart].Date;
                    var segEnd = eps[i - 1].Date.AddDays(EpisodeSpanDays);
                    segTarget.Add(new RoleTimelineMark { Left = Pct(segStart), Width = PctWidth(segStart, segEnd) });
                    if (ReferenceEquals(segTarget, segments)) tvRanges.Add((segStart, segEnd));
                    runStart = i;
                }
                if (!tvEpNos.TryGetValue(bySeries.Key, out var set))
                {
                    set = new SortedSet<int>();
                    tvEpNos[bySeries.Key] = set;
                }
                foreach (var c in eps) set.Add(c.EpNo);
            }
            foreach (var c in chain.Where(c => c.Kind == CreditKind.Movie))
            {
                movies.Add(new RoleTimelineMark { Left = Pct(c.Date) });
                movieDates.Add(c.Date);
                movieIds.Add(c.SeriesId);
            }
            // 歌・劇伴・盤は、同じ日のものを 1 つの印にまとめる。
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Song).Select(c => c.Date).Distinct())
                songs.Add(new RoleTimelineMark { Left = Pct(d) });
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Bgm).Select(c => c.Date).Distinct())
                bgms.Add(new RoleTimelineMark { Left = Pct(d) });
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Product).Select(c => c.Date).Distinct())
                products.Add(new RoleTimelineMark { Left = Pct(d) });
        }

        // 内訳：TV・映画はシリーズの放送・公開順に「📺 シリーズ名 #1～49（添え書き）」「🎥 映画名（添え書き）」
        // （大人向けスピンオフの話は 📺 の代わりに 🌙）、
        // 歌・劇伴・盤は年ごとに「🎵 2004年 曲名、曲名」「🎼 2004年 作品名（録音回）」「💿 2004年 商品名」で 1 行ずつ。
        var works = new List<(DateOnly Sort, string Text)>();
        foreach (var sid in tvEpNos.Keys.Concat(movieIds).Distinct())
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            string note = e.SeriesNotes is not null && e.SeriesNotes.TryGetValue(sid, out var n) && n != "" ? $"（{n}）" : "";
            works.Add((s.StartDate, tvEpNos.TryGetValue(sid, out var nos)
                ? $"{(_ctx.IsOtonaSeries(sid) ? "🌙" : "📺")} {s.Title} {EpisodeRangeCompressor.Compress(nos)}{note}"
                : $"🎥 {s.Title}{note}"));
        }
        foreach (var (kind, icon, order) in new[] { (CreditKind.Song, "🎵", 0), (CreditKind.Bgm, "🎼", 1), (CreditKind.Product, "💿", 2) })
        {
            foreach (var byYear in credits.Where(c => c.Kind == kind).GroupBy(c => c.Date.Year))
            {
                var titles = byYear.Select(c => c.Title).Where(t => t != "").Distinct(StringComparer.Ordinal);
                // 同じ年の中では歌 → 劇伴 → 盤の順に並べる。
                works.Add((new DateOnly(byYear.Key, 1, 1).AddDays(order), $"{icon} {byYear.Key}年 {string.Join("、", titles)}"));
            }
        }

        // 関連する役職の担当：役職ごとに、TV の話は同じシリーズで話数が続く間をひと続きの細い帯に、映画は白抜きの輪にする（色は役職ごと）。
        // 内訳には主の担当の後ろに「〔役職名〕📺 シリーズ名 #…」「〔役職名〕🎥 映画名」を役職ごとに足す（大人向けスピンオフの話は 📺 の代わりに 🌙）。
        var related = new List<RoleTimelineRelatedMark>();
        var drawnRelated = new List<RoleTimelineRelatedRole>();
        var relatedWorks = new List<string>();
        foreach (var rel in e.RelatedRoles
                     .OrderBy(x => x.Lane)
                     .ThenBy(x => x.SortOrder)
                     .ThenBy(x => x.RoleCode, StringComparer.Ordinal))
        {
            if (!relatedColors.TryGetValue(rel.RoleCode, out var color)) continue;
            var relEps = new Dictionary<int, SortedDictionary<int, DateOnly>>();
            foreach (var (sid, eid) in rel.Episodes)
            {
                if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
                if (!relEps.TryGetValue(sid, out var bySeries))
                {
                    bySeries = new SortedDictionary<int, DateOnly>();
                    relEps[sid] = bySeries;
                }
                bySeries[ep.SeriesEpNo] = ep.OnAirDate;
            }
            var relMovies = rel.MovieSeriesIds.Where(_ctx.SeriesById.ContainsKey).ToList();
            if (relEps.Count == 0 && relMovies.Count == 0) continue;
            drawnRelated.Add(rel);

            var relLines = new List<(DateOnly Sort, string Text)>();
            foreach (var (sid, bySeries) in relEps)
            {
                var eps = bySeries.ToList();
                int runStart = 0;
                for (int i = 1; i <= eps.Count; i++)
                {
                    if (i < eps.Count && eps[i].Key == eps[i - 1].Key + 1) continue;
                    var segStart = eps[runStart].Value;
                    var segEnd = eps[i - 1].Value.AddDays(EpisodeSpanDays);
                    related.Add(new RoleTimelineRelatedMark { Color = color, Left = Pct(segStart), Width = PctWidth(segStart, segEnd), IsSpinOff = _ctx.IsSpinOffSeries(sid) });
                    tvRanges.Add((segStart, segEnd));
                    runStart = i;
                }
                if (_ctx.SeriesById.TryGetValue(sid, out var s))
                    relLines.Add((s.StartDate, $"〔{rel.RoleName}〕{(_ctx.IsOtonaSeries(sid) ? "🌙" : "📺")} {s.Title} {EpisodeRangeCompressor.Compress(bySeries.Keys)}"));
            }
            foreach (var sid in relMovies)
            {
                var s = _ctx.SeriesById[sid];
                related.Add(new RoleTimelineRelatedMark { Color = color, Left = Pct(s.StartDate), IsMovie = true });
                movieDates.Add(s.StartDate);
                relLines.Add((s.StartDate, $"〔{rel.RoleName}〕🎥 {s.Title}"));
            }
            relatedWorks.AddRange(relLines.OrderBy(l => l.Sort).Select(l => l.Text));
        }

        // 映画・スピンオフを別の段に描くか（RoleTimelineRow.MovieLane / SpinOffLane と同じ決まり）。
        // 映画は、公開日が本線の TV の話の帯（関連する役職の帯を含む）の期間（前後 MovieLaneMarginDays 日を含む）に入れば分ける。
        bool movieLane = movieDates.Any(d => tvRanges.Any(r =>
            d >= r.Start.AddDays(-MovieLaneMarginDays) && d <= r.End.AddDays(MovieLaneMarginDays)));
        bool spinOffLane = spinOffSegments.Count > 0
            && (segments.Count > 0 || movies.Count > 0 || songs.Count > 0 || bgms.Count > 0 || products.Count > 0);
        // 続けて参加した期間の細線は、一番上の段（本線）に描く印の参加だけでつなぎ直して引く。別の段に分けた映画・スピンオフの日付まで
        // 線がはみ出さないように、端は上の段の最初の印の始まりと最後の印の終わり（TV の話は放送枠の終わり）にきっちり合わせる。
        // 細線は 2 件以上つながった期間だけに引く（単発の参加は帯・点だけ）。
        var topCredits = credits.Where(c =>
                !(movieLane && c.Kind == CreditKind.Movie)
                && !(spinOffLane && c.Kind == CreditKind.Tv && _ctx.IsSpinOffSeries(c.SeriesId)))
            .ToList();
        var run = new List<Credit>();
        void CloseRun()
        {
            if (run.Count >= 2)
            {
                var s0 = run.Min(c => c.Date);
                spans.Add(new RoleTimelineMark { Left = Pct(s0), Width = PctWidth(s0, ChainEnd(run)) });
            }
            run = new List<Credit>();
        }
        foreach (var c in topCredits)
        {
            if (run.Count > 0 && c.Date.DayNumber - run[^1].Date.DayNumber > rules.MaxGapDays) CloseRun();
            run.Add(c);
        }
        CloseRun();

        // 映画の丸（自分の映画の点と関連する役職の映画の輪。同じ段に並ぶ）が隣の丸と MovieOverlapPct より近く続く組の中に、
        // 違う役職の丸があるときだけ、役職ごとに上・下・上…と交互にずらす。同じ役職の丸どうし（併映など）はずらさずに重ねる。
        // 役職は、自分の映画を空文字、関連する役職の映画を役職の色で見分ける（色は役職ごとに 1 つ）。
        var moviePoints = movies.Select(m => (Left: double.Parse(m.Left, CultureInfo.InvariantCulture), Role: "", Set: (Action<int>)(v => m.Shift = v)))
            .Concat(related.Where(m => m.IsMovie).Select(m => (Left: double.Parse(m.Left, CultureInfo.InvariantCulture), Role: m.Color, Set: (Action<int>)(v => m.Shift = v))))
            .OrderBy(x => x.Left)
            .ToList();
        for (int i = 0; i < moviePoints.Count;)
        {
            int j = i + 1;
            while (j < moviePoints.Count && moviePoints[j].Left - moviePoints[j - 1].Left < MovieOverlapPct) j++;
            var roles = moviePoints.Skip(i).Take(j - i).Select(x => x.Role).Distinct().ToList();
            if (roles.Count >= 2)
                for (int k = i; k < j; k++) moviePoints[k].Set(roles.IndexOf(moviePoints[k].Role) % 2 == 0 ? -1 : 1);
            i = j;
        }

        var row = new RoleTimelineRow
        {
            EntityKind = e.EntityKind,
            EntityName = e.EntityName,
            EntitySubLabel = e.EntitySubLabel,
            EntityUrl = e.EntityUrl,
            Spans = spans,
            Segments = segments,
            SpinOffSegments = spinOffSegments,
            HasNonOtonaSpinOff = hasNonOtonaSpinOff,
            Movies = movies,
            MovieLane = movieLane,
            Songs = songs,
            Bgms = bgms,
            Products = products,
            Related = related,
            PeriodLabel = string.Join("、", periodLabels),
            WorksText = string.Join("\n", works.OrderBy(w => w.Sort).Select(w => w.Text).Concat(relatedWorks))
        };
        BuildAffiliationLines(e, row, rules);
        return new BuiltRow(e, row, chains[0][0].Date, chains[^1][^1].Date, e.FirstSortPos, credits.Count, qualifies, drawnRelated);
    }

    /// <summary>
    /// 所属線と内訳の「所属：」の行を組む。所属線は TV の話（本線とスピンオフ）の担当からだけ引き、映画の担当からは引かない
    /// （映画の点の段に線が付いて見えないように）。スピンオフの段を分けた行（<see cref="RoleTimelineRow.SpinOffLane"/>）では、
    /// スピンオフの話の線をスピンオフの段（<see cref="RoleTimelineAffiliationMark.InSpinOffLane"/>）に分けて引く。
    /// 内訳の「所属：」の期間は映画も含めて数える。企業ごとに、所属としてクレジットされた担当を日付順に並べ、
    /// 続けて参加した期間と同じ間隔（<see cref="RoleTimelineRules.MaxGapDays"/>）以内ならつないで 1 本の線にする
    /// （映画の個人名だけのクレジットなど、所属の出ていない回が挟まっても切れない）。1 件だけの期間も、TV の 1 話ぶんの幅で引く。
    /// まとめた企業（<see cref="RoleTimelineEntity.MemberOf"/>。系譜の企業を含む）以外の企業の線は点線にする。企業の並びは最初の所属の早い順。
    /// 違う企業の線が同じ時期に重なるときは、後から始まる線を 1 段上に引く（<see cref="RoleTimelineAffiliationMark.Lane"/>）。
    /// </summary>
    private void BuildAffiliationLines(RoleTimelineEntity e, RoleTimelineRow row, RoleTimelineRules rules)
    {
        if (e.Affiliations.Count == 0) return;
        var marks = new List<(DateOnly From, DateOnly To, RoleTimelineAffiliationMark Mark)>();
        var texts = new List<(DateOnly First, string Text)>();
        bool splitSpinOff = row.SpinOffLane;

        // 日付順の参加を、間隔 MaxGapDays 以内でつないだ期間（始まり, 最後の参加）に分ける。
        List<(DateOnly From, DateOnly Last)> Runs(List<DateOnly> dates)
        {
            var runs = new List<(DateOnly, DateOnly)>();
            dates.Sort();
            int start = 0;
            for (int i = 1; i <= dates.Count; i++)
            {
                if (i < dates.Count && dates[i].DayNumber - dates[i - 1].DayNumber <= rules.MaxGapDays) continue;
                runs.Add((dates[start], dates[i - 1]));
                start = i;
            }
            return runs;
        }

        foreach (var aff in e.Affiliations)
        {
            var mainDates = new List<DateOnly>();
            var spinDates = new List<DateOnly>();
            foreach (var (sid, eid) in aff.Episodes)
            {
                if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
                (splitSpinOff && _ctx.IsSpinOffSeries(sid) ? spinDates : mainDates).Add(ep.OnAirDate);
            }
            var allDates = mainDates.Concat(spinDates).ToList();
            foreach (var sid in aff.MovieSeriesIds)
                if (_ctx.SeriesById.TryGetValue(sid, out var s)) allDates.Add(s.StartDate);
            if (allDates.Count == 0) continue;

            bool isOther = !string.Equals(aff.GroupKey, e.MemberOf, StringComparison.Ordinal);
            foreach (var (dates, inSpin) in new[] { (mainDates, false), (spinDates, true) })
            {
                foreach (var (from, last) in Runs(dates))
                {
                    var to = last.AddDays(EpisodeSpanDays);
                    marks.Add((from, to, new RoleTimelineAffiliationMark
                    {
                        Left = Pct(from),
                        Width = PctWidth(from, to),
                        IsOther = isOther,
                        CompanyKey = aff.CompanyKey,
                        InSpinOffLane = inSpin
                    }));
                }
            }
            if (aff.CompanyName != "")
            {
                var runs = Runs(allDates);
                texts.Add((runs[0].From, $"所属：{aff.CompanyName}（{string.Join("、", runs.Select(r => PeriodLabel(r.From, r.Last)))}）"));
            }
        }
        // 段の割り当て：段（本線・スピンオフ）ごとに、始まりの早い順に、前の線が終わっている一番下の段へ置く。
        foreach (var group in marks.GroupBy(x => x.Mark.InSpinOffLane))
        {
            var laneEnds = new List<DateOnly>();
            foreach (var (from, to, mark) in group.OrderBy(x => x.From))
            {
                int lane = laneEnds.FindIndex(end => end <= from);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(to);
                }
                else laneEnds[lane] = to;
                mark.Lane = lane;
            }
        }
        row.Affiliations = marks.Select(x => x.Mark).ToList();
        row.AffiliationText = string.Join("\n", texts.OrderBy(t => t.First).Select(t => t.Text));
    }

    /// <summary>
    /// 連続する <paramref name="windowDays"/> 日のあいだに入る参加の数の最大（日付順に並んだ <paramref name="credits"/> を
    /// 尺取りで数える。最初と最後の参加の日付の差が <paramref name="windowDays"/> 日以内なら同じ期間に入る）。
    /// </summary>
    /// <summary>
    /// 参加の日付の並びが、線表に載せる決まり（<paramref name="rules"/>）を満たすか。<see cref="BuildRow"/> の判定と同じ。
    /// OGP カードのように線表そのものは組まず、「サイトの年表に載る人か」だけを知りたいときに使う。
    /// </summary>
    public static bool Qualifies(IEnumerable<DateOnly> dates, bool hasOpeningCredit, int movieCount, RoleTimelineRules rules)
    {
        if (rules.IncludeAll) return true;
        if (rules.IncludeOpeningCredit && hasOpeningCredit) return true;
        if (rules.MinMovies > 0 && movieCount >= rules.MinMovies) return true;
        var sorted = dates.OrderBy(d => d.DayNumber).ToList();
        int best = 0;
        for (int i = 0, j = 0; i < sorted.Count; i++)
        {
            while (sorted[i].DayNumber - sorted[j].DayNumber > rules.WindowDays) j++;
            best = Math.Max(best, i - j + 1);
        }
        return best >= rules.MinCreditsInWindow;
    }

    private static int MaxCreditsInWindow(List<Credit> credits, int windowDays)
    {
        int best = 0;
        for (int i = 0, j = 0; i < credits.Count; i++)
        {
            while (credits[i].Date.DayNumber - credits[j].Date.DayNumber > windowDays) j++;
            best = Math.Max(best, i - j + 1);
        }
        return best;
    }

    /// <summary>
    /// 歌・劇伴・盤の参加のうち最も遅い日の翌日（軸を伸ばす日。<see cref="RoleTimelineBuilder(BuildContext, DateOnly?)"/> の extendTo に渡す）。
    /// 歌・劇伴・盤の参加が無ければ null。
    /// </summary>
    public static DateOnly? ExtendToFor(IEnumerable<RoleTimelineEntity> entities)
    {
        DateOnly? last = null;
        foreach (var e in entities)
            foreach (var p in e.Songs.Concat(e.Bgms).Concat(e.Products))
                if (last is null || p.Date > last) last = p.Date;
        return last?.AddDays(1);
    }

    /// <summary>参加期間の表記（「2004年2月〜2005年1月」。同じ月なら「2004年2月」）。</summary>
    private static string PeriodLabel(DateOnly first, DateOnly last)
    {
        string a = $"{first.Year}年{first.Month}月";
        string b = $"{last.Year}年{last.Month}月";
        return a == b ? a : $"{a}〜{b}";
    }

    /// <summary>日付の軸上の位置（軸の左端からの百分率、小数 2 桁の文字列）。軸の外は端に寄せる。</summary>
    private string Pct(DateOnly d) => Num(Ratio(d) * 100);

    /// <summary>期間の軸上の幅（百分率、小数 2 桁の文字列）。</summary>
    private string PctWidth(DateOnly start, DateOnly end) => Num(Math.Max(0, Ratio(end) - Ratio(start)) * 100);

    /// <summary>
    /// 日付の軸上の位置（0〜1）。参加のある期間だけの軸では、日付の入る区間の中で置き、区間の外（切れ目・軸の外）の日付は
    /// 直前の区間の終わり（最初の区間より前なら左端）に寄せる。
    /// </summary>
    private double Ratio(DateOnly d)
    {
        if (_pieces is null)
            return Math.Clamp((double)(d.DayNumber - _axisStart.DayNumber) / _axisDays, 0, 1);
        int prevEnd = 0;
        foreach (var pc in _pieces)
        {
            if (d < pc.Start) return (double)prevEnd / _axisDays;
            if (d <= pc.End) return (double)(pc.Offset + d.DayNumber - pc.Start.DayNumber) / _axisDays;
            prevEnd = pc.Offset + (pc.End.DayNumber - pc.Start.DayNumber);
        }
        return 1;
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private enum CreditKind { Tv, Movie, Song, Bgm, Product }

    /// <summary>描く単位の参加 1 件（TV の 1 話・映画 1 本・歌 1 曲・劇伴の録音回 1 回・盤 1 点）。</summary>
    private readonly record struct Credit(DateOnly Date, CreditKind Kind, int SeriesId, int EpNo, string Title);
}

/// <summary>線表に載せる行の決まりと、凡例・説明文の言い回し（ページごと）。</summary>
/// <param name="MaxGapDays">続けて参加したとみなす（細線でつなぐ）、隣り合う参加どうしの日付の差の上限（日）。描き方だけに使い、載せる決まりには使わない。</param>
/// <param name="WindowDays">載せる決まりの「一定期間」（日）。連続するこの日数のあいだの参加を数える。</param>
/// <param name="MinCreditsInWindow">一定期間（<paramref name="WindowDays"/>）のあいだに要る参加の数の下限。</param>
/// <param name="IncludeOpeningCredit">オープニングのクレジットに出たことがあれば回数によらず載せるか。</param>
/// <param name="IncludeAll">決まりによらず候補をすべて載せるか。</param>
/// <param name="ShowAllUpTo">描ける参加のある候補がこの数以下なら、決まりによらずすべて載せる（<paramref name="IncludeAll"/> のページでは使わない）。</param>
/// <param name="Verb">凡例の動詞（「担当」「出演」「参加」）。</param>
/// <param name="FilterTip">
/// 「主な方のみ」のスイッチの説明の吹き出しに出す、主な方の条件（1 要素 1 行）。全員を載せるページは空。ページの種類ごとに固定で、
/// 登録状況（どの人が載ったか・誰がオープニングに出ているか）では変えない。
/// </param>
internal sealed record RoleTimelineRules(
    int MaxGapDays, int WindowDays, int MinCreditsInWindow, bool IncludeOpeningCredit, bool IncludeAll, int ShowAllUpTo, string Verb,
    IReadOnlyList<string> FilterTip)
{
    /// <summary>
    /// スピンオフ（大人向けスピンオフなど）の話を、本線の TV の話と別の色の帯で描くか（凡例も分ける）。人物詳細・企業/団体詳細の
    /// 年表で使う。横軸の帯と目盛りは本線の TV シリーズのまま。
    /// </summary>
    public bool SeparateSpinOffs { get; init; }

    /// <summary>
    /// 映画の本数（通算）がこの数以上なら回数・期間によらず載せる（0 なら使わない）。映画は年に 1〜2 本しかなく
    /// 「一定期間に何回」の決まりに届かないので、映画で続けて担当している方を拾うために使う。
    /// </summary>
    public int MinMovies { get; init; }

    /// <summary>
    /// 凡例の関連する役職のリンク先（役職コード → URL）。null なら本編の役職詳細（<see cref="PathUtil.CreatorsRoleUrl"/>）。
    /// オトナシリーズの役職のページでは、オトナシリーズの役職のページ（<see cref="PathUtil.CreatorsOtonaRoleUrl"/>）を指す。
    /// </summary>
    public Func<string, string>? RelatedRoleUrl { get; init; }

    /// <summary>全員を載せる決まりのページの条件（スイッチを出さない）。</summary>
    private static readonly IReadOnlyList<string> NoFilter = Array.Empty<string>();

    /// <summary>決まりで絞るページでも全員を載せる候補の数の上限（主な方を補う数でもある）。</summary>
    public const int DefaultShowAllUpTo = 10;

    /// <summary>役職詳細：メインスタッフ（役職を問わずオープニングに出た）と、1 年間（52 週）に 4 回以上担当したスタッフと、映画を 3 本以上担当したスタッフ（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules Staff = new(
        91, WindowDays: 364, MinCreditsInWindow: 4, IncludeOpeningCredit: true, IncludeAll: false, DefaultShowAllUpTo, "担当",
        new[]
        {
            "次のいずれかに当たる方",
            "・オープニングにクレジットされたことがある",
            "・52週のあいだに4回以上担当した（TVの話数・映画の本数）",
            "・映画を3本以上担当した",
            $"{DefaultShowAllUpTo}名に満たないときは担当回数の多い方から補います",
            "（同じ回数ならクレジットが早い方、同じ話なら先に出ている方）"
        })
    { MinMovies = 3, SeparateSpinOffs = true };

    /// <summary>声の出演：1 年間（52 週）に 4 回以上出演した声優（細線は 4 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules VoiceCast = new(
        28, WindowDays: 364, MinCreditsInWindow: 4, IncludeOpeningCredit: false, IncludeAll: false, DefaultShowAllUpTo, "出演",
        new[]
        {
            "52週のあいだに4回以上出演した方（TVの話数・映画の本数）",
            $"{DefaultShowAllUpTo}名に満たないときは出演回数の多い方から補います",
            "（同じ回数ならクレジットが早い方、同じ話なら先に出ている方）"
        })
    { SeparateSpinOffs = true };

    /// <summary>作詞・作曲・編曲の役職詳細：1 年間（52 週）に 2 曲以上担当した人物（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules SongWriter = new(
        91, WindowDays: 364, MinCreditsInWindow: 2, IncludeOpeningCredit: false, IncludeAll: false, DefaultShowAllUpTo, "担当",
        new[]
        {
            "52週のあいだに2曲以上担当した方",
            $"{DefaultShowAllUpTo}名に満たないときは担当曲数の多い方から補います",
            "（同じ曲数なら担当が早い方）"
        });

    /// <summary>音楽の役職詳細（演奏など）：一覧に載る人物・団体すべて（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules MusicRole = new(
        91, WindowDays: 0, MinCreditsInWindow: 0, IncludeOpeningCredit: false, IncludeAll: true, ShowAllUpTo: 0, "担当", NoFilter);

    /// <summary>
    /// 人物詳細・企業/団体詳細：その人・その会社の役職・役柄の行すべて（続けて参加した期間の線は 13 週以内の間隔でつなぐ）。
    /// 凡例の動詞は呼び出し側が行の顔ぶれに合わせて差し替える（<c>with { Verb = … }</c>）。
    /// </summary>
    public static readonly RoleTimelineRules Credit = new(
        91, WindowDays: 0, MinCreditsInWindow: 0, IncludeOpeningCredit: false, IncludeAll: true, ShowAllUpTo: 0, "参加", NoFilter)
    { SeparateSpinOffs = true };

    /// <summary>歌唱：一覧に載る歌手・キャラクターすべて（続けて参加した期間の線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules Singers = new(
        91, WindowDays: 0, MinCreditsInWindow: 0, IncludeOpeningCredit: false, IncludeAll: true, ShowAllUpTo: 0, "参加", NoFilter);
}

/// <summary>線表に載せる候補 1 つ分（<see cref="RoleTimelineBuilder.Build"/> の入力）。</summary>
internal sealed class RoleTimelineEntity
{
    /// <summary>"person" / "company" / "singer" / "character" / "unit" / "role"（人物・企業/団体詳細の役職の行）（行頭のアイコンと、絞り込みの種類）。</summary>
    public required string EntityKind { get; init; }
    /// <summary>行に出す名前（人物は表示名義、企業は最後に使われた屋号、キャラクターは一覧と同じ名義）。</summary>
    public required string EntityName { get; init; }
    /// <summary>内訳で名前の下に添える 1 行（キャラクターの「CV: 声優」など）。無ければ空文字。</summary>
    public string EntitySubLabel { get; init; } = "";
    /// <summary>名前のリンク先（人物・企業・キャラクター詳細）。</summary>
    public required string EntityUrl { get; init; }
    /// <summary>
    /// 最初と最後の参加の日付が同じ行どうしの並びのキー（小さいほうが先）。役職詳細・声の出演は初めてクレジットされた話の中での
    /// クレジットの位置（その話のクレジットで上に出ている順）、歌唱・作詞作曲編曲は初参加の録音、音楽の役職詳細は最初の担当先の日付。
    /// </summary>
    public long FirstSortPos { get; init; }
    /// <summary>役職を問わずオープニングのクレジットに出たことがあるか（役職詳細のメインスタッフの判定）。</summary>
    public bool HasOpeningCredit { get; init; }
    /// <summary>
    /// 所属の人をまとめる見出しになる行の鍵（役職詳細の企業の行。"company:{company_id}"）。まとめないものは空文字。
    /// 事業の引き継ぎでつながった企業は、系譜の代表の企業の鍵を共有する（同じ鍵の行は並べてひとまとめにする）。
    /// </summary>
    public string GroupKey { get; init; } = "";
    /// <summary>企業の行の、その企業自身の鍵（"company:{company_id}"。系譜の企業でも系譜の代表ではなく自分）。企業以外は空文字。</summary>
    public string CompanyKey { get; init; } = "";
    /// <summary>
    /// 所属先の会社の <see cref="GroupKey"/>（役職詳細で、その役職を会社の所属としてクレジットされたことのある人物）。
    /// 「すべて」を見せているときは会社の行のすぐ下に字下げして置く。所属していなければ空文字。
    /// </summary>
    public string MemberOf { get; init; } = "";
    /// <summary>
    /// 企業の所属としてクレジットされた担当（企業ごと）。行の帯の下に所属線を引き、内訳に「所属：企業名（期間）」を添える。
    /// まとめた企業（<see cref="MemberOf"/>）の線は実線、ほかの企業の線は点線。役職詳細以外のページでは空。
    /// </summary>
    public IReadOnlyList<RoleTimelineAffiliation> Affiliations { get; init; } = Array.Empty<RoleTimelineAffiliation>();
    /// <summary>参加した TV 系の話 (series_id, episode_id)。</summary>
    public IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes { get; init; } = Array.Empty<(int, int)>();
    /// <summary>参加した映画系のシリーズ。</summary>
    public IReadOnlyCollection<int> MovieSeriesIds { get; init; } = Array.Empty<int>();
    /// <summary>参加した歌（初めて盤に収められた日と曲名）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Songs { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>参加した劇伴の録音回（初めて盤に収められた日と「作品名（録音回）」）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Bgms { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>参加した盤（発売日と商品名）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Products { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>内訳でシリーズ・映画の後ろに括弧で添える文（series_id → 文。声優の演じたキャラなど）。</summary>
    public IReadOnlyDictionary<int, string>? SeriesNotes { get; init; }
    /// <summary>
    /// 役職詳細で、ページの役職と関連（段階・並列）でつながった役職の担当。主の帯の下に重ねて描くだけで、
    /// 行の並び・主な方の判定には使わない。役職詳細以外のページでは空。
    /// </summary>
    public IReadOnlyList<RoleTimelineRelatedRole> RelatedRoles { get; init; } = Array.Empty<RoleTimelineRelatedRole>();
}

/// <summary>人物が 1 つの企業の所属としてクレジットされた担当（<see cref="RoleTimelineEntity.Affiliations"/>）。</summary>
internal sealed class RoleTimelineAffiliation
{
    /// <summary>企業の鍵（<see cref="RoleTimelineEntity.GroupKey"/> と同じ。系譜の企業は系譜の代表の鍵）。</summary>
    public required string GroupKey { get; init; }
    /// <summary>その企業自身の鍵（<see cref="RoleTimelineEntity.CompanyKey"/> と同じ。系譜の企業ごとに所属線の色を変えるのに使う）。</summary>
    public required string CompanyKey { get; init; }
    /// <summary>内訳に出す企業名。</summary>
    public required string CompanyName { get; init; }
    /// <summary>所属としてクレジットされた TV 系の話 (series_id, episode_id)。</summary>
    public IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes { get; init; } = Array.Empty<(int, int)>();
    /// <summary>所属としてクレジットされた映画系のシリーズ。</summary>
    public IReadOnlyCollection<int> MovieSeriesIds { get; init; } = Array.Empty<int>();
}

/// <summary>線表の行に重ねる関連する役職 1 つの担当（<see cref="RoleTimelineEntity.RelatedRoles"/>）。</summary>
internal sealed class RoleTimelineRelatedRole
{
    /// <summary>ページの役職から見た関連の種類（帯の色相の基準）。</summary>
    public RoleRelationLane Lane { get; init; }
    /// <summary>役職（系譜の代表 role_code）。凡例のリンク先に使う。</summary>
    public required string RoleCode { get; init; }
    /// <summary>役職名（凡例と内訳に出す）。</summary>
    public required string RoleName { get; init; }
    /// <summary>役職マスタの表示順（凡例・内訳・色の割り当てで同じ種類の役職を並べる順）。</summary>
    public int SortOrder { get; init; }
    /// <summary>担当した TV 系の話 (series_id, episode_id)。</summary>
    public IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes { get; init; } = Array.Empty<(int, int)>();
    /// <summary>担当した映画系のシリーズ。</summary>
    public IReadOnlyCollection<int> MovieSeriesIds { get; init; } = Array.Empty<int>();
}

/// <summary>線表に点で描く参加 1 件（歌・劇伴・盤の日付と、内訳に出す名前）。</summary>
internal readonly record struct RoleTimelinePoint(DateOnly Date, string Title);

/// <summary>線表の表示モデル。位置・幅は軸の左端からの百分率（小数 2 桁の文字列）。</summary>
internal sealed class RoleTimelineModel
{
    /// <summary>「主な方のみ」のスイッチを出すか（候補が <see cref="RoleTimelineRules.ShowAllUpTo"/> を超える、決まりで絞るページ）。</summary>
    public bool IsFilterable { get; set; }
    /// <summary>スイッチの説明の吹き出しに出す、主な方の条件（1 要素 1 行）。</summary>
    public IReadOnlyList<string> FilterTip { get; set; } = Array.Empty<string>();
    /// <summary>主な方の行の数（スイッチの横の「14 / 38名」の左。右は <see cref="Rows"/> の数）。</summary>
    public int MainCount { get; set; }
    /// <summary>凡例（描いた印の種類だけ）。</summary>
    public IReadOnlyList<RoleTimelineLegendItem> Legend { get; set; } = Array.Empty<RoleTimelineLegendItem>();
    /// <summary>TV シリーズの帯（交互に薄く塗る）。</summary>
    public IReadOnlyList<RoleTimelineBand> Bands { get; set; } = Array.Empty<RoleTimelineBand>();
    /// <summary>参加のある期間だけの軸で、続かない帯のあいだに置く切れ目（それ以外の線表では空）。</summary>
    public IReadOnlyList<RoleTimelineGap> Gaps { get; set; } = Array.Empty<RoleTimelineGap>();
    /// <summary>年の目盛り（帯ごとに 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineTick> Ticks { get; set; } = Array.Empty<RoleTimelineTick>();
    /// <summary>帯が多いときに目盛りの年を下 2 桁にする CSS クラス（"rtl-ticks-medium" は狭い画面だけ / "rtl-ticks-sparse" は常に / 空）。</summary>
    public string TickDensityClass { get; set; } = "";
    /// <summary>行（並びは参加の始まり → 終わりの早い順）。</summary>
    public IReadOnlyList<RoleTimelineRow> Rows { get; set; } = Array.Empty<RoleTimelineRow>();
    /// <summary>
    /// 会社とその所属の人をまとめた行があるか。あるときの <see cref="Rows"/> はまとめた並び（「すべて」のとき）で、
    /// 個人・団体で絞り込んだときは各行の <see cref="RoleTimelineRow.FlatOrder"/> の順に見せる（site.css）。
    /// </summary>
    public bool HasGroups { get; set; }
}

/// <summary>
/// 凡例 1 項目。Kind は "tv" / "spinoff" / "movie" / "song" / "bgm" / "disc" / "span"、関連する役職は "rel"（TV の話の細い帯）か
/// "rel-movie"（映画の白抜きの輪）（印の見本の CSS クラス）。関連する役職の見本の色は <see cref="Color"/>。
/// </summary>
internal sealed class RoleTimelineLegendItem
{
    public string Kind { get; set; } = "";
    /// <summary>
    /// 2 つ目の見本の CSS クラスの接尾（関連する役職で TV の話と映画の両方を描いたとき、帯の見本に続けて映画の輪の見本
    /// "rel-movie" を並べる）。無ければ空文字。
    /// </summary>
    public string ExtraKind { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>関連する役職の見本の色（CSS の色）。それ以外の項目では空文字。</summary>
    public string Color { get; set; } = "";
    /// <summary>ラベルのリンク先（関連する役職の役職詳細）。無ければ空文字。</summary>
    public string Url { get; set; } = "";
}

/// <summary>線表の TV シリーズの帯 1 本。</summary>
internal sealed class RoleTimelineBand
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
    /// <summary>交互に薄く塗る側の帯か。</summary>
    public bool IsAlt { get; set; }
}

/// <summary>参加のある期間だけの軸の切れ目 1 つ（参加の無い期間を詰めた所）。</summary>
internal sealed class RoleTimelineGap
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
}

/// <summary>線表の年の目盛り 1 つ（TV シリーズの放送開始の位置）。</summary>
internal sealed class RoleTimelineTick
{
    public string Left { get; set; } = "";
    public string YearLabel { get; set; } = "";
    /// <summary>シリーズの正式タイトル（目盛りの title 属性に出す）。</summary>
    public string SeriesTitle { get; set; } = "";
    public string SeriesUrl { get; set; } = "";
    /// <summary>年の文字を出すか（帯の幅が足りない軸の右端では出さない）。</summary>
    public bool ShowLabel { get; set; }
}

/// <summary>所属線 1 本（企業の所属としてクレジットされていた 1 つの期間）。</summary>
internal sealed class RoleTimelineAffiliationMark
{
    public string Left { get; set; } = "0";
    public string Width { get; set; } = "0";
    /// <summary>年表でまとめた企業とは別の企業の所属か（点線で引く）。</summary>
    public bool IsOther { get; set; }
    /// <summary>所属した企業の鍵（<see cref="RoleTimelineAffiliation.CompanyKey"/>）。</summary>
    public string CompanyKey { get; set; } = "";
    /// <summary>線の色（CSS の色。系譜の 2 つ目からの企業だけ。空ならふだんの所属線の色）。</summary>
    public string Color { get; set; } = "";
    /// <summary>線を引く段（0 が一番下。違う企業の線が同じ時期に重なるとき 1 段ずつ上げる）。</summary>
    public int Lane { get; set; }
    /// <summary>スピンオフの段（<see cref="RoleTimelineRow.SpinOffLane"/>）の下に引く線か。偽なら本線の段（段を分けない行は行の下）。</summary>
    public bool InSpinOffLane { get; set; }
}

/// <summary>線表の 1 行（人物・企業/団体・キャラクター 1 つ）。</summary>
internal sealed class RoleTimelineRow
{
    public string EntityKind { get; set; } = "";
    public string EntityName { get; set; } = "";
    /// <summary>内訳で名前の下に添える 1 行（無ければ空文字）。</summary>
    public string EntitySubLabel { get; set; } = "";
    public string EntityUrl { get; set; } = "";
    /// <summary>主な方の行か（「主な方のみ」のスイッチが入っているときに見せる行。スイッチの無いページではすべて true）。</summary>
    public bool IsMain { get; set; } = true;
    /// <summary>所属の人をまとめた会社の行か（「すべて」のときのゾーンの見出し）。</summary>
    public bool IsGroupHead { get; set; }
    /// <summary>ゾーンの見出しのうち、系譜の 2 つ目以降の会社の行か（前の会社の行と区切らずに続ける）。</summary>
    public bool IsGroupSuccessor { get; set; }
    /// <summary>系譜の会社の行で、名前に所属線の色の下線を引くか。</summary>
    public bool HasAffChip { get; set; }
    /// <summary>名前の下線の色（CSS の色。空ならふだんの所属線の色）。</summary>
    public string AffColor { get; set; } = "";
    /// <summary>会社の行の下にまとめた所属の人の行か（「すべて」のときは字下げする）。</summary>
    public bool IsGroupMember { get; set; }
    /// <summary>まとめる前の並びでの位置（個人・団体で絞り込んだときの並び。<see cref="RoleTimelineModel.HasGroups"/> のときだけ使う）。</summary>
    public int FlatOrder { get; set; }
    /// <summary>本線の TV の話を描いた行か（📺 を入れているときに見せる行。関連する役職の担当は数えない）。</summary>
    public bool HasTv => Segments.Count > 0;
    /// <summary>スピンオフ（オトナシリーズなど）の話を別の色の帯で描いた行か（🌙 を入れているときに見せる行。関連する役職の担当は数えない）。</summary>
    public bool HasOtona => SpinOffSegments.Count > 0;

    /// <summary>
    /// 映画を別の段に描くか。映画（関連する役職の映画の輪を含む）の公開日が本線の TV の話の帯（関連する役職の帯を含む）と重なる時期がある行だけ立つ
    /// （帯と点が重なって見えなくなるため）。立つ行では、関連する役職の映画の輪も映画の段に描く。
    /// </summary>
    public bool MovieLane { get; set; }

    /// <summary>
    /// スピンオフの話の帯を別の段に描くか。スピンオフの帯と本線の印（TV の話・映画・歌・劇伴・盤）の両方がある行だけ立つ。
    /// </summary>
    public bool SpinOffLane => SpinOffSegments.Count > 0
        && (Segments.Count > 0 || Movies.Count > 0 || Songs.Count > 0 || Bgms.Count > 0 || Products.Count > 0);

    /// <summary>
    /// 行の段の数（1〜3）。段は上から本線（TV の話・歌・劇伴・盤）、映画（<see cref="MovieLane"/>）、スピンオフ（<see cref="SpinOffLane"/>）。
    /// 名前は段の数によらず行の真ん中に 1 つ。
    /// </summary>
    public int LaneCount => 1 + (MovieLane ? 1 : 0) + (SpinOffLane ? 1 : 0);
    /// <summary>映画を描いた行か（🎥 を入れているときに見せる行。関連する役職の担当は数えない）。</summary>
    public bool HasMovie => Movies.Count > 0;
    /// <summary>会社の行（<see cref="IsGroupHead"/>）の下にまとめた所属の人に、TV の話を描いた行があるか。</summary>
    public bool ZoneHasTv { get; set; }
    /// <summary>会社の行（<see cref="IsGroupHead"/>）の下にまとめた所属の人に、映画を描いた行があるか。</summary>
    public bool ZoneHasMovie { get; set; }
    /// <summary>会社の行（<see cref="IsGroupHead"/>）の下にまとめた所属の人に、オトナシリーズの話を描いた行があるか。</summary>
    public bool ZoneHasOtona { get; set; }
    /// <summary>続けて参加した期間（2 件以上つながったもの）の細線。</summary>
    public IReadOnlyList<RoleTimelineMark> Spans { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>TV の話の帯（話数が続く間はひと続き）。</summary>
    public IReadOnlyList<RoleTimelineMark> Segments { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>スピンオフの話の帯（<see cref="RoleTimelineRules.SeparateSpinOffs"/> の線表だけ。ほかの線表では <see cref="Segments"/> に入る）。</summary>
    public IReadOnlyList<RoleTimelineMark> SpinOffSegments { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>
    /// <see cref="SpinOffSegments"/> に、オトナシリーズ（大人向けスピンオフ）以外のスピンオフの話の帯が入っているか。
    /// 凡例のスピンオフの帯の項目を「オトナ」にするか「スピンオフ」にするかを決める。
    /// </summary>
    public bool HasNonOtonaSpinOff { get; set; }
    /// <summary>映画の点（<see cref="RoleTimelineMark.Width"/> は使わない）。</summary>
    public IReadOnlyList<RoleTimelineMark> Movies { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>歌の点（同じ日に出た曲は 1 つ。<see cref="RoleTimelineMark.Width"/> は使わない）。</summary>
    public IReadOnlyList<RoleTimelineMark> Songs { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>劇伴の点（同じ日のものは 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineMark> Bgms { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>盤の四角（同じ日のものは 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineMark> Products { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>関連する役職の担当（主の帯の下に重ねる細い帯・白抜きの輪）。</summary>
    public IReadOnlyList<RoleTimelineRelatedMark> Related { get; set; } = Array.Empty<RoleTimelineRelatedMark>();
    /// <summary>所属線（企業の所属としてクレジットされていた期間。帯の下に引く）。</summary>
    public IReadOnlyList<RoleTimelineAffiliationMark> Affiliations { get; set; } = Array.Empty<RoleTimelineAffiliationMark>();
    /// <summary>内訳の「所属：企業名（期間）」（企業ごとに改行でつないだもの。無ければ空文字）。</summary>
    public string AffiliationText { get; set; } = "";
    /// <summary>
    /// プリキュア（プリキュアのマスタのキャラ）を演じた方の行か。歴代プリキュア声優の「プリキュアを演じた方のみ」の絞り込みに使う
    /// （行の data-precure-cast。ほかのページでは常に false）。
    /// </summary>
    public bool IsPrecureCast { get; set; }
    /// <summary>
    /// 「プリキュアを演じた方のみ」のときの並び順（演じたプリキュアのプリキュアのマスタの順。1 始まり、プリキュアを演じていない行は 0）。
    /// 行の --rtl-precure-order で渡し、site.css が絞り込み中だけ並べ替えに使う。
    /// </summary>
    public int PrecureOrder { get; set; }
    /// <summary>内訳の参加期間（「2004年2月〜2005年1月」を「、」でつないだもの）。</summary>
    public string PeriodLabel { get; set; } = "";
    /// <summary>内訳の作品ごとの参加（改行でつないだもの）。</summary>
    public string WorksText { get; set; } = "";
}

/// <summary>線表上の印 1 つの位置と幅。</summary>
internal sealed class RoleTimelineMark
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
    /// <summary>映画の点を上下にずらす向き（-1 で上、1 で下、0 でずらさない）。違う役職の映画の丸と重なりそうなときだけ立つ。</summary>
    public int Shift { get; set; }
}

/// <summary>関連する役職の担当の印 1 つ（TV の話の細い帯、または映画の白抜きの輪）。</summary>
internal sealed class RoleTimelineRelatedMark
{
    /// <summary>役職の色（CSS の色。<see cref="RoleTimelineBuilder"/> が役職ごとに割り当てる）。</summary>
    public string Color { get; set; } = "";
    public string Left { get; set; } = "";
    /// <summary>帯の幅（映画の輪では使わない）。</summary>
    public string Width { get; set; } = "";
    /// <summary>映画の輪か。</summary>
    public bool IsMovie { get; set; }
    /// <summary>スピンオフ（オトナシリーズなど）の話の帯か（🌙 を切っているときに隠す）。</summary>
    public bool IsSpinOff { get; set; }
    /// <summary>映画の輪を上下にずらす向き（-1 で上、1 で下、0 でずらさない）。違う役職の映画の丸と重なりそうなときだけ立つ。</summary>
    public int Shift { get; set; }
}
