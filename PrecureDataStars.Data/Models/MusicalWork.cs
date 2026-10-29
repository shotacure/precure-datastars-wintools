namespace PrecureDataStars.Data.Models;

/// <summary>
/// musical_works テーブルに対応するエンティティモデル（PK: work_id）。
/// 曲（著作物）：歌でも劇伴でもない、JASRAC に登録された曲だけを持つ
/// （例：メドレーの劇伴の中にだけ出てくる曲で、どの盤にも単独では入っていないもの）。
/// 歌は <see cref="Song"/>、劇伴は <see cref="BgmCue"/> で持ち、ここには入れない。
/// 作家は <see cref="MusicalWorkCredit"/>、劇伴の曲からの参照は <see cref="BgmCueSourcePart"/>。
/// </summary>
public sealed class MusicalWork
{
    /// <summary>曲（著作物）の主キー（AUTO_INCREMENT）。</summary>
    public int WorkId { get; set; }

    /// <summary>曲名。</summary>
    public string Title { get; set; } = "";

    /// <summary>曲名の読み（ひらがな）。</summary>
    public string? TitleKana { get; set; }

    /// <summary>JASRAC の作品コード（例 <c>242-0576-1</c>）。重複不可。</summary>
    public string? JasracWorkCode { get; set; }

    /// <summary>ISWC（例 <c>T-927.548.991-3</c>）。重複不可。</summary>
    public string? Iswc { get; set; }

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
