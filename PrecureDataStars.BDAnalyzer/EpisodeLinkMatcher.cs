#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// 読み取ったプレイリスト（チャプターの尺の並び）を、作品の各話のパートの尺（<c>episode_parts.disc_length</c>）の並びと
    /// 突き合わせて、「このプレイリストは第 N 話、チャプター i は第 N 話のパート k」の候補を出す。
    /// 尺は DB の値を正とし、チャプターの尺（Blu-ray の生の値）は照合の手がかりとしてだけ使う。
    /// 話の最後のチャプター（ユニットの末尾）には 1 秒の余白が付いているので、話の最後のパートだけ円盤尺 + 1 秒を期待する。
    /// <list type="bullet">
    ///   <item><description>比べるのは、話のパートのうち円盤尺（disc_length）を持つものを episode_seq 順に並べたもの。</description></item>
    ///   <item><description>チャプター列の連続した部分列が、パート列と 1 対 1 で尺の差 <see cref="ToleranceMs"/> 以内（最後のパートは余白 1 秒を足して比べる）なら一致。
    ///     前後に余ったチャプターは余白（BLANK）とする。</description></item>
    ///   <item><description>1 つのプレイリストに複数の話が続けて入っていてもよい（全話連続）。先頭から走査して話を順に当て、
    ///     次に探す話は直前に当てた話の次の話数を優先する。同じ話を 2 度当てない。複数の話が同じ所に当たるときは候補の数を添える。
    ///     当たった話が 1 つなら EPISODE、2 つ以上なら PLAY_ALL、無ければ null（パート列の 2 倍以上のチャプターがあれば PLAY_ALL の候補）。</description></item>
    /// </list>
    /// </summary>
    public static class EpisodeLinkMatcher
    {
        /// <summary>話の最後のチャプター（ユニットの末尾）に付いている余白（ミリ秒）。Blu-ray はこの 1 秒を含めて記録し、DB の円盤尺は含まない。</summary>
        public const int EpisodeTailMarginMs = 1000;

        /// <summary>チャプターの尺と期待する尺（<see cref="ExpectedMs"/>）の差として許す幅（ミリ秒）。</summary>
        public const int ToleranceMs = 1000;

        /// <summary>パートに対して期待するチャプターの尺（ミリ秒）。話の最後のパートは末尾の余白 1 秒を足す。</summary>
        public static long ExpectedMs(EpisodePart part, bool isLastPartOfEpisode)
            => (part.DiscLength ?? 0) * 1000L + (isLastPartOfEpisode ? EpisodeTailMarginMs : 0);

        /// <summary>チャプターの尺と期待する尺の差が許容の範囲か。</summary>
        public static bool IsWithinTolerance(long diffMs) => Math.Abs(diffMs) <= ToleranceMs;

        /// <summary>1 つのプレイリストの当て方の提案。</summary>
        public sealed class PlaylistProposal
        {
            public string PlaylistFile { get; init; } = "";
            /// <summary>EPISODE / PLAY_ALL / BONUS / OTHER。決められなければ null。</summary>
            public string? Kind { get; set; }
            public int? EpisodeId { get; set; }
            /// <summary>当たった話の候補の数（2 以上なら要確認）。</summary>
            public int CandidateCount { get; set; }
            /// <summary>チャプターごとの当て方（チャプター列と同じ順）。</summary>
            public List<ChapterProposal> Chapters { get; } = new();
        }

        /// <summary>1 つのチャプターの当て方の提案。</summary>
        public sealed class ChapterProposal
        {
            /// <summary>EPISODE_PART / BLANK。決められなければ null。</summary>
            public string? Kind { get; set; }
            public int? EpisodeId { get; set; }
            public byte? EpisodeSeq { get; set; }
            /// <summary>チャプターの尺 − 期待する尺（円盤尺。話の最後のパートは余白 1 秒込み）（ミリ秒）。パートが当たっていなければ null。</summary>
            public long? DiffMs { get; set; }
        }

        /// <summary>
        /// 当て方を提案する。<paramref name="playlists"/> は (プレイリスト名, チャプターの尺のミリ秒列) の並び、
        /// <paramref name="episodes"/> は作品の話（series_ep_no 順）、<paramref name="partsByEpisode"/> は話ごとのパート（episode_seq 順）。
        /// </summary>
        public static List<PlaylistProposal> Propose(
            IReadOnlyList<(string PlaylistFile, IReadOnlyList<ulong> ChapterDurationsMs)> playlists,
            IReadOnlyList<Episode> episodes,
            IReadOnlyDictionary<int, IReadOnlyList<EpisodePart>> partsByEpisode)
        {
            // 話ごとの「円盤尺を持つパート」の列。
            var partSeqs = new Dictionary<int, List<EpisodePart>>();
            foreach (var ep in episodes)
            {
                if (!partsByEpisode.TryGetValue(ep.EpisodeId, out var parts)) continue;
                var withDisc = parts.Where(p => p.DiscLength is not null).OrderBy(p => p.EpisodeSeq).ToList();
                if (withDisc.Count > 0) partSeqs[ep.EpisodeId] = withDisc;
            }

            var used = new HashSet<int>();
            var result = new List<PlaylistProposal>();
            foreach (var (file, durations) in playlists)
            {
                var proposal = new PlaylistProposal { PlaylistFile = file };
                for (int i = 0; i < durations.Count; i++) proposal.Chapters.Add(new ChapterProposal());

                // チャプター列を先頭から走査し、話のパート列が収まる所を順に当てていく（全話連続のプレイリストにも対応）。
                // 次に探すのは直前に当てた話の次の話数を優先し、無ければ未使用の話のうち最も手前で収まるもの。
                // 同じ尺の話が複数あるときは候補の数を添える。当たらなかったチャプターは余白（BLANK）。
                var matchedEpisodes = new List<int>();
                int pos = 0;
                int? lastEpNo = null;
                while (pos < durations.Count)
                {
                    (Episode Episode, int Offset)? best = null;
                    int candidateCount = 0;
                    var next = lastEpNo is int ln ? episodes.FirstOrDefault(e => e.SeriesEpNo == ln + 1 && !used.Contains(e.EpisodeId)) : null;
                    if (next is not null && partSeqs.TryGetValue(next.EpisodeId, out var nextParts))
                    {
                        int off = FindOffset(durations, nextParts, pos);
                        if (off >= 0) best = (next, off);
                    }
                    if (best is null)
                    {
                        foreach (var ep in episodes)
                        {
                            if (used.Contains(ep.EpisodeId) || !partSeqs.TryGetValue(ep.EpisodeId, out var parts)) continue;
                            int off = FindOffset(durations, parts, pos);
                            if (off < 0) continue;
                            if (best is null || off < best.Value.Offset) best = (ep, off);
                        }
                    }
                    if (best is null) break;

                    var (picked, offset) = best.Value;
                    var pickedParts = partSeqs[picked.EpisodeId];
                    foreach (var ep in episodes)
                    {
                        if (!used.Contains(ep.EpisodeId) && partSeqs.TryGetValue(ep.EpisodeId, out var parts)
                            && parts.Count == pickedParts.Count && FindOffset(durations, parts, offset) == offset)
                            candidateCount++;
                    }
                    used.Add(picked.EpisodeId);
                    matchedEpisodes.Add(picked.EpisodeId);
                    lastEpNo = picked.SeriesEpNo;
                    if (candidateCount > proposal.CandidateCount) proposal.CandidateCount = candidateCount;
                    for (int k = 0; k < pickedParts.Count; k++)
                    {
                        var ch = proposal.Chapters[offset + k];
                        ch.Kind = "EPISODE_PART";
                        ch.EpisodeId = picked.EpisodeId;
                        ch.EpisodeSeq = pickedParts[k].EpisodeSeq;
                        ch.DiffMs = (long)durations[offset + k] - ExpectedMs(pickedParts[k], k == pickedParts.Count - 1);
                    }
                    pos = offset + pickedParts.Count;
                }

                foreach (var ch in proposal.Chapters) ch.Kind ??= matchedEpisodes.Count > 0 ? "BLANK" : null;
                if (matchedEpisodes.Count == 1)
                {
                    proposal.Kind = "EPISODE";
                    proposal.EpisodeId = matchedEpisodes[0];
                }
                else if (matchedEpisodes.Count >= 2)
                {
                    proposal.Kind = "PLAY_ALL";
                }
                else
                {
                    // 一致する話が無い。パート列の 2 倍以上のチャプターを持つなら全話連続の候補、それ以外は特典などの候補。
                    int typicalParts = partSeqs.Count > 0 ? (int)Math.Round(partSeqs.Values.Average(p => p.Count)) : 0;
                    proposal.Kind = typicalParts > 0 && durations.Count >= typicalParts * 2 ? "PLAY_ALL" : null;
                }
                result.Add(proposal);
            }
            return result;
        }

        /// <summary>パート列がチャプター列の <paramref name="from"/> 以降で連続した部分列として一致する最初の開始位置。無ければ -1。</summary>
        private static int FindOffset(IReadOnlyList<ulong> durations, List<EpisodePart> parts, int from = 0)
        {
            if (parts.Count == 0 || parts.Count > durations.Count) return -1;
            for (int offset = from; offset + parts.Count <= durations.Count; offset++)
            {
                bool ok = true;
                for (int k = 0; k < parts.Count && ok; k++)
                {
                    long diff = (long)durations[offset + k] - ExpectedMs(parts[k], k == parts.Count - 1);
                    if (!IsWithinTolerance(diff)) ok = false;
                }
                if (ok) return offset;
            }
            return -1;
        }
    }
}
