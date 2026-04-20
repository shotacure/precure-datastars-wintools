namespace PrecureDataStars.Data.Models;

/// <summary>
/// video_chapters テーブルに対応するエンティティモデル（複合 PK: catalog_no + chapter_no）。
/// <para>
/// BD/DVD ディスク上のチャプター情報を保持する。<see cref="Track"/> が CD-DA 専用であるのと対に、
/// こちらは光学映像ディスク（Blu-ray / DVD）専用。
/// </para>
/// <remarks>
/// BDAnalyzer の MPLS/IFO パース結果が <see cref="StartTimeMs"/> / <see cref="DurationMs"/> /
/// <see cref="PlaylistFile"/> / <see cref="SourceKind"/> を自動投入する。
/// <see cref="Title"/> / <see cref="PartType"/> / <see cref="Notes"/> は後から手動で埋める想定。
/// </remarks>
/// </summary>
public sealed class VideoChapter
{
    /// <summary>所属ディスクの品番（→ discs.catalog_no、PK の 1 段目）。</summary>
    public string CatalogNo { get; set; } = "";

    /// <summary>シリアルなチャプター番号（1 始まり、PK の 2 段目）。</summary>
    public ushort ChapterNo { get; set; }

    /// <summary>チャプタータイトル（手動入力）。読み取り直後は NULL。</summary>
    public string? Title { get; set; }

    /// <summary>パート種別（→ part_types.part_type）。AVANT / OP / PART_A 等。読み取り直後は NULL。</summary>
    public string? PartType { get; set; }

    /// <summary>プレイリスト先頭からのチャプター開始時刻（ミリ秒）。</summary>
    public ulong StartTimeMs { get; set; }

    /// <summary>チャプターの長さ（ミリ秒）。</summary>
    public ulong DurationMs { get; set; }

    /// <summary>パース元のプレイリストファイル名（BD は mpls、DVD は IFO のファイル名）。</summary>
    public string? PlaylistFile { get; set; }

    /// <summary>パース元の種別。MPLS = Blu-ray、IFO = DVD、MANUAL = 手動追加。</summary>
    public string SourceKind { get; set; } = "MPLS";

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
