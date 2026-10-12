#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// 盤のディスク名（<c>META/DL/bdmt_*.xml</c> の表示名）と作品の正式タイトルの近さで、盤の作品を当てる。
    /// <list type="bullet">
    ///   <item><description>比べる前に両方を NFKC で全角・半角をそろえ、小文字にし、「DISC6」「Vol.2」のような番号を外して、文字と数字だけを残す（空白・「！☆♪〜」などの記号は落とす）。</description></item>
    ///   <item><description>近さは 2 文字ずつの組の重なり（Dice 係数。0〜1）。</description></item>
    ///   <item><description>併映（COFEATURE / SEGMENT）は親の映画にまとめて数える（盤の作品は親）。</description></item>
    /// </list>
    /// 名前だけでは TV と同じ名前の映画、BOX の盤名（「〜20th LEGENDARY BOX〜」）などで 1 位と 2 位の差が小さいことがあるので、
    /// 尺で当てられなかったときの決め手としてだけ使い（<see cref="PickConfident"/>）、決めきれないときは人に選んでもらう。
    /// </summary>
    public static class SeriesNameMatcher
    {
        /// <summary>名前だけで決めるのに要る、1 位の近さの下限。</summary>
        public const double MinScore = 0.6;

        /// <summary>名前だけで決めるのに要る、1 位と 2 位の近さの差の下限。</summary>
        public const double MinMargin = 0.1;

        private static readonly Regex DiscNumberRegex = new(@"(disc|vol\.?)\s*\d+", RegexOptions.CultureInvariant);

        /// <summary>候補の作品（親の映画にまとめたもの）とディスク名との近さ。</summary>
        public sealed record Candidate(Series Series, double Score);

        /// <summary>
        /// ディスク名に近い順に作品を並べる。併映は親の映画にまとめ、親の映画とその併映のうちいちばん近いものの値をとる。
        /// ディスク名が空なら空の一覧。
        /// </summary>
        public static IReadOnlyList<Candidate> Rank(string? discName, IReadOnlyList<Series> all)
        {
            if (string.IsNullOrWhiteSpace(discName)) return Array.Empty<Candidate>();
            var disc = Bigrams(Normalize(discName));
            if (disc.Count == 0) return Array.Empty<Candidate>();
            var best = new Dictionary<int, Candidate>();
            foreach (var s in all)
            {
                var root = FeatureLinkMatcher.BuildGroup(s, all).Root;
                double score = Dice(disc, Bigrams(Normalize(s.Title)));
                if (!best.TryGetValue(root.SeriesId, out var cur) || score > cur.Score) best[root.SeriesId] = new Candidate(root, score);
            }
            return best.Values.OrderByDescending(c => c.Score).ThenBy(c => c.Series.SeriesId).ToList();
        }

        /// <summary>
        /// 名前だけで作品を決めてよければその作品 ID を返す。いちばん近い作品が <paramref name="eligible"/> を満たし、
        /// 近さが <see cref="MinScore"/> 以上で、満たす作品の中の 2 位と <see cref="MinMargin"/> 以上離れているときだけ。
        /// <paramref name="eligible"/> は尺で当たらなかったと分かっている作品（円盤尺を持つ TV 作品など）を外すのに使う。
        /// いちばん近い作品がそれなら決めない（TV の特典盤を同じ名前の映画と取り違えないため）。
        /// </summary>
        public static int? PickConfident(IReadOnlyList<Candidate> ranked, Func<Series, bool> eligible)
        {
            if (ranked.Count == 0 || !eligible(ranked[0].Series)) return null;
            var top = ranked[0];
            double second = ranked.Skip(1).Where(c => eligible(c.Series)).Select(c => c.Score).DefaultIfEmpty(0).First();
            return top.Score >= MinScore && top.Score - second >= MinMargin ? top.Series.SeriesId : null;
        }

        /// <summary>比べるための形にそろえる（NFKC・小文字・盤の番号を外す・文字と数字だけ残す）。</summary>
        internal static string Normalize(string text)
        {
            string s = DiscNumberRegex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "");
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static HashSet<string> Bigrams(string s)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < s.Length; i++) set.Add(s.Substring(i, 2));
            return set;
        }

        private static double Dice(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            int common = a.Count(b.Contains);
            return 2.0 * common / (a.Count + b.Count);
        }
    }
}
