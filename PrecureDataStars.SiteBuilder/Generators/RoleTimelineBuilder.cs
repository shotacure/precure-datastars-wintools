using System.Globalization;
using System.Text;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 役職詳細の「担当の移り変わり」（線表）を組み立てる。
/// <list type="bullet">
///   <item><description>横軸は時期。クレジットを収録している全期間（最初の TV シリーズの放送開始から、
///     クレジットを収録した最新の TV の話・映画まで）で、全役職で同じ軸を使う。
///     TV シリーズ 1 作を 1 本の帯にし、帯の左端に放送開始年の目盛りを置く。</description></item>
///   <item><description>行は人物・企業/団体。担当を日付順に並べ、隣り合う担当の差が <see cref="MaxGapDays"/> 日（13 週）
///     以内ならつないで「続けて担当した期間」とする。載せるのは次のどちらかに当たるもの。
///     <list type="number">
///       <item><description>その役職で TV 系シリーズのオープニングのクレジットに出たことがある（メイン級のスタッフ）。
///         映画のオープニングは数えない（映画にしか出ない人まで入れると収拾がつかないため）。</description></item>
///       <item><description>続けて担当した期間のうち 1 つでも <see cref="MinPeriodDays"/> 日（3 クール = 39 週）以上に伸びている。</description></item>
///     </list>
///     載せた行には単発の担当も含めてすべての担当を描く。</description></item>
///   <item><description>並びは、最初の担当の日付の早い順、同じなら最後の担当の日付の早い順
///     （→ 初めてクレジットされた位置 → 名前）。</description></item>
///   <item><description>描くものは、続けて担当した期間（2 件以上つながったものの細線）・担当した TV の話
///     （連続する話はひと続きの帯）・映画（公開日の点）。TV の 1 話は放送日から <see cref="EpisodeSpanDays"/> 日の幅を占める。</description></item>
///   <item><description>畳んだ状態の概観（文字のない細線だけの図）は、全行の続けて担当した期間（単発の担当はその幅）の
///     線だけを展開時と同じ順に描く。1 行 <see cref="MiniPitch"/>px で <see cref="MiniMaxHeight"/>px（本文 4 行ぶん）に
///     収まればそのまま、収まらなければ行の間隔を詰めて <see cref="MiniMaxHeight"/>px に押し込む。</description></item>
/// </list>
/// 軸は全役職で共通なので、インスタンスを 1 つ作って役職ごとに <see cref="Build"/> を呼ぶ。
/// </summary>
internal sealed class RoleTimelineBuilder
{
    /// <summary>続けて担当したとみなす、隣り合う担当どうしの日付の差の上限（日）。13 週。</summary>
    public const int MaxGapDays = 91;

    /// <summary>
    /// オープニングに出ていない人物・企業/団体を載せるのに要る、続けて担当した期間の長さの下限（日）。3 クール（39 週）。
    /// 期間の長さは最初の担当の日付から最後の担当の放送枠の終わりまで（第 1 話から第 39 話まで毎週担当すればちょうど届く）。
    /// </summary>
    public const int MinPeriodDays = 273;

    /// <summary>TV の 1 話が線表上で占める幅（日）。週 1 回の放送枠ぶん。</summary>
    private const int EpisodeSpanDays = 7;

    /// <summary>畳んだ概観の 1 行の高さ（px）。行が多くて <see cref="MiniMaxHeight"/> に収まらないときは詰める。</summary>
    private const double MiniPitch = 4;

    /// <summary>畳んだ概観の線の太さの、1 行の高さに対する割合。</summary>
    private const double MiniThicknessRatio = 0.5;

    /// <summary>行を詰めたときの線の太さの下限（px）。これより細いと線が消えて見えるので、隣の行と重なっても保つ。</summary>
    private const double MiniMinThickness = 0.6;

    /// <summary>畳んだ概観の縦幅の上限（px）。本文（16px・行の高さ 1.65）の 4 行ぶん。</summary>
    private const double MiniMaxHeight = 104;

    /// <summary>畳んだ概観の横の座標の幅（SVG の viewBox 上の単位。横は表示幅いっぱいに伸ばす）。</summary>
    private const double MiniWidth = 1000;

