using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>
/// 「その日に何があったか」の記念日データを 1 度だけ集めて <see cref="AnniversaryEntry"/> の列にする。
///
/// <para>
/// 集める出来事は 4 種類：エピソードの放送日、映画の公開日、キャラクターの誕生日、人物の誕生日。
/// ホームのカレンダー（クライアント側 JS へ渡す JSON）と日付別の記念日ページが同じ集合を共有するため、
/// 収集はここに一本化する。どちらか一方でしか拾えない出来事を作らないことが目的。
/// </para>
/// <para>
/// 誕生日の解決にはキャラクター・人物・プリキュア・キャラ名義・シリーズ所属の 5 マスタが要るため、
/// ビルド 1 回につき 1 度だけロードする。
/// </para>
/// </summary>
public static class AnniversaryDataBuilder
{
    /// <summary>記念日データを収集する。並び順はエピソード（放送日昇順）→ 映画（公開日昇順）→ キャラクター誕生日 → 人物誕生日。</summary>
    public static async Task<IReadOnlyList<AnniversaryEntry>> BuildAsync(
        BuildContext ctx,
        IConnectionFactory factory,
        CancellationToken ct = default)
    {
        var entries = new List<AnniversaryEntry>();

        AddEpisodes(ctx, entries);
        AddMovies(ctx, entries);
        await AddBirthdaysAsync(ctx, factory, entries, ct).ConfigureAwait(false);

        return entries;
    }

    /// <summary>エピソードの放送日を積む。</summary>
    private static void AddEpisodes(BuildContext ctx, List<AnniversaryEntry> entries)
    {
        // 「最終回」の定義は series.episodes（マスタの総話数）が示す回。
        // episodes テーブルへの登録進度や EndDate の有無には依存させない
        // （マスタが先行宣言した総話数で最終話判定する。総話数未設定のシリーズは最終話マーカーを持たない）。
        var lastEpNoBySeries = new Dictionary<int, int>();
        foreach (var s in ctx.Series)
        {
            if (s.Episodes is ushort total && total > 0)
                lastEpNoBySeries[s.SeriesId] = total;
        }

        // 全エピソードをフラット化する。子作品（MOVIE_SHORT）は単独詳細ページを持たないため除外する。
        var flattened = new List<(Episode Episode, Series Series)>();
        foreach (var (seriesId, episodes) in ctx.EpisodesBySeries)
        {
            if (!ctx.SeriesById.TryGetValue(seriesId, out var series)) continue;
            if (SeriesClassifier.IsMovieShortChild(series)) continue;
            foreach (var episode in episodes) flattened.Add((episode, series));
        }

        foreach (var (episode, series) in flattened.OrderBy(x => x.Episode.OnAirAt))
        {
            bool isLast = lastEpNoBySeries.TryGetValue(series.SeriesId, out var lastNo)
                          && episode.SeriesEpNo == lastNo;
            // サブタイトル解禁時刻（未解禁のときだけ非 null）。表示側がぼかしの出し分けに使う。
            var revealAt = SubtitleGuardRenderer.RevealAtFor(episode.EpisodeId, ctx.SubtitleRevealAtByEpisodeId);

            entries.Add(new AnniversaryEntry
            {
                Kind = "ep",
                Year = episode.OnAirAt.Year,
                Month = episode.OnAirAt.Month,
                Day = episode.OnAirAt.Day,
                SeriesTitle = series.Title,
                SeriesSlug = series.Slug,
                SeriesTitleShort = string.IsNullOrEmpty(series.TitleShort) ? series.Title : series.TitleShort,
                SeriesStartYear = series.StartDate.Year,
                EpisodeNo = episode.SeriesEpNo,
                // サブタイトル未確定話はプレースホルダ（（サブタイトル「未定」）等）で出す。
                EpisodeTitle = episode.TitleDisplayText,
                EpisodeUrl = PathUtil.EpisodeUrl(series.Slug, episode.SeriesEpNo),
                IsFirstEpisode = episode.SeriesEpNo == 1,
                IsLastEpisode = isLast,
                RevealAtIso = revealAt is { } at ? SubtitleGuardRenderer.ToRevealAtIso(at) : ""
            });
        }
    }

    /// <summary>映画（MOVIE / SPRING）の公開日を積む。</summary>
    private static void AddMovies(BuildContext ctx, List<AnniversaryEntry> entries)
    {
        foreach (var s in ctx.Series
                     .Where(s => string.Equals(s.KindCode, "MOVIE", StringComparison.Ordinal)
                              || string.Equals(s.KindCode, "SPRING", StringComparison.Ordinal))
                     .OrderBy(s => s.StartDate))
        {
            entries.Add(new AnniversaryEntry
            {
                Kind = "mv",
                Year = s.StartDate.Year,
                Month = s.StartDate.Month,
                Day = s.StartDate.Day,
                SeriesTitle = s.Title,
                SeriesSlug = s.Slug,
                SeriesTitleShort = string.IsNullOrEmpty(s.TitleShort) ? s.Title : s.TitleShort,
                SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                SeriesStartYear = s.StartDate.Year
            });
        }
    }

