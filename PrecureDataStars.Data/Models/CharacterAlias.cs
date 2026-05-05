namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_aliases テーブルに対応するエンティティモデル（PK: alias_id）。
/// <para>
/// キャラクターの名義（表記）。話数や状況による表記揺れを記録する。
/// 例: 同じ <c>character_id</c> に対して "美墨なぎさ" / "キュアブラック" / "ブラック" /
/// "ふたりはプリキュア　なぎさ" のように複数 alias が並ぶ。
/// クレジットの声優出演エントリは本テーブルの alias を参照する。
/// </para>
/// </summary>
public sealed class CharacterAlias
{
    /// <summary>名義の主キー（AUTO_INCREMENT）。</summary>
    public int AliasId { get; set; }

    /// <summary>所属するキャラクター ID（→ characters.character_id）。</summary>
    public int CharacterId { get; set; }

    /// <summary>名義表記（必須）。</summary>
    public string Name { get; set; } = "";

    /// <summary>名義表記の読み。</summary>
    public string? NameKana { get; set; }

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
