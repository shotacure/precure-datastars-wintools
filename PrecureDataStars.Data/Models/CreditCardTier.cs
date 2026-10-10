namespace PrecureDataStars.Data.Models;

/// <summary>credit_card_tiers テーブルに対応するエンティティモデル（PK: card_tier_id）。</summary>
public sealed class CreditCardTier
{
    /// <summary>
    /// 1 カード内で使える段組番号の上限（DB の ck_card_tier_no と同じ値）。
    /// 2 列に並んだ役職の左の列・右の列と、その下の中央に置かれた役職のように、横位置の違うまとまりごとに段を分ける。
    /// 流れるクレジット（ROLL）は 1 枚の中で横位置が何度も変わるので、9 段まで持てる。
    /// </summary>
    public const byte MaxTierNo = 9;

    /// <summary>Tier の主キー（AUTO_INCREMENT）。</summary>
    public int CardTierId { get; set; }

    /// <summary>所属するカード ID（→ credit_cards.card_id）。</summary>
    public int CardId { get; set; }

    /// <summary>段組番号（1 始まり、<see cref="MaxTierNo"/> まで）。</summary>
    public byte TierNo { get; set; } = 1;

    /// <summary>画面の縦の位置（"T" = 上 / "M" = 中 / "B" = 下）。null は未確認。
    /// ティアは横位置の違うまとまりで、<see cref="TierNo"/> は並び順だけを表すため、位置を情報として持つ。
    /// サイトのクレジットはティアを縦に積んで出し、位置は表示に使わない。</summary>
    public string? PositionV { get; set; }

    /// <summary>画面の横の位置（"L" = 左 / "C" = 中央 / "R" = 右）。null は未確認。</summary>
    public string? PositionH { get; set; }

    /// <summary>位置を「TL」「BC」のような 2 文字（縦＋横）で返す。どちらかが未確認なら null。</summary>
    public string? PositionCode => string.IsNullOrEmpty(PositionV) || string.IsNullOrEmpty(PositionH) ? null : PositionV + PositionH;

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
