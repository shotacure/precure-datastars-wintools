namespace PrecureDataStars.Data.Models;

/// <summary>
/// company_aliases テーブルに対応するエンティティモデル（PK: alias_id）。
/// <para>
/// 企業の名義（屋号）。1 company に多くの alias が紐付き、屋号変更時は
/// <c>predecessor_alias_id</c> / <c>successor_alias_id</c> でリンクする。
/// 分社化等で別 company にまたがるリンクも自参照 FK を許容しているため、
/// 同じ 2 列で表現できる。クレジット中の企業エントリやロゴ親は本 alias を指す。
/// </para>
/// </summary>
public sealed class CompanyAlias
{
    /// <summary>名義の主キー（AUTO_INCREMENT）。</summary>
    public int AliasId { get; set; }

    /// <summary>所属する企業 ID（→ companies.company_id）。</summary>
    public int CompanyId { get; set; }

    /// <summary>屋号表記（必須、例: "東映動画" "東映アニメーション"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>屋号表記の読み。</summary>
    public string? NameKana { get; set; }

    /// <summary>屋号の英語表記（v1.2.4 追加）。英文クレジット出力で使用。</summary>
    public string? NameEn { get; set; }

    /// <summary>前任名義 ID（屋号変更前の名義を指す自参照、任意）。</summary>
    public int? PredecessorAliasId { get; set; }

    /// <summary>後任名義 ID（屋号変更後の名義を指す自参照、任意）。</summary>
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
