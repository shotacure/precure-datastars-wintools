namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_aliases テーブルに対応するエンティティモデル（PK: alias_id）。
/// <para>
/// キャラクターの名義（表記）。話数や状況による表記揺れを記録する。
/// 例: 同じ <c>character_id</c> に対して "美墨なぎさ" / "キュアブラック" / "ブラック" /
/// "ふたりはプリキュア　なぎさ" のように複数 alias が並ぶ。
/// クレジットの声優出演エントリは本テーブルの alias を参照する。
/// </para>
/// <para>
/// v1.2.1 で <c>valid_from</c> / <c>valid_to</c> 列を物理削除した。alias 自体は
/// 時系列情報を持たず、表記揺れごとに別 alias 行として並存させる運用に統一している
/// （声優交代等の期間管理は character_voice_castings 側で REGULAR / SUBSTITUTE /
/// TEMPORARY / MOB の役割と valid_from / valid_to を併用して扱う）。
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

    /// <summary>名義の英語表記（v1.2.4 追加）。英文クレジット出力で使用。</summary>
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
