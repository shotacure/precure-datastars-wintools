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

    /// <summary>
    /// カードの版（そのカードが出る媒体の組。"BROADCAST" = 本放送、"BLURAY,STREAMING" = Blu-ray・配信、
    /// "BROADCAST,BLURAY" = 本放送・Blu-ray、"STREAMING" = 配信 など。null = 共通。値と表示名は <see cref="CreditMediaVersions"/>）。
    /// 媒体によって違うカードだけ版を付け、2 つの版のカードを隣どうしに並べる。描画は版の付いたカードが続くところを
    /// 1 組ずつの枠にまとめて、組の前の版を上・後ろの版を下に出す。集計は共通のカードと後ろの版のカードで数え、
    /// 前の版のカードは後ろの版のカードにいない人だけを数える。
    /// </summary>
    public string? MediaVersion { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
