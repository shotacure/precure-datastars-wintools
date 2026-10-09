#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// 映画など作品単位の作品（話を持たない）の盤で、どのプレイリスト・チャプターがどの作品の本編（FEATURE）かを当てる。
    /// 作品は「親の映画」と、その子として親にぶら下がる併映（<c>relation_to_parent</c> が COFEATURE / SEGMENT）のまとまり（<see cref="FeatureGroup"/>）で扱う。
    /// <list type="bullet">
    ///   <item><description>独立して入っている：作品ごとにプレイリストが分かれている。上映時間（<c>series.run_time_seconds</c>）にいちばん近い尺のプレイリストを、その作品の本編とする。</description></item>
    ///   <item><description>繋がって入っている：1 本のプレイリストに上映順（<c>seq_in_parent</c>）で続いている。チャプターの尺を先頭から足して、
    ///     各作品の上映時間に合う切れ目でチャプターを作品に振り分ける（すべての作品に上映時間があるときだけ）。プレイリストの作品は親（3 本立ては親のまとまり）。</description></item>
    /// </list>
    /// 上映時間は「本編のプレイリスト（またはチャプター）の尺 − 先頭の黒み」で入れた値で、比べるときは黒みのぶんの差を許す幅に含める。
    /// 上映時間の無い作品は当てない（人が当ててから上映時間を入れれば、次から当たる）。
    /// </summary>
    public static class FeatureLinkMatcher
    {
        /// <summary>上映時間に対して許す差（ミリ秒）の上限。短い併映は上映時間の 5%（10 秒以上）に狭める。</summary>
        public const int MaxToleranceMs = 90_000;

        /// <summary>繋がって入っているとき、先頭・末尾に余白として残してよいチャプターの数と、その尺の上限（ミリ秒）。</summary>
        private const int MaxEdgeChapters = 2;
        private const int MaxEdgeChapterMs = 30_000;

        /// <summary>上映時間に対して許す差（ミリ秒）。</summary>
        public static long ToleranceFor(ushort runTimeSeconds) => Math.Max(10_000L, Math.Min(MaxToleranceMs, runTimeSeconds * 50L));

        /// <summary>親の映画と併映のまとまり。</summary>
        public sealed class FeatureGroup
        {
            /// <summary>親（盤の作品。3 本立ては親のまとまり）。</summary>
            public Series Root { get; init; } = null!;
            /// <summary>親が自分の本編を持たないまとまり（子がすべて SEGMENT の 3 本立てなど）か。</summary>
            public bool RootIsUmbrella { get; init; }
            /// <summary>本編を持つ作品を上映順に（親が本編を持つなら親も含む）。</summary>
            public IReadOnlyList<Series> Pieces { get; init; } = Array.Empty<Series>();
        }

        /// <summary>選んだ作品から、親の映画と併映のまとまりを組む。子（COFEATURE / SEGMENT）を選んだときは親から組む。</summary>
        public static FeatureGroup BuildGroup(Series selected, IReadOnlyList<Series> all)
        {
            var root = selected;
            if (IsChildRelation(selected.RelationToParent) && selected.ParentSeriesId is int pid && all.FirstOrDefault(s => s.SeriesId == pid) is { } parent)
                root = parent;
            var children = all.Where(s => s.ParentSeriesId == root.SeriesId && IsChildRelation(s.RelationToParent)).ToList();
            bool umbrella = children.Count > 0 && children.All(c => string.Equals(c.RelationToParent, "SEGMENT", StringComparison.Ordinal));
            var pieces = new List<Series>(children);
            if (!umbrella) pieces.Add(root);
            return new FeatureGroup
            {
                Root = root,
                RootIsUmbrella = umbrella,
                Pieces = pieces.OrderBy(s => s.SeqInParent ?? 0).ThenBy(s => s.SeriesId).ToList()
            };
        }

        private static bool IsChildRelation(string? relation)
            => string.Equals(relation, "COFEATURE", StringComparison.Ordinal) || string.Equals(relation, "SEGMENT", StringComparison.Ordinal);

        /// <summary>1 つのプレイリストの当て方。</summary>
        public sealed class Proposal
        {
            public string PlaylistFile { get; init; } = "";
            /// <summary>プレイリストの作品（独立ならその作品、繋がっているなら親）。</summary>
            public int SeriesId { get; init; }
            /// <summary>チャプターごとの作品（チャプターと同じ順。null は余白）。</summary>
            public IReadOnlyList<int?> ChapterSeriesIds { get; init; } = Array.Empty<int?>();
            /// <summary>人に見せる説明（「独立：差 +2.1 秒」など）。</summary>
            public string Note { get; init; } = "";
        }

        /// <summary>
        /// まとまりの作品をプレイリストに当てる。繋がって入っているプレイリストを先に探し（作品が 2 つ以上で、すべてに上映時間があるとき）、
        /// 残りのプレイリストに作品を 1 つずつ独立として当てる。親が本編を持たないまとまりで親に上映時間があれば、親もプレイリスト全体で当てる。
        /// 同じプレイリストを 2 度使わない。
        /// </summary>
        public static List<Proposal> Propose(IReadOnlyList<(string PlaylistFile, ulong DurationMs, IReadOnlyList<ulong> ChapterDurationsMs)> playlists, FeatureGroup group)
        {
            var result = new List<Proposal>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 繋がって入っている
            if (group.Pieces.Count >= 2 && group.Pieces.All(p => p.RunTimeSeconds is not null))
            {
                foreach (var (file, _, chapters) in playlists)
                {
                    var assign = TrySplit(chapters, group.Pieces);
                    if (assign is null) continue;
                    result.Add(new Proposal
                    {
                        PlaylistFile = file, SeriesId = group.Root.SeriesId, ChapterSeriesIds = assign,
                        Note = "繋がって入っている（" + string.Join(" → ", group.Pieces.Select(p => p.Title)) + "）"
                    });
                    used.Add(file);
                }
            }

            // 独立して入っている
            var singles = group.Pieces.ToList();
            if (group.RootIsUmbrella) singles.Insert(0, group.Root);
            foreach (var piece in singles)
            {
                if (piece.RunTimeSeconds is not ushort rt) continue;
                long expected = rt * 1000L, tol = ToleranceFor(rt);
                (string File, int Chapters, long Diff)? best = null;
                foreach (var (file, duration, chapters) in playlists)
                {
                    if (used.Contains(file)) continue;
                    long diff = (long)duration - expected;
                    if (Math.Abs(diff) > tol) continue;
                    if (best is null || Math.Abs(diff) < Math.Abs(best.Value.Diff)) best = (file, chapters.Count, diff);
                }
                if (best is null) continue;
                used.Add(best.Value.File);
                bool umbrellaWhole = group.RootIsUmbrella && piece.SeriesId == group.Root.SeriesId;
                result.Add(new Proposal
                {
                    PlaylistFile = best.Value.File, SeriesId = piece.SeriesId,
                    // 親のまとまりをプレイリスト全体で当てたときは、チャプターの作品は人が振り分ける
                    ChapterSeriesIds = Enumerable.Repeat<int?>(umbrellaWhole ? null : piece.SeriesId, best.Value.Chapters).ToList(),
                    Note = $"独立（上映時間との差 {best.Value.Diff / 1000.0:+0.0;-0.0} 秒）"
                });
            }
            return result;
        }

        /// <summary>
        /// チャプターの尺を先頭から足して、作品の上映時間に合う切れ目で上映順に振り分ける。先頭・末尾の短いチャプター（2 つまで、30 秒以下）は余白にしてよい。
        /// 振り分けられなければ null。
        /// </summary>
        private static List<int?>? TrySplit(IReadOnlyList<ulong> chapters, IReadOnlyList<Series> pieces)
        {
            for (int lead = 0; lead <= MaxEdgeChapters && lead < chapters.Count; lead++)
            {
                if (lead > 0 && chapters[lead - 1] > MaxEdgeChapterMs) break;
                var assign = new List<int?>(Enumerable.Repeat<int?>(null, chapters.Count));
                int pos = lead;
                bool ok = true;
                foreach (var piece in pieces)
                {
                    long expected = piece.RunTimeSeconds!.Value * 1000L, tol = ToleranceFor(piece.RunTimeSeconds.Value);
                    long sum = 0;
                    int bestEnd = -1; long bestDiff = long.MaxValue;
                    for (int j = pos; j < chapters.Count; j++)
                    {
                        sum += (long)chapters[j];
                        long diff = Math.Abs(sum - expected);
                        if (diff <= tol && diff < bestDiff) { bestDiff = diff; bestEnd = j; }
                        if (sum - expected > tol) break;
                    }
                    if (bestEnd < 0) { ok = false; break; }
                    for (int j = pos; j <= bestEnd; j++) assign[j] = piece.SeriesId;
                    pos = bestEnd + 1;
                }
                if (!ok) continue;
                int trailing = chapters.Count - pos;
                if (trailing > MaxEdgeChapters) continue;
                if (Enumerable.Range(pos, trailing).Any(j => chapters[j] > MaxEdgeChapterMs)) continue;
                return assign;
            }
            return null;
        }
    }
}
