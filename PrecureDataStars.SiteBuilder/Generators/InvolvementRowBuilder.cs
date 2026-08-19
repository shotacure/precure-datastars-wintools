using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// シリーズ単位行（<see cref="InvolvementSeriesRow"/>）へ呼び出し側が差し込む付加情報。
/// 人物詳細は「演じたキャラ名」（声優関与のシリーズ内連名）と「所属屋号ラベル」を
/// スコープ別（シリーズ全体 / エピソード単位）に併記するため、これを解決して返す。
/// 企業詳細のように付加情報を持たない呼び出し側は <see cref="Empty"/>（全フィールド空文字）になる。
/// </summary>
internal readonly record struct InvolvementSeriesRowExtras(
    string SeriesScopeCharacterNames,
    string PerEpisodeCharacterNames,
    string SeriesScopeAffiliationsLabel,
    string PerEpisodeAffiliationsLabel)
{
    /// <summary>付加情報なしの既定値（全フィールド空文字）。extras フック未指定時に使う。</summary>
    internal static readonly InvolvementSeriesRowExtras Empty = new("", "", "", "");
}

/// <summary>
/// 人物・企業詳細ページの「クレジット履歴」で使う、役職グループ内のシリーズ単位行
/// （<see cref="InvolvementSeriesRow"/>）構築の共通骨格。
/// PersonsGenerator / CompaniesGenerator が「映画系判定 → 話数集合の収集 → 全話判定（SetEquals）→
/// 話数圧縮表記（<see cref="EpisodeRangeCompressor"/>）→ シリーズ全体スコープ行 → 行構築 →
/// 担当量（TV 話数・映画本数）カウント」という同一実装を重複保持していたため一本化した。
/// 差分（人物詳細だけが持つ「演じたキャラ名」「所属屋号ラベル」の併記）は
/// <c>extrasResolver</c> フックで呼び出し側から差し込む。
/// 依存は読み取り専用の <see cref="BuildContext"/> 辞書と引数のみで、
/// 並列レンダリングフェーズから複数スレッドで同時に呼んでも安全。
/// </summary>
internal static class InvolvementRowBuilder
{
    /// <summary>
    /// 役職グループ 1 件分の関与群をシリーズ単位（放送開始日昇順）に集約し、
    /// 話数圧縮表記つきの行群と担当量（TV 話数・映画本数）を組み立てる。
    /// 各シリーズにつき「シリーズ全体スコープの 1 行（あれば先）」と「エピソード単位の集約 1 行（話数があれば）」の
    /// 最大 2 行を出す。全話担当のときは話数表記を省略し <see cref="InvolvementSeriesRow.IsAllEpisodes"/> を立てる。
    /// </summary>
    /// <param name="ctx">ビルド共有コンテキスト（読み取り専用辞書のみ参照）。</param>
    /// <param name="roleGroup">同一役職グループに属する関与群。</param>
    /// <param name="extrasResolver">シリーズ単位の関与群から行併記の付加情報（演じたキャラ名・所属屋号ラベル）を
    /// 解決するフック。未指定（null）なら全フィールド空文字の <see cref="InvolvementSeriesRowExtras.Empty"/> を使う。</param>
    internal static (List<InvolvementSeriesRow> SeriesRows, int EpisodeCountTotal, int MovieCountTotal) BuildSeriesRows(
        BuildContext ctx,
        IEnumerable<Involvement> roleGroup,
        Func<IEnumerable<Involvement>, InvolvementSeriesRowExtras>? extrasResolver = null)
    {
        var seriesRows = new List<InvolvementSeriesRow>();
        int episodeCountTotal = 0;
        int movieCountTotal = 0;

        foreach (var bySeries in roleGroup
            .GroupBy(i => i.SeriesId)
            .OrderBy(sg => ctx.SeriesStartDate(sg.Key)))
        {
            if (!ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;

            // このシリーズが「映画系（series_kinds.credit_attach_to='SERIES'）」か判定。
            // MOVIE / MOVIE_SHORT / SPRING / EVENT が該当。当該シリーズへの関与は何件あっても 1 本としてカウント。
            bool isMovieKindSeries = ctx.IsMovieKindSeries(bySeries.Key);

            // 同一シリーズで「シリーズ全体スコープ」と「エピソード単位」が混在しうる。
            // シリーズ全体スコープは別行として残し、エピソード単位は話数集合に集約する。
            var episodeNos = new HashSet<int>();
            bool hasSeriesScope = false;
            foreach (var inv in bySeries)
            {
                if (inv.EpisodeId is int eid)
                {
                    var ep = ctx.LookupEpisode(bySeries.Key, eid);
                    if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                }
                else
                {
                    hasSeriesScope = true;
                }
            }

            // シリーズ内の全話数（圧縮表記の「(全話)」判定用）。
            var allSeriesEpNos = ctx.EpisodesBySeries.TryGetValue(bySeries.Key, out var allEps)
                ? allEps.Select(e => e.SeriesEpNo).ToList()
                : new List<int>();

            // 呼び出し側固有の付加情報（演じたキャラ名・所属屋号ラベル）を解決する。フック未指定なら全て空文字。
            var extras = extrasResolver is null ? InvolvementSeriesRowExtras.Empty : extrasResolver(bySeries);

            // (a) シリーズ全体スコープの 1 行（あれば先に出す）。
            // 映画系シリーズ（credit_attach_to='SERIES'）はそもそも全クレジットが series 直付けの
            // 「シリーズ全体」相当なので、わざわざ「（シリーズ全体）」ラベルを併記する意味がない
            // （見出しの「N 本」表記＋シリーズ名で十分自明）。TV 系シリーズに稀に出る series-scope
            // クレジットだけ「（シリーズ全体）」を出して、エピソード単位の行と区別する。
            if (hasSeriesScope)
            {
                seriesRows.Add(new InvolvementSeriesRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    // テンプレ側（persons-detail.sbn / companies-detail.sbn）が
                    // "({{ RangeLabel }})" と括弧で包むため、ここでは括弧を含まない素のラベルにする
                    // （含めると二重括弧になる）。
                    RangeLabel = isMovieKindSeries ? "" : "シリーズ全体",
                    IsAllEpisodes = false,
                    CharacterNames = extras.SeriesScopeCharacterNames,
                    AffiliationsLabel = extras.SeriesScopeAffiliationsLabel
                });
            }

            // (b) エピソード単位の集約 1 行（話数があれば）。
            if (episodeNos.Count > 0)
            {
                bool isAll = allSeriesEpNos.Count > 0
                    && episodeNos.SetEquals(allSeriesEpNos);
                string rangeLabel = isAll
                    ? string.Empty
                    : EpisodeRangeCompressor.Compress(episodeNos);

                seriesRows.Add(new InvolvementSeriesRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    RangeLabel = rangeLabel,
                    IsAllEpisodes = isAll,
                    CharacterNames = extras.PerEpisodeCharacterNames,
                    AffiliationsLabel = extras.PerEpisodeAffiliationsLabel,
                    Episodes = BuildEpisodeBreakdown(ctx, bySeries.Key, series.Slug, episodeNos, isAll)
                });
            }

            // (c) 担当量カウント：シリーズ種別で「話」と「本」を分けて加算。
            // 映画系（credit_attach_to='SERIES'）：当該シリーズに関与が 1 件以上あれば 1 本としてカウント
            //   （映画 1 本に OP / ED / INSERT / SOUND_TRACK が同一カードに同居しても 1 本扱い）。
            // TV 系（credit_attach_to='EPISODE'）：エピソード単位の関与話数を加算（重複話数は HashSet で排除済み）。
            // SERIES スコープのみで episode 関与が無い TV 系（稀ケース）は本カウントには寄与せず 0 計上。
            if (isMovieKindSeries)
            {
                if (hasSeriesScope || episodeNos.Count > 0) movieCountTotal += 1;
            }
            else
            {
                episodeCountTotal += episodeNos.Count;
            }
        }

