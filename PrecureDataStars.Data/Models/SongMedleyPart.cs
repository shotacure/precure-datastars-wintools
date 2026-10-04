namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_medley_parts テーブルに対応するエンティティモデル（複合 PK: medley_song_id + part_seq）。
/// メドレーの曲（<see cref="MedleySongId"/>）の中の 1 曲を、何曲目か（<see cref="PartSeq"/>）と原曲（<see cref="SourceSongId"/>）で持つ。
/// 原曲の曲名・作詞・作曲・編曲は原曲（songs / song_credits）から引く。
/// </summary>
public sealed class SongMedleyPart
{
    /// <summary>メドレーの曲（→ songs.song_id）。</summary>
    public int MedleySongId { get; set; }

    /// <summary>メドレーの中で何曲目か（1 始まり）。同じ原曲が何度出てもよい。</summary>
    public byte PartSeq { get; set; }

    /// <summary>原曲（→ songs.song_id）。版違いは表記どおりの版の曲を指す。原曲が DB に無いときは null。</summary>
    public int? SourceSongId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
