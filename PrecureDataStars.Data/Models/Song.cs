namespace PrecureDataStars.Data.Models;

/// <summary>songs テーブルに対応するエンティティモデル（PK: song_id）。</summary>
public sealed class Song
{
    /// <summary>曲の主キー（AUTO_INCREMENT）。</summary>
    public int SongId { get; set; }

    /// <summary>曲タイトル（派生アレンジ名込み、例: "DANZEN! ふたりはプリキュア Ver. MaxHeart"）。</summary>
    public string Title { get; set; } = "";

    /// <summary>曲タイトルの読み（ひらがな）。</summary>
    public string? TitleKana { get; set; }

    // 音楽種別は録音単位で持つ設計のため、Song モデルには持たない
    // （カバー・アレンジで OP→キャラソン等に文脈変化するため
    // 種別は <see cref="SongRecording.MusicClassCode"/> で表現する）。

    // 出典シリーズも録音単位で持つ設計のため、Song モデルには持たない
    // （同一曲のカバー版や別作品挿入歌への流用で出典が文脈変化するため
    // 出典は <see cref="SongRecording.SeriesId"/> で表現する）。

    /// <summary>作詞者名。</summary>
    public string? LyricistName { get; set; }

    /// <summary>作詞者名（読み）。</summary>
    public string? LyricistNameKana { get; set; }

    /// <summary>作曲者名。</summary>
    public string? ComposerName { get; set; }

    /// <summary>作曲者名（読み）。</summary>
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