        return (seriesRows, episodeCountTotal, movieCountTotal);
    }

    /// <summary>
    /// 担当エピソードの内訳を展開する上限。これ以下の行だけサブタイトルと放送日を並べる。
    /// 数話しか担当していない人物・企業のページは「#6」という数字しか出ず、
    /// 何の話なのか分からないうえ当該エピソードへのリンクも張られない状態だった。
    /// 一方で数百話を担当する常連スタッフの行まで展開すると、リストが本文を埋め尽くして
    /// ページの主旨がぼやける。少数担当の行に限って展開する線引きにしている。
    /// </summary>
    internal const int EpisodeBreakdownMaxCount = 20;

    /// <summary>
    /// 担当話数が少ない行について、各話のサブタイトル・放送日・詳細ページ URL を解決する。
    /// 全話担当の行は圧縮表記側で「(全話)」と表現できるため展開しない。
    /// </summary>
    private static IReadOnlyList<InvolvementEpisodeRow> BuildEpisodeBreakdown(
        BuildContext ctx,
        int seriesId,
        string seriesSlug,
        IReadOnlyCollection<int> episodeNos,
        bool isAllEpisodes)
    {
        if (isAllEpisodes || episodeNos.Count == 0 || episodeNos.Count > EpisodeBreakdownMaxCount)
            return Array.Empty<InvolvementEpisodeRow>();

        if (!ctx.EpisodesBySeries.TryGetValue(seriesId, out var seriesEpisodes))
            return Array.Empty<InvolvementEpisodeRow>();

        var wanted = episodeNos.ToHashSet();
        return seriesEpisodes
            .Where(e => wanted.Contains(e.SeriesEpNo))
            .OrderBy(e => e.SeriesEpNo)
            .Select(e => new InvolvementEpisodeRow
            {
                SeriesEpNo = e.SeriesEpNo,
                // サブタイトルが確定していれば鉤括弧で括る。未確定話の TitleDisplayText は
                // （サブタイトル「未定」）のように自前で括弧を含むため、そのまま続ける。
                Label = string.IsNullOrEmpty(e.TitleText)
                    ? $"第{e.SeriesEpNo}話{e.TitleDisplayText}"
                    : $"第{e.SeriesEpNo}話「{e.TitleText}」",
                Url = PathUtil.EpisodeUrl(seriesSlug, e.SeriesEpNo),
                OnAirLabel = JpDateFormat.Date(e.OnAirAt)
            })
            .ToList();
    }
}