    /// <summary>
    /// 年の目盛りの文字を、5 の倍数の年以外も出せる帯の数の上限。
    /// これを超えると狭い画面（768px 以下）で 5 の倍数の年だけに、<see cref="AllTickBandsMaxWide"/> を超えると常に 5 の倍数の年だけにする。
    /// </summary>
    private const int AllTickBandsMaxNarrow = 10;

    /// <summary>広い画面でも年の目盛りの文字を全部出せる帯の数の上限。</summary>
    private const int AllTickBandsMaxWide = 22;

    private readonly BuildContext _ctx;
    private readonly DateOnly _axisStart;
    private readonly int _axisDays;
    private readonly IReadOnlyList<RoleTimelineBand> _bands;
    private readonly IReadOnlyList<RoleTimelineTick> _ticks;
    private readonly string _tickDensityClass;
    private readonly string _miniBandsPath;

    /// <summary>軸（期間・シリーズの帯・年の目盛り）を確定させる。収録しているクレジットが無ければ軸を持たず、<see cref="Build"/> は常に null を返す。</summary>
    public RoleTimelineBuilder(BuildContext ctx)
    {
        _ctx = ctx;
        _bands = Array.Empty<RoleTimelineBand>();
        _ticks = Array.Empty<RoleTimelineTick>();
        _tickDensityClass = "";
        _miniBandsPath = "";

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

        var tvSeries = ctx.SeriesById.Values
            .Where(s => string.Equals(s.KindCode, "TV", StringComparison.Ordinal) && s.StartDate < axisEnd)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();
        if (tvSeries.Count == 0) return;

        _axisStart = tvSeries[0].StartDate;
        _axisDays = axisEnd.DayNumber - _axisStart.DayNumber;
        if (_axisDays <= 0) return;

        var bands = new List<RoleTimelineBand>(tvSeries.Count);
        var ticks = new List<RoleTimelineTick>(tvSeries.Count);
        var miniBands = new StringBuilder();
        bool manyBands = tvSeries.Count > AllTickBandsMaxNarrow;
        for (int i = 0; i < tvSeries.Count; i++)
        {
            var s = tvSeries[i];
            var bandEnd = i + 1 < tvSeries.Count ? tvSeries[i + 1].StartDate : axisEnd;
            if (bandEnd > axisEnd) bandEnd = axisEnd;
            bool alt = i % 2 == 1;
            bands.Add(new RoleTimelineBand
            {
                Left = Pct(s.StartDate),
                Width = PctWidth(s.StartDate, bandEnd),
                IsAlt = alt
            });
            if (alt)
            {
                // 概観の帯は横だけ伸ばす SVG に描く（縦は 0〜1 を CSS 側の高さいっぱいに伸ばす）。
                miniBands.Append('M').Append(Num(MiniX(s.StartDate))).Append(" 0H")
                    .Append(Num(MiniX(bandEnd))).Append("V1H").Append(Num(MiniX(s.StartDate))).Append('Z');
            }
            // 軸の右端で幅の足りない帯（収録が始まったばかりのシリーズ）は、目盛りの文字がはみ出すので出さない。
            bool showLabel = (bandEnd.DayNumber - s.StartDate.DayNumber) >= 120;
            ticks.Add(new RoleTimelineTick
            {
                Left = Pct(s.StartDate),
                YearLabel = s.StartDate.Year.ToString(CultureInfo.InvariantCulture),
                SeriesTitle = s.Title,
                SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                ShowLabel = showLabel,
                IsMinor = manyBands && s.StartDate.Year % 5 != 0
            });
        }
        _bands = bands;
        _ticks = ticks;
        _miniBandsPath = miniBands.ToString();
        _tickDensityClass = tvSeries.Count > AllTickBandsMaxWide ? "rtl-ticks-sparse"
            : manyBands ? "rtl-ticks-medium"
            : "";
    }

