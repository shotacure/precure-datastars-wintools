namespace PrecureDataStars.Data.Models;

/// <summary>credit_cards テーブルに対応するエンティティモデル（PK: card_id）。</summary>
public sealed class CreditCard
{
    /// <summary>カードの主キー（AUTO_INCREMENT）。</summary>
    public int CardId { get; set; }

    /// <summary>所属するクレジット ID（→ credits.credit_id）。</summary>
    public int CreditId { get; set; }

    /// <summary>クレジット内の表示順（1 始まり、credit_id 内 UNIQUE）。</summary>
    public byte CardSeq { get; set; }

    /// <summary>
    /// このカードの見せ方（"CARDS" = 1 画面ずつ切り替わるカード / "ROLL" = 流れるロール）。既定 "CARDS"。
    /// 映画の ED のように「声の出演はカード → スタッフはロール → 著作権表記はまたカード」と
    /// 1 つのクレジットの中で形式が切り替わるため、カード単位で持つ。ロール部分は ROLL のカード 1 枚で表す。
    /// </summary>
    public string Presentation { get; set; } = "CARDS";

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
