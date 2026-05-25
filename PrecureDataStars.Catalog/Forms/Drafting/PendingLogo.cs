namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集 Draft セッションで「保存時に logos へ投入する予定」のロゴ 1 件。
/// LOGO 種別エントリの「+ 新規...」（<c>QuickAddLogoDialog</c>）から作成される。
/// <see cref="CompanyAliasId"/> は実 ID（既存企業屋号）でも仮 ID（同セッションの Pending CompanyAlias）でも
/// 受け取れる設計で、<c>CreditSaveService</c> Phase 0 が「先に CompanyAlias を解決 → その実 ID で Logo を INSERT」
/// の順序を保証する。
/// </summary>
public sealed class PendingLogo
{
    /// <summary>払い出した負数 logo_id。<c>DraftEntry.Entity.LogoId</c> の置換キー。</summary>
    public required int TempLogoId { get; init; }

    /// <summary><c>logos.company_alias_id</c>。正数なら既存実 ID、負数なら同セッション内の Pending CompanyAlias を指す。
    /// 保存時には負数なら Phase 0 の CompanyAlias 解決後の実 ID に置換してから Logo INSERT を行う。</summary>
    public required int CompanyAliasId { get; init; }

    /// <summary><c>logos.ci_version_label</c>。CI バージョン文字列。</summary>
    public string? CiVersionLabel { get; init; }

    /// <summary><c>logos.valid_from</c>。任意。</summary>
    public DateTime? ValidFrom { get; init; }

    /// <summary><c>logos.valid_to</c>。任意。</summary>
    public DateTime? ValidTo { get; init; }

    /// <summary><c>logos.description</c>。任意。</summary>
    public string? Description { get; init; }

    /// <summary><c>logos.notes</c>。任意。</summary>
    public string? Notes { get; init; }
}