    /// <summary>
    /// 1 役職分の線表を組み立てる。載せる行が 1 つも無ければ null（テンプレは節を出さない）。
    /// </summary>
    /// <param name="entities">役職に関わった人物・企業/団体（担当量はエンティティの全名義の合算）。</param>
    public RoleTimelineModel? Build(IEnumerable<RoleTimelineEntity> entities)
    {
        if (_axisDays <= 0) return null;

        var rows = new List<(RoleTimelineRow Row, DateOnly First, DateOnly Last, long FirstPos)>();
        foreach (var e in entities)
        {
            var built = BuildRow(e);
            if (built is not null) rows.Add(built.Value);
        }
        if (rows.Count == 0) return null;

        var ordered = rows
            .OrderBy(r => r.First)
            .ThenBy(r => r.Last)
            .ThenBy(r => r.FirstPos)
            .ThenBy(r => r.Row.EntityName, StringComparer.Ordinal)
            .ToList();

        // 畳んだ概観：全行を同じ並びで描く。縦幅の上限に収まらなければ行の間隔を詰める。
        double pitch = Math.Min(MiniPitch, MiniMaxHeight / ordered.Count);
        double thickness = Math.Max(MiniMinThickness, pitch * MiniThicknessRatio);
        double miniHeight = Math.Round(pitch * ordered.Count, 2);
        var miniLines = new StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            double y = i * pitch + (pitch - thickness) / 2;
            foreach (var p in ordered[i].Row.Periods)
            {
                double x0 = MiniX(p.Start), x1 = MiniX(p.End);
                miniLines.Append('M').Append(Num(x0)).Append(' ').Append(Num(y))
                    .Append('H').Append(Num(x1))
                    .Append('V').Append(Num(y + thickness))
                    .Append('H').Append(Num(x0)).Append('Z');
            }
        }

