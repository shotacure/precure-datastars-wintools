using PrecureDataStars.Data.Models;

namespace PrecureDataStars.OaVerifier.Forms;

/// <summary>
/// エピソードの 1 パートの検証行。確認のため全パートを表示し、
/// 【本放送未確認】マーカーを持つパート（<see cref="IsUnconfirmed"/>）だけを強調・承認対象にする。
/// 開始/終了オフセット（番組先頭からの累積秒）と承認状態を保持し、
/// 元の <see cref="EpisodePart"/> を承認時の notes 更新のために抱える。
/// </summary>
internal sealed class PartRow
{
    /// <summary>エピソード内のパート連番（episode_seq）。</summary>
    public required byte EpisodeSeq { get; init; }

    /// <summary>パート種別コード（AVANT / OPENING / CM1 / SPONSOR_CREDIT_A など）。</summary>
    public required string PartType { get; init; }

    /// <summary>開始オフセット（番組先頭からの累積秒）。</summary>
    public required int StartOffsetSec { get; init; }

    /// <summary>終了オフセット（番組先頭からの累積秒）。</summary>
    public required int EndOffsetSec { get; init; }

    /// <summary>OA 尺（秒）。表示用。</summary>
    public required int OaLengthSec { get; init; }

    /// <summary>【本放送未確認】マーカーを持つ（＝確認対象）パートか。</summary>
    public required bool IsUnconfirmed { get; init; }

    /// <summary>承認（マーカー除去）済みか。<see cref="IsUnconfirmed"/> が true のパートにのみ意味がある。</summary>
    public bool Approved { get; set; }

    /// <summary>承認時の notes 更新に使う元レコード。</summary>
    public required EpisodePart Source { get; init; }

    /// <summary>開始オフセットの mm:ss 表記。</summary>
    public string StartLabel => Fmt(StartOffsetSec);

    /// <summary>終了オフセットの mm:ss 表記。</summary>
    public string EndLabel => Fmt(EndOffsetSec);

    /// <summary>OA 尺の表記（秒）。</summary>
    public string OaLabel => $"{OaLengthSec}s";

    /// <summary>状態ラベル。未確認パートのみ「未確認 / 承認済」、それ以外は「—」。</summary>
    public string StatusLabel => !IsUnconfirmed ? "—" : (Approved ? "承認済" : "未確認");

    /// <summary>薄い赤で強調すべき行か（未確認かつ未承認）。</summary>
    public bool Highlight => IsUnconfirmed && !Approved;

    private static string Fmt(int totalSec) => $"{totalSec / 60:00}:{totalSec % 60:00}";
}
