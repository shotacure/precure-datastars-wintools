namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_aliases テーブルに対応するエンティティモデル（PK: alias_id）。
/// <para>
/// 人物の名義（表記）。1 person に多くの alias が紐付き、改名時は
/// <c>predecessor_alias_id</c> / <c>successor_alias_id</c> でリンクすることで
/// 表記履歴を辿れるようにする。alias と person の結び付けは
/// <c>person_alias_persons</c> 中間テーブル経由（通常 1:1、共同名義のみ多対多）。
/// </para>
/// </summary>
public sealed class PersonAlias
{
    /// <summary>名義の主キー（AUTO_INCREMENT）。</summary>
    public int AliasId { get; set; }

    /// <summary>名義表記（必須、例: "金月真美" "ゆう" "ゆうきまさみ"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>名義表記の読み。</summary>
    public string? NameKana { get; set; }

    /// <summary>前任名義 ID（改名前の名義を指す自参照、任意）。</summary>
    public int? PredecessorAliasId { get; set; }

    /// <summary>後任名義 ID（改名後の名義を指す自参照、任意）。</summary>
    public int? SuccessorAliasId { get; set; }

    /// <summary>名義の有効開始日（任意）。</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>名義の有効終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

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
