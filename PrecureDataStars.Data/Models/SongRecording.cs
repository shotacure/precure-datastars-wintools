namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_recordings テーブルに対応するエンティティモデル（PK: song_recording_id）。
/// <para>
/// 歌の「歌唱者バージョン」を表す。同一 <see cref="SongId"/>（＝メロディ + アレンジで 1 意な親曲）に対して、
/// 歌唱者違い・バリエーション違い（例: 「五條真由美 Ver.」「うちやえゆか Ver.」）を個別レコードで区別する。
/// </para>
/// <para>
/// 編曲は songs 側、サイズ/パート（フル/TV/カラオケ 等）は tracks 側に移動したため、
/// 当モデルは純粋に「誰が歌っているか」「どういうバリエーションか」だけを表す。
/// </para>
/// </summary>
public sealed class SongRecording
{
    /// <summary>録音の主キー（AUTO_INCREMENT）。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>親曲 ID（→ songs.song_id）。</summary>
    public int SongId { get; set; }

    /// <summary>この録音の歌唱者（カンマ区切りで複数歌唱者にも対応）。</summary>
    public string? SingerName { get; set; }

    /// <summary>この録音の歌唱者（読み）。</summary>
    public string? SingerNameKana { get; set; }

    /// <summary>
    /// 自由ラベル。歌唱者が複数いるとき・バリエーション名がタイトルに含まれないときの補助表記に使う
    /// （例: "メリダ Ver."、"2025 Re-recording"）。
    /// </summary>
    public string? VariantLabel { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
