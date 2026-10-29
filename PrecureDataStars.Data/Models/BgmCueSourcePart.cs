namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cue_source_parts テーブルに対応するエンティティモデル（複合 PK: series_id + m_no_detail + part_seq）。
/// 劇伴の曲（<see cref="BgmCue"/>）が使っている曲 1 つを、曲の中で流れる順（<see cref="PartSeq"/>）で持つ。
/// 使われている曲は歌（<see cref="SourceSongId"/>）か曲（著作物、<see cref="SourceWorkId"/>）のどちらか一方だけ
/// （DB の CHECK で担保）。曲名・作家は参照先（songs / song_credits、musical_works / musical_work_credits）から引く。
/// </summary>
public sealed class BgmCueSourcePart
{
    /// <summary>劇伴の曲のシリーズ（→ bgm_cues.series_id、複合 PK 第 1 列）。</summary>
    public int SeriesId { get; set; }

    /// <summary>劇伴の曲の M 番号（→ bgm_cues.m_no_detail、複合 PK 第 2 列）。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>劇伴の曲の中で何曲目に流れるか（1 始まり）。同じ曲が何度出てもよい。</summary>
    public byte PartSeq { get; set; }

    /// <summary>使われている歌（→ songs.song_id）。曲（著作物）の行では null。</summary>
    public int? SourceSongId { get; set; }

    /// <summary>使われている曲（著作物）（→ musical_works.work_id）。歌の行では null。</summary>
    public int? SourceWorkId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