        return new RoleTimelineModel
        {
            Bands = _bands,
            Ticks = _ticks,
            TickDensityClass = _tickDensityClass,
            Rows = ordered.Select(r => r.Row).ToList(),
            MiniHeight = Num(miniHeight),
            MiniViewBox = $"0 0 {Num(MiniWidth)} {Num(miniHeight)}",
            MiniBandsPath = _miniBandsPath,
            MiniLinesPath = miniLines.ToString()
        };
    }

    /// <summary>
    /// 1 エンティティの担当を日付順に並べて「続けて担当した期間」に区切り、描く行を作る。
    /// オープニングに出たことがなく、<see cref="MinPeriodDays"/> 日以上に伸びた期間も無ければ載せない（null）。
    /// </summary>
    private (RoleTimelineRow Row, DateOnly First, DateOnly Last, long FirstPos)? BuildRow(RoleTimelineEntity e)
    {
        var credits = new List<Credit>(e.Episodes.Count + e.MovieSeriesIds.Count);
        foreach (var (sid, eid) in e.Episodes)
        {
            // シリーズ全体に付いた TV 系のクレジット（話の無いもの）は日付を持たないので描かない。
            if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
            credits.Add(new Credit(ep.OnAirDate, false, sid, ep.SeriesEpNo));
        }
        foreach (var sid in e.MovieSeriesIds)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            credits.Add(new Credit(s.StartDate, true, sid, 0));
        }
        if (credits.Count == 0) return null;
        credits.Sort((a, b) =>
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.SeriesId.CompareTo(b.SeriesId);
            return c != 0 ? c : a.EpNo.CompareTo(b.EpNo);
        });

        // 隣り合う担当の日付の差が MaxGapDays 以内なら同じ期間（単発の担当は 1 件だけの期間になる）。
        var chains = new List<List<Credit>>();
        var cur = new List<Credit> { credits[0] };
        for (int i = 1; i < credits.Count; i++)
        {
            if (credits[i].Date.DayNumber - cur[^1].Date.DayNumber <= MaxGapDays)
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

        // 期間の終わり：最後の担当の放送枠の終わり（映画は公開日の翌日）。
        static DateOnly ChainEnd(List<Credit> chain)
            => chain.Max(c => c.IsMovie ? c.Date.AddDays(1) : c.Date.AddDays(EpisodeSpanDays));
        if (!e.HasOpeningCredit
            && !chains.Any(c => ChainEnd(c).DayNumber - c[0].Date.DayNumber >= MinPeriodDays))
        {
            return null;
        }

        var periods = new List<RoleTimelinePeriod>(chains.Count);
        var spans = new List<RoleTimelineMark>(chains.Count);
        var segments = new List<RoleTimelineMark>();
        var movies = new List<RoleTimelineMark>();
        var periodLabels = new List<string>(chains.Count);
        var drawnTv = new Dictionary<int, SortedSet<int>>();
        var drawnMovies = new HashSet<int>();

        foreach (var chain in chains)
        {
            var start = chain[0].Date;
            var end = ChainEnd(chain);
            periods.Add(new RoleTimelinePeriod(start, end));
            // 細線は 2 件以上つながった期間だけに引く（単発の担当は帯・点だけ）。
            if (chain.Count >= 2)
                spans.Add(new RoleTimelineMark { Left = Pct(start), Width = PctWidth(start, end) });
            periodLabels.Add(PeriodLabel(start, chain[^1].Date));

            // TV の話は、同じシリーズで話数が続く間をひと続きの帯にまとめる。
            foreach (var bySeries in chain.Where(c => !c.IsMovie).GroupBy(c => c.SeriesId))
            {
                var eps = bySeries.OrderBy(c => c.EpNo).ToList();
                int runStart = 0;
                for (int i = 1; i <= eps.Count; i++)
                {
                    if (i < eps.Count && eps[i].EpNo == eps[i - 1].EpNo + 1) continue;
                    var segStart = eps[runStart].Date;
                    var segEnd = eps[i - 1].Date.AddDays(EpisodeSpanDays);
                    segments.Add(new RoleTimelineMark { Left = Pct(segStart), Width = PctWidth(segStart, segEnd) });
                    runStart = i;
                }
                if (!drawnTv.TryGetValue(bySeries.Key, out var set))
                {
                    set = new SortedSet<int>();
                    drawnTv[bySeries.Key] = set;
                }
                foreach (var c in eps) set.Add(c.EpNo);
            }
            foreach (var c in chain.Where(c => c.IsMovie))
            {
                movies.Add(new RoleTimelineMark { Left = Pct(c.Date) });
                drawnMovies.Add(c.SeriesId);
            }
        }

        // 内訳：担当をシリーズの放送・公開順に「📺 シリーズ名 #1～49」「🎥 映画名」で 1 行ずつ。
        var works = drawnTv.Keys.Concat(drawnMovies)
            .Distinct()
            .Where(sid => _ctx.SeriesById.ContainsKey(sid))
            .Select(sid => _ctx.SeriesById[sid])
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(s => drawnTv.TryGetValue(s.SeriesId, out var nos)
                ? $"📺 {s.Title} {EpisodeRangeCompressor.Compress(nos)}"
                : $"🎥 {s.Title}");

        var row = new RoleTimelineRow
        {
            EntityKind = e.EntityKind,
            EntityName = e.EntityName,
            EntityUrl = e.EntityUrl,
            Spans = spans,
            Segments = segments,
            Movies = movies,
            Periods = periods,
            PeriodLabel = string.Join("、", periodLabels),
            WorksText = string.Join("\n", works)
        };
        return (row, chains[0][0].Date, chains[^1][^1].Date, e.FirstSortPos);
    }

    /// <summary>担当期間の表記（「2004年2月〜2005年1月」。同じ月なら「2004年2月」）。</summary>
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

    private double MiniX(DateOnly d) => Ratio(d) * MiniWidth;

    private double Ratio(DateOnly d)
        => Math.Clamp((double)(d.DayNumber - _axisStart.DayNumber) / _axisDays, 0, 1);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>描く単位の担当 1 件（TV の 1 話、または映画 1 本）。</summary>
    private readonly record struct Credit(DateOnly Date, bool IsMovie, int SeriesId, int EpNo);
}

