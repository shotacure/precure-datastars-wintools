namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集 Draft セッションで「保存時に person_aliases へ投入する予定」の名義 1 件を表す。
/// 「適用」「+ 新規...」の時点では DB に書き込まず、仮の負数 alias_id を払い出して
/// <see cref="CreditDraftSession.PendingPersonAliases"/> に積み、各 <c>DraftEntry.Entity.PersonAliasId</c>
/// にもその負数を入れる。保存ボタンで <c>CreditSaveService</c> が Phase 0 を実行する際に、
/// 本オブジェクトの内容を元に <c>persons</c>（必要なら）→ <c>person_aliases</c> → <c>person_alias_persons</c>
/// を INSERT し、得られた実 alias_id で全 Entry の負数 ID を置換する。
/// </summary>
public sealed class PendingPersonAlias
{
    /// <summary><see cref="CreditDraftSession.AllocateTempId"/> で払い出した負数 alias_id。
    /// 全 <c>DraftEntry.Entity.PersonAliasId</c> のうち、この値と一致するものが保存時に実 ID に置換される。</summary>
    public required int TempAliasId { get; init; }

    /// <summary>新規 <c>person_aliases.name</c>。必須。</summary>
    public required string AliasName { get; init; }

    /// <summary>新規 <c>person_aliases.name_kana</c>。任意。</summary>
    public string? AliasKana { get; init; }

    /// <summary>新規 <c>person_aliases.name_en</c>。任意。</summary>
    public string? AliasEn { get; init; }

    /// <summary>新規 <c>person_aliases.display_text_override</c>。任意。
    /// ユニット名義等で定形外の表示文字列を上書きしたいとき。</summary>
    public string? AliasDisplayOverride { get; init; }

    /// <summary>
    /// 系統A: 既存 <c>persons</c> に新名義として紐付ける場合の person_id。
    /// non-null なら新規 person 行は作らず、既存 person との紐付け（<c>person_alias_persons</c>）のみ追加する。
    /// null なら系統B（新規 person 新設）として扱う。
    /// </summary>
    public int? AttachToExistingPersonId { get; init; }

    /// <summary>系統B: <c>persons.full_name</c>（系統A の場合は無視）。</summary>
    public string? FullName { get; init; }

    /// <summary>系統B: <c>persons.full_name_kana</c>。</summary>
    public string? FullNameKana { get; init; }

    /// <summary>系統B: <c>persons.family_name</c>。英文クレジットや姓・名分離検索に必要。</summary>
    public string? FamilyName { get; init; }

    /// <summary>系統B: <c>persons.given_name</c>。</summary>
    public string? GivenName { get; init; }

    /// <summary>系統B: <c>persons.name_en</c>。</summary>
    public string? PersonNameEn { get; init; }

    /// <summary>系統B: <c>persons.notes</c>。</summary>
    public string? PersonNotes { get; init; }
}