    /// <summary>キャラクター・人物の誕生日を積む。</summary>
    private static async Task AddBirthdaysAsync(
        BuildContext ctx,
        IConnectionFactory factory,
        List<AnniversaryEntry> entries,
        CancellationToken ct)
    {
        var characters = await new CharactersRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var persons = await new PersonsRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var precures = await new PrecuresRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var charAliases = await new CharacterAliasesRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var seriesPrecures = await new SeriesPrecuresRepository(factory).GetAllAsync(ct).ConfigureAwait(false);

        var aliasById = charAliases.ToDictionary(a => a.AliasId);

        // character_id → 代表 precure（最小 precure_id を採って決定的に）。
        var precureByCharacter = new Dictionary<int, Precure>();
        foreach (var pr in precures.OrderBy(pr => pr.PrecureId))
        {
            if (aliasById.TryGetValue(pr.TransformAliasId, out var ta)
                && !precureByCharacter.ContainsKey(ta.CharacterId))
            {
                precureByCharacter[ta.CharacterId] = pr;
            }
        }

        // precure_id → 代表シリーズ（series_precures のうち放送開始が最も早いもの）。
        var seriesByPrecure = new Dictionary<int, Series>();
        foreach (var sp in seriesPrecures)
        {
            if (!ctx.SeriesById.TryGetValue(sp.SeriesId, out var s)) continue;
            if (seriesByPrecure.TryGetValue(sp.PrecureId, out var cur) && cur.StartDate <= s.StartDate) continue;
            seriesByPrecure[sp.PrecureId] = s;
        }

        // ── キャラクター誕生日（PRECURE / ALLY、月日が揃っているもの）──
        foreach (var c in characters)
        {
            if (!(string.Equals(c.CharacterKind, "PRECURE", StringComparison.Ordinal)
                  || string.Equals(c.CharacterKind, "ALLY", StringComparison.Ordinal))) continue;
            if (c.BirthMonth is not byte cm || c.BirthDay is not byte cd) continue;

            // 既定は正式名称（precure 紐付けの無い ALLY 等）。
            string displayName = c.Name;
            string keyColor = "";
            Series? repSeries = null;

            if (precureByCharacter.TryGetValue(c.CharacterId, out var pr))
            {
                keyColor = pr.KeyColor ?? "";
                // プリキュアの誕生日は変身前名義で表示する。
                if (aliasById.TryGetValue(pr.PreTransformAliasId, out var preA) && !string.IsNullOrEmpty(preA.Name))
                    displayName = preA.Name;
                if (seriesByPrecure.TryGetValue(pr.PrecureId, out var sps)) repSeries = sps;
            }

            var (bg, fg, bd) = KeyColorBadge.Resolve(keyColor);
            entries.Add(new AnniversaryEntry
            {
                Kind = "cb",
                Month = cm,
                Day = cd,
                CharacterName = c.Name,
                CharacterDisplayName = displayName,
                // プリキュア詳細ページは廃止済みで /characters/{character_id}/ が兼ねる。
                CharacterUrl = PathUtil.CharacterUrl(c.CharacterId),
                KeyColorBackground = bg,
                KeyColorForeground = fg,
                KeyColorBorder = bd,
                SeriesTitle = repSeries?.Title ?? "",
                SeriesSlug = repSeries?.Slug ?? "",
                SeriesUrl = repSeries is null ? "" : PathUtil.SeriesUrl(repSeries.Slug),
                SeriesStartYear = repSeries?.StartDate.Year ?? 0
            });
        }

        // ── 人物誕生日（生年は公開設定が PUBLIC かつ判明時のみ持たせる）──
        foreach (var pe in persons)
        {
            if (pe.BirthMonth is not byte pm || pe.BirthDay is not byte pd) continue;
            int? birthYear = (string.Equals(pe.BirthYearVisibility, "PUBLIC", StringComparison.Ordinal) && pe.BirthYear.HasValue)
                ? pe.BirthYear.Value
                : null;

            entries.Add(new AnniversaryEntry
            {
                Kind = "pb",
                Month = pm,
                Day = pd,
                PersonName = pe.FullName,
                PersonUrl = PathUtil.PersonUrl(pe.PersonId),
                BirthYear = birthYear
            });
        }
    }
}
