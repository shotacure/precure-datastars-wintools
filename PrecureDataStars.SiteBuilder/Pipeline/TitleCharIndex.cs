using System.Text.Json;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 全エピソードのサブタイトル文字統計（<c>episodes.title_char_stats</c> JSON の <c>chars</c> キー集合）を
/// 1 度だけ展開して、文字単位／エピソード単位の双方向辞書として保持する事前計算インデックス。
/// <see cref="Rendering.TitleCharInfoRenderer"/> がエピソード詳細ページの「初出 / 唯一 / N年Mか月ぶり」
/// 表記を組み立てる際、per-page で <see cref="Data.Repositories.EpisodesRepository.GetFirstUseOfCharAsync"/> や
/// <see cref="Data.Repositories.EpisodesRepository.GetEpisodeUsageCountOfCharAsync"/> を文字数分繰り返すと
/// JSON_CONTAINS_PATH の全エピソード走査が累計で数万クエリ規模になるため、本クラスの構築結果を介して
/// すべての判定をメモリ上の辞書参照で完結させる。
/// 構築には新規 DB クエリを必要としない（<see cref="Episode.TitleCharStats"/> は <see cref="Data.SiteDataLoader"/>
/// が既に全話分ロード済みのため、JSON を C# 側でパースするだけで足りる）。
/// </summary>
public sealed class TitleCharIndex
{
    /// <summary>
    /// 文字キー（書記素クラスタ単位、<c>title_char_stats.chars</c> の JSON プロパティ名）から、
    /// その文字を含むエピソードの一覧を引く辞書。値リストは
    /// (<see cref="TitleCharOccurrence.TotalEpNo"/> 昇順, <see cref="TitleCharOccurrence.OnAirAt"/>,
    /// <see cref="TitleCharOccurrence.EpisodeId"/>) のタイブレーク順に整列済み。
    /// 「初出」判定は先頭要素の比較で、「唯一」判定は要素数で、「ぶり」判定は二分探索で行う。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TitleCharOccurrence>> ByChar { get; }

    /// <summary>
    /// エピソード ID から、そのエピソードのサブタイトルで使われた文字キー集合を引く辞書。
    /// 用途は限定的（<see cref="TitleCharInfoRenderer"/> 内で当該エピソードの使用文字を素早く取りたいときの
    /// 補助）であり、現状は使われていないが、将来別のジェネレータからも同じ展開結果を再利用しやすくするために
    /// 双方向で持つ。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> CharsByEpisode { get; }

    private TitleCharIndex(
        IReadOnlyDictionary<string, IReadOnlyList<TitleCharOccurrence>> byChar,
        IReadOnlyDictionary<int, IReadOnlyList<string>> charsByEpisode)
    {
        ByChar = byChar;
        CharsByEpisode = charsByEpisode;
    }

    /// <summary>
    /// 与えられたエピソード集合からインデックスを構築する。
    /// <see cref="Episode.TitleCharStats"/> が空、または <see cref="Episode.TotalEpNo"/> が未設定の話は
    /// インデックスに含めない（既存 SQL <c>GetTitleCharRevivalStatsAsync</c> の WHERE 条件と同じ運用）。
    /// </summary>
    /// <param name="episodes">全エピソード（既に <c>is_deleted = 0</c> 済みであることを前提とする）。</param>
    /// <param name="seriesById">シリーズ ID → <see cref="Series"/> の索引。 復活情報の「『○○プリキュア』第N話」表記用に <see cref="Series.Title"/> を引く。</param>
    /// <returns>構築済みインデックス。</returns>
    public static TitleCharIndex Build(
        IEnumerable<Episode> episodes,
        IReadOnlyDictionary<int, Series> seriesById)
    {
        var byChar = new Dictionary<string, List<TitleCharOccurrence>>(StringComparer.Ordinal);
        var charsByEpisode = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var e in episodes)
        {
            if (string.IsNullOrEmpty(e.TitleCharStats)) continue;
            if (e.TotalEpNo is not int totalEpNo) continue;
            if (!seriesById.TryGetValue(e.SeriesId, out var series)) continue;

            // title_char_stats JSON から chars のキーだけ取り出す。値（出現回数）は本インデックスでは使わない。
            // JSON が壊れているとき（保存不整合等）は当該エピソードをスキップして他に影響を与えない。
            var chars = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(e.TitleCharStats);
                if (doc.RootElement.TryGetProperty("chars", out var charsEl)
                    && charsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in charsEl.EnumerateObject())
                    {
                        chars.Add(prop.Name);
                    }
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (chars.Count == 0) continue;
            charsByEpisode[e.EpisodeId] = chars;

            var occ = new TitleCharOccurrence(
                EpisodeId: e.EpisodeId,
                SeriesId: e.SeriesId,
                SeriesTitle: series.Title,
                SeriesEpNo: e.SeriesEpNo,
                TitleText: e.TitleText ?? string.Empty,
                OnAirAt: e.OnAirAt,
                TotalEpNo: totalEpNo);

            foreach (var c in chars)
            {
                if (!byChar.TryGetValue(c, out var list))
                {
                    list = new List<TitleCharOccurrence>();
                    byChar[c] = list;
                }
                list.Add(occ);
            }
        }

        // TotalEpNo 昇順 (OnAirAt, EpisodeId タイブレーク) で整列。
        // 二分探索や「自分より前の最終出現」の取り出しを O(log n) で行えるようにするための前処理。
        var sortedByChar = new Dictionary<string, IReadOnlyList<TitleCharOccurrence>>(StringComparer.Ordinal);
        foreach (var (key, list) in byChar)
        {
            list.Sort(static (a, b) =>
            {
                int c = a.TotalEpNo.CompareTo(b.TotalEpNo);
                if (c != 0) return c;
                c = a.OnAirAt.CompareTo(b.OnAirAt);
                if (c != 0) return c;
                return a.EpisodeId.CompareTo(b.EpisodeId);
            });
            sortedByChar[key] = list;
        }

        return new TitleCharIndex(sortedByChar, charsByEpisode);
    }
}

/// <summary>サブタイトル中の 1 文字キーが 1 エピソードで使われた事実を表す出現レコード。 シリーズ正式名・サブタイトル本文・放送日まで一緒に保持しておくことで、 「ぶり」表記（『○○プリキュア』第N話「サブタイトル」(YYYY.M.D) 以来）の組み立てを 追加クエリなしで完了させる。</summary>
public sealed record TitleCharOccurrence(
    int EpisodeId,
    int SeriesId,
    string SeriesTitle,
    int SeriesEpNo,
    string TitleText,
    DateTime OnAirAt,
    int TotalEpNo);
