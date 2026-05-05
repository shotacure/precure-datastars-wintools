namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_alias_persons テーブルに対応する中間モデル（PK: alias_id, person_id）。
/// <para>
/// 名義 ⇄ 人物の多対多関連。通常は 1 alias につき 1 行（同一人物 1 名義）だが、
/// 共同名義（複数人物が 1 つの表記を共有する稀ケース）にも対応するため多対多としている。
/// 共同名義の場合 <see cref="PersonSeq"/> が表示順を保持する。
/// </para>
/// </summary>
public sealed class PersonAliasPerson
{
    /// <summary>名義 ID（PK の一部、→ person_aliases.alias_id）。</summary>
    public int AliasId { get; set; }

    /// <summary>人物 ID（PK の一部、→ persons.person_id）。</summary>
    public int PersonId { get; set; }

    /// <summary>共同名義における表示順（既定 1）。</summary>
    public byte PersonSeq { get; set; } = 1;

    /// <summary>レコード作成日時（監査用）。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>レコード最終更新日時（監査用）。</summary>
    public DateTime? UpdatedAt { get; set; }
}
