namespace PrecureDataStars.Data.Models;

/// <summary>
/// songs テーブルに対応するエンティティモデル（PK: song_id）。
/// <para>
/// 「メロディ + アレンジ」として 1 意な歌マスタ。同じメロディでも編曲が違えば別の <c>Song</c> 行となる
/// （例: 「DANZEN! ふたりはプリキュア」と「DANZEN! ふたりはプリキュア Ver. MaxHeart」は別レコード）。
/// 歌唱者違いは <c>song_recordings</c> テーブル側で表現する。
/// サイズ違い（フル/TV/ショート 等）・パート違い（ボーカル/カラオケ 等）は <c>tracks</c> 側の
/// <c>song_size_variant_code</c> / <c>song_part_variant_code</c> で表現する。
/// </para>
/// </summary>
public sealed class Song
{
    /// <summary>曲の主キー（AUTO_INCREMENT）。</summary>
    public int SongId { get; set; }

    /// <summary>曲タイトル（派生アレンジ名込み、例: "DANZEN! ふたりはプリキュア Ver. MaxHeart"）。</summary>
    public string Title { get; set; } = "";

    /// <summary>曲タイトルの読み（ひらがな）。</summary>
    public string? TitleKana { get; set; }

    /// <summary>音楽種別コード（→ song_music_classes）。OP/ED/挿入歌等。</summary>
    public string? MusicClassCode { get; set; }

    /// <summary>曲の出自シリーズ ID（→ series）。</summary>
    public int? SeriesId { get; set; }

    /// <summary>作詞者名。v1.1.2 で <c>OriginalLyricistName</c> から改名（songs の他カラムと合わせて接頭辞 original_ を撤去）。</summary>
    public string? LyricistName { get; set; }

    /// <summary>作詞者名（読み）。v1.1.2 で <c>OriginalLyricistNameKana</c> から改名。</summary>
    public string? LyricistNameKana { get; set; }

    /// <summary>作曲者名。v1.1.2 で <c>OriginalComposerName</c> から改名。</summary>
    public string? ComposerName { get; set; }

    /// <summary>作曲者名（読み）。v1.1.2 で <c>OriginalComposerNameKana</c> から改名。</summary>
    public string? ComposerNameKana { get; set; }

    /// <summary>編曲者名。songs がアレンジ単位になったためここに持つ。</summary>
    public string? ArrangerName { get; set; }

    /// <summary>編曲者名（読み）。</summary>
    public string? ArrangerNameKana { get; set; }

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