namespace PrecureDataStars.Data.Models;

/// <summary>
/// logos テーブルに対応するエンティティモデル（PK: logo_id）。
/// <para>
/// 企業ロゴマスタ。屋号（<c>company_alias</c>）配下に CI バージョン違いのロゴが紐付く構造。
/// クレジット中で企業を表現する場合、屋号（テキスト寄り）またはロゴ（特定 CI バージョン）の
/// どちらか一方を指す。<see cref="CiVersionLabel"/> は同一屋号下で UNIQUE。
/// </para>
/// </summary>
public sealed class Logo
{
    /// <summary>ロゴの主キー（AUTO_INCREMENT）。</summary>
    public int LogoId { get; set; }

    /// <summary>所属する企業名義 ID（→ company_aliases.alias_id）。</summary>
    public int CompanyAliasId { get; set; }

    /// <summary>CI バージョンラベル（必須、例: "1990年版" "2003年リニューアル"）。屋号下で UNIQUE。</summary>
    public string CiVersionLabel { get; set; } = "";

    /// <summary>当該ロゴの有効開始日（任意）。</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>当該ロゴの有効終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>説明（任意、例: 形状の特徴やデザイナー情報など）。</summary>
    public string? Description { get; set; }

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
