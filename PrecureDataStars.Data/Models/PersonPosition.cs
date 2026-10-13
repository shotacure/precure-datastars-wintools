namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_positions テーブルに対応するエンティティモデル（PK: position_id）。
/// 人物の「役職履歴」1 件（1 回の異動で就いた役職）。人物詳細に「役職履歴（会社名）」の見出しで
/// 「異動日 ｜ 役職」の表として出す。
/// </summary>
public sealed class PersonPosition
{
    /// <summary>主キー（AUTO_INCREMENT）。</summary>
    public int PositionId { get; set; }

    /// <summary>人物。</summary>
    public int PersonId { get; set; }

    /// <summary>その役職の会社（任意。サイトでは見出しに会社名を添えて企業詳細へリンクする）。</summary>
    public int? CompanyId { get; set; }

    /// <summary>人物ごとの並び順（小さい順。同じなら <see cref="EffectiveDate"/> 順）。日付不明の行もこの順で置く。</summary>
    public int DisplayOrder { get; set; }

    /// <summary>異動日。null は日付不明（サイトでは「？」と出す）。</summary>
    public DateTime? EffectiveDate { get; set; }

    /// <summary>役職（発表の表記どおり）。</summary>
    public string PositionText { get; set; } = "";

    /// <summary>裏取りに使った発表のページ（内部用。サイトには出さない）。</summary>
    public string? SourceUrl { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
