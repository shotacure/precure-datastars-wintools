namespace PrecureDataStars.Data.Models;

/// <summary>
/// company_relations テーブルに対応するエンティティモデル（PK: relation_id）。
/// 企業・団体どうしの関係を「団体 → 団体」の向きと期間つきで持つ。
/// <list type="bullet">
///   <item><see cref="CompanyRelationKinds.Parent"/>：<see cref="FromCompanyId"/> が親、<see cref="ToCompanyId"/> が子（子会社・部署・編集部・雑誌など）。</item>
///   <item><see cref="CompanyRelationKinds.Successor"/>：<see cref="FromCompanyId"/> の事業を <see cref="ToCompanyId"/> が引き継いだ（会社分割・合併・事業譲渡など）。</item>
/// </list>
/// 同じ会社の改名は company_aliases の前後リンクで持ち、本テーブルには入れない。
/// </summary>
public sealed class CompanyRelation
{
    /// <summary>主キー（AUTO_INCREMENT）。</summary>
    public int RelationId { get; set; }

    /// <summary>関係の起点の団体。PARENT なら親、SUCCESSOR なら引き継がれた側（前身）。</summary>
    public int FromCompanyId { get; set; }

    /// <summary>関係の終点の団体。PARENT なら子、SUCCESSOR なら引き継いだ側（後継）。</summary>
    public int ToCompanyId { get; set; }

    /// <summary>関係の種類（<see cref="CompanyRelationKinds"/> の値）。</summary>
    public string RelationKind { get; set; } = CompanyRelationKinds.Parent;

    /// <summary>表示の言い回し（「部署」「子会社」「雑誌」「会社分割」など）。NULL のときは種類ごとの既定の言葉で表示する。</summary>
    public string? RelationLabel { get; set; }

    /// <summary>関係の開始日（任意）。</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>関係の終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>company_relations.relation_kind の値。</summary>
public static class CompanyRelationKinds
{
    /// <summary>所属（from が親、to が子）。</summary>
    public const string Parent = "PARENT";

    /// <summary>事業の引き継ぎ（from が前身、to が後継）。</summary>
    public const string Successor = "SUCCESSOR";
}
