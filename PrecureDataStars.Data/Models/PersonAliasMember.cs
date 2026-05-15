namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_alias_members テーブルに対応するエンティティモデル（複合 PK: parent_alias_id + member_seq）。
/// <para>
/// ユニット名義（連名・グループの単位として使う <see cref="PersonAlias"/>）の構成メンバーを
/// 順序付きで保持する。メンバーは人物名義（PERSON）またはキャラクター名義（CHARACTER）。
/// </para>
/// <para>
/// 例:
/// <list type="bullet">
///   <item><description>「キュア・カルテット」(unit alias) → 五條真由美 / うちやえゆか / 工藤真由 / 宮本佳那子（PERSON 4 名）</description></item>
///   <item><description>「スマイルプリキュア!」(unit alias) → キュアハッピー / キュアサニー / ... （CHARACTER 5 名、display_text_override で詳細表記）</description></item>
/// </list>
/// </para>
/// <para>
/// ネスト禁止：本テーブルにメンバーとして登場した PERSON alias がさらに別ユニットの親になる、
/// または親が他ユニットのメンバーになる、という構造はトリガーで弾かれる。
/// </para>
/// </summary>
public sealed class PersonAliasMember
{
    /// <summary>所属するユニット側の名義 ID（→ person_aliases.alias_id）。</summary>
    public int ParentAliasId { get; set; }

    /// <summary>連名の表示順（1 始まり）。</summary>
    public byte MemberSeq { get; set; }

    /// <summary>メンバー種別（PERSON / CHARACTER）。</summary>
    public PersonAliasMemberKind MemberKind { get; set; }

    /// <summary>
    /// メンバーが人物名義の場合の参照先（→ person_aliases.alias_id）。
    /// <see cref="MemberKind"/> が PERSON のとき非 NULL。
    /// </summary>
    public int? MemberPersonAliasId { get; set; }

    /// <summary>
    /// メンバーがキャラクター名義の場合の参照先（→ character_aliases.alias_id）。
    /// <see cref="MemberKind"/> が CHARACTER のとき非 NULL。
    /// </summary>
    public int? MemberCharacterAliasId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// <see cref="PersonAliasMember.MemberKind"/> の種別。
/// </summary>
public enum PersonAliasMemberKind
{
    /// <summary>人物名義（→ person_aliases）。</summary>
    Person = 0,
    /// <summary>キャラクター名義（→ character_aliases）。</summary>
    Character = 1
}