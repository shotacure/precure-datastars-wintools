namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集 Draft セッションで「保存時に character_aliases へ投入する予定」のキャラ名義 1 件。
/// CHARACTER_VOICE 系エントリの「+ 新規キャラ名義...」または一括入力 Apply で
/// マスタ未ヒットだったキャラ名義を、保存ボタンまで Draft 内に保留するためのバケット。
/// </summary>
public sealed class PendingCharacterAlias
{
    /// <summary>払い出した負数 alias_id。<c>DraftEntry.Entity.CharacterAliasId</c> の置換キー。</summary>
    public required int TempAliasId { get; init; }

    /// <summary>新規 <c>character_aliases.name</c>。必須。</summary>
    public required string AliasName { get; init; }

    /// <summary>新規 <c>character_aliases.name_kana</c>。任意。</summary>
    public string? AliasKana { get; init; }

    /// <summary>新規 <c>character_aliases.name_en</c>。任意。</summary>
    public string? AliasEn { get; init; }

    /// <summary>
    /// 系統A: 既存 <c>characters</c> に新名義として紐付ける場合の character_id。
    /// non-null なら新規 character 行は作らず、character_aliases に対象 character_id で 1 行 INSERT するだけ。
    /// null なら系統B（新規 character 新設）として扱う。
    /// </summary>
    public int? AttachToExistingCharacterId { get; init; }

    /// <summary>系統B: <c>characters.name</c>（系統A の場合は無視）。</summary>
    public string? CharacterName { get; init; }

    /// <summary>系統B: <c>characters.name_kana</c>。</summary>
    public string? CharacterNameKana { get; init; }

    /// <summary>系統B: <c>characters.character_kind</c>。新規 character には必須。</summary>
    public string? CharacterKindCode { get; init; }

    /// <summary>系統B: <c>characters.notes</c>。</summary>
    public string? CharacterNotes { get; init; }
}
