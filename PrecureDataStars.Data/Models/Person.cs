namespace PrecureDataStars.Data.Models;

/// <summary>
/// persons テーブルに対応するエンティティモデル（PK: person_id）。
/// <para>
/// 「同一人物としての同一性」を持たせる単位。表記揺れや改名は <c>person_aliases</c> 側の
/// 名義レコードで管理し、本テーブルは「個人」一意の器として機能する。
/// 結合は <c>person_alias_persons</c> 経由（通常 1 alias = 1 person、共同名義の稀ケースのみ多対多）。
/// </para>
/// </summary>
public sealed class Person
{
    /// <summary>人物の主キー（AUTO_INCREMENT）。</summary>
    public int PersonId { get; set; }

    /// <summary>姓（任意）。</summary>
    public string? FamilyName { get; set; }

    /// <summary>名（任意）。</summary>
    public string? GivenName { get; set; }

    /// <summary>フルネーム表示名（必須）。一般に "姓 名" を半角スペース区切りで保持。</summary>
    public string FullName { get; set; } = "";

    /// <summary>フルネームの読み（ひらがな等）。</summary>
    public string? FullNameKana { get; set; }

    /// <summary>英語表記（任意）。</summary>
    public string? NameEn { get; set; }

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