/// <summary>線表に載せる候補の人物・企業/団体 1 つ分（<see cref="RoleTimelineBuilder.Build"/> の入力）。</summary>
/// <param name="EntityKind">"person" / "company"。</param>
/// <param name="EntityName">行に出す名前（人物は表示名義、企業は最後に使われた屋号）。</param>
/// <param name="EntityUrl">人物・企業詳細の URL。</param>
/// <param name="FirstSortPos">初めてクレジットされた話の中での位置（並びの最後の手前のタイブレーク）。</param>
/// <param name="HasOpeningCredit">その役職で TV 系シリーズのオープニングのクレジットに出たことがあるか（映画のオープニングは含めない）。</param>
/// <param name="Episodes">担当した TV 系の話 (series_id, episode_id)。</param>
/// <param name="MovieSeriesIds">担当した映画系のシリーズ。</param>
internal sealed record RoleTimelineEntity(
    string EntityKind,
    string EntityName,
    string EntityUrl,
    long FirstSortPos,
    bool HasOpeningCredit,
    IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes,
    IReadOnlyCollection<int> MovieSeriesIds);

/// <summary>役職詳細の線表の表示モデル。位置・幅は軸の左端からの百分率（小数 2 桁の文字列）。</summary>
internal sealed class RoleTimelineModel
{
    /// <summary>TV シリーズの帯（交互に薄く塗る）。</summary>
    public IReadOnlyList<RoleTimelineBand> Bands { get; set; } = Array.Empty<RoleTimelineBand>();
    /// <summary>年の目盛り（帯ごとに 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineTick> Ticks { get; set; } = Array.Empty<RoleTimelineTick>();
    /// <summary>帯が多いときに目盛りの文字を間引く CSS クラス（"rtl-ticks-medium" / "rtl-ticks-sparse" / 空）。</summary>
    public string TickDensityClass { get; set; } = "";
    /// <summary>展開時の行（並びは担当の始まり → 終わりの早い順）。</summary>
    public IReadOnlyList<RoleTimelineRow> Rows { get; set; } = Array.Empty<RoleTimelineRow>();
    /// <summary>畳んだ概観の高さ（px）。</summary>
    public string MiniHeight { get; set; } = "";
    /// <summary>畳んだ概観の SVG の viewBox。</summary>
    public string MiniViewBox { get; set; } = "";
    /// <summary>畳んだ概観の帯（交互の薄い塗り）の path。縦は 0〜1 の単位で描く。</summary>
    public string MiniBandsPath { get; set; } = "";
    /// <summary>畳んだ概観の、続けて担当した期間の線の path。</summary>
    public string MiniLinesPath { get; set; } = "";
}

/// <summary>線表の TV シリーズの帯 1 本。</summary>
internal sealed class RoleTimelineBand
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
    /// <summary>交互に薄く塗る側の帯か。</summary>
    public bool IsAlt { get; set; }
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
    /// <summary>帯が多いとき、狭い画面で文字を間引く年（5 の倍数以外）か。</summary>
    public bool IsMinor { get; set; }
}

/// <summary>線表の 1 行（人物・企業/団体 1 つ）。</summary>
internal sealed class RoleTimelineRow
{
    public string EntityKind { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityUrl { get; set; } = "";
    /// <summary>続けて担当した期間（2 件以上つながったもの）の細線。</summary>
    public IReadOnlyList<RoleTimelineMark> Spans { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>担当した TV の話の帯（話数が続く間はひと続き）。</summary>
    public IReadOnlyList<RoleTimelineMark> Segments { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>担当した映画の点（<see cref="RoleTimelineMark.Width"/> は使わない）。</summary>
    public IReadOnlyList<RoleTimelineMark> Movies { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>続けて担当した期間（単発の担当を含む。畳んだ概観の線を描くのに使う）。</summary>
    public IReadOnlyList<RoleTimelinePeriod> Periods { get; set; } = Array.Empty<RoleTimelinePeriod>();
    /// <summary>内訳の担当期間（「2004年2月〜2005年1月」を「、」でつないだもの）。</summary>
    public string PeriodLabel { get; set; } = "";
    /// <summary>内訳の作品ごとの担当（「📺 シリーズ名 #1～49」「🎥 映画名」を改行でつないだもの）。</summary>
    public string WorksText { get; set; } = "";
}

/// <summary>線表上の印 1 つの位置と幅。</summary>
internal sealed class RoleTimelineMark
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
}

/// <summary>続けて担当した期間（開始日〜終了日。終了日は最後の担当の放送枠の終わり）。</summary>
internal readonly record struct RoleTimelinePeriod(DateOnly Start, DateOnly End);
