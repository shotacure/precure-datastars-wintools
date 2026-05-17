namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_groups テーブルに対応するエンティティモデル（PK: card_group_id）。
/// <para>
/// Tier 内の Group（サブグループ）1 つ = 1 行。<see cref="GroupNo"/> は 1 始まり。
///同 tier 内で役職同士が視覚的にサブグループを成すケース
/// （例：[美術監督・色彩設計] と [撮影監督・撮影助手] が同 tier の中で別塊として表示される）を表現する。
/// </para>
/// <para>
/// Tier が新規作成されると <see cref="GroupNo"/>=1 が 1 行自動投入される運用
/// （CreditCardTiersRepository.InsertAsync で実装）。
/// <see cref="GroupNo"/> は単純なシーケンスで、ユーザーが「+ Group」したときにインクリメントされる。
/// </para>
/// </summary>
public sealed class CreditCardGroup
{
    /// <summary>Group の主キー（AUTO_INCREMENT）。</summary>
    public int CardGroupId { get; set; }

    /// <summary>所属する Tier ID（→ credit_card_tiers.card_tier_id）。</summary>
    public int CardTierId { get; set; }

    /// <summary>グループ番号（1 始まり、同 Tier 内で UNIQUE）。</summary>
    public byte GroupNo { get; set; } = 1;

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}