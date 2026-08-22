using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// エピソード単位の主題歌・挿入歌（<c>episode_theme_songs</c>）をシリーズ単位に集約して、
/// シリーズ詳細ページの「主題歌・挿入歌」セクション用 <see cref="ThemeSongDescriptor"/> 列に畳み込むヘルパ。
/// 映画系列（<c>credit_attach_to='SERIES'</c>）は <c>series_theme_songs</c> をそのまま使うため対象外で、
/// エピソードを持つ系列（TV / SPIN-OFF / OTONA / SHORT）だけがここを通る。
/// <para>集約単位は <c>(theme_kind, song_recording_id, is_broadcast_only)</c> の 3 つ組。
/// 同じ曲が同じ枠で何話にもわたって使われていても 1 行に畳み、使用話数は範囲ラベルとして持つ。</para>
/// <para>並びは「最初に流れたもの」優先で、
/// 劇中順（OP → 挿入歌 → ED）→ 初出話数昇順 → 本放送優先 → song_recording_id の 4 段。
/// この順序を後段の <see cref="ThemeSongRowBuilder"/> にそのまま通すため、
/// 確定した並び順を <see cref="ThemeSongDescriptor.Seq"/> の連番として載せる
/// （ビルダ側は Seq 昇順で並べ直すので、結果として本クラスが決めた順が保たれる）。</para>
/// </summary>
public static class ThemeSongSeriesAggregator
{
    /// <summary>
    /// 劇中順の既定序列。<c>episode_theme_songs.seq</c> は「エピソード内の劇中順」を表す運用で、
    /// 実データも OP=1 / 挿入歌=2 / ED=3 で入っている。
    /// ただし seq はエピソードごとの値なので、挿入歌のある話とない話で ED の seq が 3 と 2 に割れる。
    /// シリーズ単位に畳んだあとの序列はこの種別マップで決め、seq そのものは使わない。
    /// </summary>
    private static int KindOrder(string themeKind) => themeKind switch
    {
        "OP" => 0,
        "INSERT" => 1,
        "ED" => 2,
        _ => 3
    };

