namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集 Draft セッションで「保存時に company_aliases へ投入する予定」の企業屋号 1 件。
/// COMPANY 系エントリ、ブロックの所属企業（<c>credit_role_blocks.leading_company_alias_id</c>）、
/// エントリの所属企業（<c>credit_block_entries.affiliation_company_alias_id</c>）、Logo の
/// <c>company_alias_id</c> など、複数の参照ポイントで仮 ID が使われる。
/// </summary>
public sealed class PendingCompanyAlias
{
    /// <summary>払い出した負数 alias_id。各種 CompanyAliasId 列の置換キー。</summary>
    public required int TempAliasId { get; init; }

    /// <summary>新規 <c>company_aliases.name</c>（屋号）。必須。</summary>
    public required string AliasName { get; init; }

    /// <summary>新規 <c>company_aliases.name_kana</c>。任意。</summary>
    public string? AliasKana { get; init; }

    /// <summary>新規 <c>company_aliases.name_en</c>。任意。</summary>
    public string? AliasEn { get; init; }

    /// <summary>
    /// 系統A: 既存 <c>companies</c> に新屋号として紐付ける場合の company_id。
    /// non-null なら新規 company 行は作らず、company_aliases に対象 company_id で 1 行 INSERT するだけ。
    /// null なら系統B（新規 company 新設）として扱う。
    /// </summary>
    public int? AttachToExistingCompanyId { get; init; }

    /// <summary>系統B: <c>companies.name</c>（正式名称、系統A の場合は無視）。</summary>
    public string? CompanyName { get; init; }

    /// <summary>系統B: <c>companies.name_kana</c>。</summary>
    public string? CompanyNameKana { get; init; }

    /// <summary>系統B: <c>companies.name_en</c>。</summary>
    public string? CompanyNameEn { get; init; }
}
