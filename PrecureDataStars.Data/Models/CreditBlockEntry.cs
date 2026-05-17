namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_block_entries テーブルに対応するエンティティモデル（PK: entry_id）。
/// <para>
/// ブロック内のエントリ 1 つ = 1 行。<see cref="EntryKind"/> に応じて参照先カラムが決まる。
/// </para>
/// <para>
/// <see cref="EntryKind"/> 別の必須カラム:
/// "PERSON"          → <see cref="PersonAliasId"/> / 
/// "CHARACTER_VOICE" → <see cref="PersonAliasId"/>（声優側）+ <see cref="CharacterAliasId"/> または <see cref="RawCharacterText"/> / 
/// "COMPANY"         → <see cref="CompanyAliasId"/> / 
/// "LOGO"            → <see cref="LogoId"/> / 
/// "TEXT"            → <see cref="RawText"/>（マスタ未登録のフリーテキスト退避口）。
/// </para>
/// <para>
/// SONG 種別を物理削除。主題歌の楽曲指定は episode_theme_songs を真実の源泉とし、
/// クレジット側では役職レベルでテンプレ展開時に episode_theme_songs を JOIN する運用に切り替え。
/// </para>
/// <para>
/// 整合性は DB トリガー <c>trg_credit_block_entries_b{ins,up}</c> で担保され、
/// 不正な組み合わせの INSERT/UPDATE は SQLSTATE 45000 で弾かれる。
/// </para>
/// <para>
/// 補助フィールド:
/// <see cref="AffiliationCompanyAliasId"/> / <see cref="AffiliationText"/> ... 人物名義の小カッコ所属（マスタ参照またはフリーテキスト） / 
/// <see cref="ParallelWithEntryId"/> ... 「A / B」併記の相手 entry_id への自参照。
/// </para>
/// </summary>
public sealed class CreditBlockEntry
{
    /// <summary>エントリの主キー（AUTO_INCREMENT）。</summary>
    public int EntryId { get; set; }

    /// <summary>所属するブロック ID（→ credit_role_blocks.block_id）。</summary>
    public int BlockId { get; set; }

    /// <summary>
    /// 本放送限定フラグ。
    /// false (0) = 円盤・配信用エントリ（本放送では同位置に true 行があればそちらが優先）。
    /// true (1) = 本放送用エントリ（円盤・配信では無視される）。
    /// 同 (block_id, entry_seq) 位置に false / true を 2 行並立させて、
    /// 本放送と円盤でロゴ画像が違う等の差し替えを表現する。
    /// </summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>ブロック内の表示順（1 始まり）。</summary>
    public ushort EntrySeq { get; set; }

    /// <summary>エントリ種別（"PERSON"/"CHARACTER_VOICE"/"COMPANY"/"LOGO"/"TEXT"）。SONG を撤廃。</summary>
    public string EntryKind { get; set; } = "PERSON";

    /// <summary>人物名義 ID（→ person_aliases.alias_id）。EntryKind が PERSON / CHARACTER_VOICE の場合に必須。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>キャラクター名義 ID（→ character_aliases.alias_id）。EntryKind が CHARACTER_VOICE のときのみ。</summary>
    public int? CharacterAliasId { get; set; }

    /// <summary>マスタ未登録キャラのフリーテキスト名義（モブ等）。CHARACTER_VOICE で character_alias の代わりに使用可。</summary>
    public string? RawCharacterText { get; set; }

    /// <summary>企業名義 ID（→ company_aliases.alias_id）。EntryKind が COMPANY のときのみ。</summary>
    public int? CompanyAliasId { get; set; }

    /// <summary>ロゴ ID（→ logos.logo_id）。EntryKind が LOGO のときのみ。</summary>
    public int? LogoId { get; set; }

    /// <summary>フリーテキスト本文。EntryKind が TEXT のときのみ。</summary>
    public string? RawText { get; set; }

    /// <summary>補助所属（企業名義参照、人物の小カッコ所属用、任意）。</summary>
    public int? AffiliationCompanyAliasId { get; set; }

    /// <summary>補助所属（フリーテキスト、任意）。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>「A / B」併記の相手 entry_id への自参照（任意）。</summary>
    public int? ParallelWithEntryId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}