    /// <summary>指定シリーズの全エピソードぶんの主題歌行を集約して記述子列を返す。</summary>
    /// <param name="episodes">対象シリーズのエピソード群（順不同でよい）。</param>
    /// <param name="themeSongsByEpisode">episode_id → 主題歌行（<c>BuildContext.ThemeSongsByEpisode</c>）。</param>
    /// <returns>表示順に並んだ記述子列。該当行が 1 件も無ければ空列。</returns>
    public static IReadOnlyList<ThemeSongDescriptor> Build(
        IReadOnlyList<Episode> episodes,
        IReadOnlyDictionary<int, IReadOnlyList<EpisodeThemeSong>> themeSongsByEpisode)
    {
        if (episodes.Count == 0) return Array.Empty<ThemeSongDescriptor>();

        // シリーズ内の全話数（「(全話)」判定の母集合）。
        var allEpisodeNos = episodes.Select(e => e.SeriesEpNo).ToHashSet();

        // (theme_kind, song_recording_id, is_broadcast_only) → 使用話数の集合。
        var episodeNosByGroup = new Dictionary<(string ThemeKind, int SongRecordingId, bool IsBroadcastOnly), HashSet<int>>();
        // theme_kind → 本放送限定行が押さえている話数の集合（差し替え区間の附記に使う）。
        var broadcastOnlyEpisodeNosByKind = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var ep in episodes)
        {
            if (!themeSongsByEpisode.TryGetValue(ep.EpisodeId, out var themes)) continue;
            foreach (var t in themes)
            {
                // 「クレジットされているが実際には流れていない」行は使用実績ではないので集約に含めない
                // （エピソード詳細と同じ規律）。'BROADCAST_NOT_CREDITED' は実際に流れた事実なので含める。
                if (string.Equals(t.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
                    continue;

                var key = (t.ThemeKind, t.SongRecordingId, t.IsBroadcastOnly);
                if (!episodeNosByGroup.TryGetValue(key, out var nos))
                {
                    nos = new HashSet<int>();
                    episodeNosByGroup[key] = nos;
                }
                nos.Add(ep.SeriesEpNo);

                if (t.IsBroadcastOnly)
                {
                    if (!broadcastOnlyEpisodeNosByKind.TryGetValue(t.ThemeKind, out var bcastNos))
                    {
                        bcastNos = new HashSet<int>();
                        broadcastOnlyEpisodeNosByKind[t.ThemeKind] = bcastNos;
                    }
                    bcastNos.Add(ep.SeriesEpNo);
                }
            }
        }

        if (episodeNosByGroup.Count == 0) return Array.Empty<ThemeSongDescriptor>();

        // 表示順を確定してから連番 Seq を振る。
        var ordered = episodeNosByGroup
            .OrderBy(kv => KindOrder(kv.Key.ThemeKind))
            .ThenBy(kv => kv.Value.Min())                       // 初出話数の昇順（ED1 → ED2 の切り替わりがここで並ぶ）
            .ThenByDescending(kv => kv.Key.IsBroadcastOnly)     // 同着なら実際に先に流れた本放送を先に
            .ThenBy(kv => kv.Key.SongRecordingId)               // 決定論の担保
            .ToList();

        var result = new List<ThemeSongDescriptor>(ordered.Count);
        int seq = 0;
        foreach (var kv in ordered)
        {
            var (themeKind, songRecordingId, isBroadcastOnly) = kv.Key;
            broadcastOnlyEpisodeNosByKind.TryGetValue(themeKind, out var broadcastOnlyNos);

            result.Add(new ThemeSongDescriptor(
                SongRecordingId: songRecordingId,
                ThemeKind: themeKind,
                Seq: seq++,
                IsBroadcastOnly: isBroadcastOnly,
                // 集約前に CREDITED_NOT_BROADCAST を除外済みなので、後段ビルダの同名フィルタには掛からない値を渡す。
                // 表示に効くのはこのフィルタだけで、区分ラベルは theme_kind から引かれる。
                UsageActuality: EpisodeThemeSongUsageActualities.Normal,
                // 備考は話ごとの記述なのでシリーズ単位には畳まない。
                Notes: null,
                EpisodeRangeLabel: BuildRangeLabel(
                    kv.Value, allEpisodeNos, isBroadcastOnly, broadcastOnlyNos)));
        }
        return result;
    }

    /// <summary>
    /// 使用話数ラベルを組み立てる。
    /// 素の圧縮表記（「#1～49 (全話)」「#24～47」「#1～34, 39～49」）を基本とし、
    /// 既定行（<c>is_broadcast_only=0</c>）の使用範囲に本放送限定行の差し替えが割り込むときだけ
    /// 「（本放送では #35～38 を除く）」を後置する。
    /// <para>差し替えの記録のされ方は 2 通りありうる（README の「2 行並立」規約と、
    /// 既定行の側を登録しない運用）。どちらでも附記が出るよう、除外集合は
    /// 「自グループの話数のうち本放送限定行と重なるもの」と
    /// 「自グループの範囲内の穴のうち本放送限定行が埋めているもの」の和で求める。
    /// 後者は範囲表記側にも足し戻して、素の穴あき表記（「#1～34, 39～49」）ではなく
    /// 連続範囲＋附記として読ませる。</para>
    /// </summary>
    /// <param name="episodeNos">当該グループの使用話数。</param>
    /// <param name="allEpisodeNos">シリーズ内の全話数（「(全話)」判定の母集合）。</param>
    /// <param name="isBroadcastOnly">当該グループが本放送限定行かどうか。</param>
    /// <param name="broadcastOnlyNos">同一種別で本放送限定行が押さえている話数（無ければ null）。</param>
    private static string BuildRangeLabel(
        HashSet<int> episodeNos,
        HashSet<int> allEpisodeNos,
        bool isBroadcastOnly,
        HashSet<int>? broadcastOnlyNos)
    {
        // 本放送限定行そのものは差し替える側なので附記を持たない
        // （テンプレ側が「（本放送のみ）」バッジを別途出す）。
        if (isBroadcastOnly || broadcastOnlyNos is null || broadcastOnlyNos.Count == 0)
            return EpisodeRangeCompressor.CompressWithAllEpisodesMark(episodeNos, allEpisodeNos);

        // 自グループの範囲内で、本放送限定行に差し替えられている話。
        var excluded = new HashSet<int>(episodeNos.Where(broadcastOnlyNos.Contains));

        // 自グループの範囲内の穴のうち、本放送限定行が埋めているもの。
        int min = episodeNos.Min();
        int max = episodeNos.Max();
        var filledHoles = new HashSet<int>();
        for (int n = min; n <= max; n++)
        {
            if (episodeNos.Contains(n)) continue;
            if (!allEpisodeNos.Contains(n)) continue;
            if (!broadcastOnlyNos.Contains(n)) continue;
            filledHoles.Add(n);
            excluded.Add(n);
        }

        if (excluded.Count == 0)
            return EpisodeRangeCompressor.CompressWithAllEpisodesMark(episodeNos, allEpisodeNos);

        // 穴を埋め戻した集合で範囲表記を作る（連続範囲として読ませ、抜けは附記側に寄せる）。
        var spanNos = filledHoles.Count == 0
            ? episodeNos
            : new HashSet<int>(episodeNos.Concat(filledHoles));

        string baseLabel = EpisodeRangeCompressor.CompressWithAllEpisodesMark(spanNos, allEpisodeNos);
        return $"{baseLabel}（本放送では {EpisodeRangeCompressor.Compress(excluded)} を除く）";
    }
}
