using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 「前話の放送終了」を基準にした、各エピソードのサブタイトル解禁時刻を事前計算するヘルパー。
/// 前話は同シリーズ内ではなく <c>total_oa_no</c>（シリーズ横断の通算放送回数、コロナ休止等の欠番を
/// 含む放送実績ベースの連番）が自分よりひとつ小さいエピソードを指す。シリーズが切り替わる境目
/// （新シリーズ第 1 話）でも、前シリーズ最終話からの連続としてそのまま解禁時刻を算出する。
/// 解禁時刻 = 前話の放送開始時刻（<c>on_air_at</c>、JST 前提）+ 28 分 40 秒
/// （通常回の 8:30 開始なら 08:58:40 相当。本編の実質終了目安がこのオフセットで、
/// 放送開始が 8:30 以外の特番にもそのまま適用できる）。
/// フランチャイズ最初の 1 話（前話が存在しない）や <see cref="Episode.TotalOaNo"/> 未設定の話は
/// 解禁時刻を算出できないため対象外＝常に解禁済み扱いとする。
/// </summary>
public static class SubtitleEmbargoCalculator
{
    private static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    /// <summary>前話の放送開始時刻からサブタイトル解禁時刻までのオフセット（28 分 40 秒）。</summary>
    private static readonly TimeSpan RevealOffsetFromOnAir = new(0, 28, 40);

    /// <summary>
    /// ビルド時点からこの日数より過去の解禁時刻は辞書から除外する。
    /// 除外された話はビルド時点で確実に解禁済みのため、常に解禁済みとして扱ってよい
    /// （判定自体はブラウザ側 JS が現在時刻と比較して行うため、本フィルタは表示可否を左右しない）。
    /// 1000 話超のエピソード一覧ページで無駄な data-reveal-at 属性を量産しないための絞り込み。
    /// </summary>
    private static readonly TimeSpan RetentionHorizon = TimeSpan.FromDays(60);

    /// <summary>
    /// 全エピソードからサブタイトル解禁時刻の辞書（episode_id → 解禁時刻）を構築する。
    /// 解禁時刻がビルド時点から <see cref="RetentionHorizon"/> より過去のエピソードは含めない。
    /// </summary>
    /// <param name="episodes">全エピソード（is_deleted = 0 済み前提）。</param>
    /// <param name="buildTime">ビルド実行時刻。</param>
    public static IReadOnlyDictionary<int, DateTimeOffset> Build(
        IEnumerable<Episode> episodes, DateTimeOffset buildTime)
    {
        var ordered = episodes
            .Where(e => e.TotalOaNo.HasValue)
            .OrderBy(e => e.TotalOaNo!.Value)
            .ToList();

        var horizon = buildTime - RetentionHorizon;
        var result = new Dictionary<int, DateTimeOffset>();
        for (int i = 1; i < ordered.Count; i++)
        {
            var prevEp = ordered[i - 1];
            var curEp = ordered[i];
            var prevOnAirJst = new DateTimeOffset(
                DateTime.SpecifyKind(prevEp.OnAirAt, DateTimeKind.Unspecified), JstOffset);
            var revealAt = prevOnAirJst + RevealOffsetFromOnAir;
            if (revealAt < horizon) continue;
            result[curEp.EpisodeId] = revealAt;
        }
        return result;
    }
}
