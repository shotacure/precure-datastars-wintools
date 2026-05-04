namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_cards テーブルに対応するエンティティモデル（PK: card_id）。
/// <para>
/// クレジット内のカード 1 枚 = 1 行。提示形式 = "ROLL"（巻物）のクレジットでは
/// <see cref="CardSeq"/>=1 の 1 行のみが立ち、その下に複数の役職／ブロックがぶら下がる。
/// 提示形式 = "CARDS" の場合はカードを順送りで切り替えるたびに 1 行ずつ並ぶ。
/// </para>
/// </summary>
public sealed class CreditCard
{
    /// <summary>カードの主キー（AUTO_INCREMENT）。</summary>
    public int CardId { get; set; }

    /// <summary>所属するクレジット ID（→ credits.credit_id）。</summary>
    public int CreditId { get; set; }

    /// <summary>クレジット内の表示順（1 始まり、credit_id 内 UNIQUE）。</summary>
    public byte CardSeq { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
