namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_block_entries テーブルに対応するエンティティモデル（PK: entry_id）。
/// ブロック内のエントリ 1 つ = 1 行。<see cref="EntryKind"/> に応じて参照先カラムが決まる。
/// <see cref="EntryKind"/> 別の必須カラム:
/// "PERSON"          → <see cref="PersonAliasId"/> / 
/// "CHARACTER_VOICE" → <see cref="PersonAliasId"/>（声優側）+ <see cref="CharacterAliasId"/> または <see cref="RawCharacterText"/> / 
/// "COMPANY"         → <see cref="CompanyAliasId"/> / 
/// "LOGO"            → <see cref="LogoId"/> / 
/// "TEXT"            → <see cref="RawText"/>（マスタ未登録のフリーテキスト退避口） / 
/// "BLANK"           → 参照列はすべて NULL（複数列のブロックで、画面の並びの途中に何も書かれていない升。後ろのエントリの列をそろえる。表では空の升として描き、集計では数えない）。
/// SONG 種別を物理削除。主題歌の楽曲指定は episode_theme_songs を真実の源泉とし、
/// クレジット側では役職レベルでテンプレ展開時に episode_theme_songs を JOIN する運用に切り替え。
/// 整合性は DB トリガー <c>trg_credit_block_entries_b{ins,up}</c> で担保され、
/// 不正な組み合わせの INSERT/UPDATE は SQLSTATE 45000 で弾かれる。
/// 補助フィールド:
/// <see cref="AffiliationCompanyAliasId"/> / <see cref="AffiliationText"/> ... 人物名義の小カッコ所属（マスタ参照またはフリーテキスト） / 
/// <see cref="ParallelWithEntryId"/> ... 「A / B」併記の相手 entry_id への自参照。
/// </summary>
public sealed class CreditBlockEntry
{
    /// <summary>エントリの主キー（AUTO_INCREMENT）。</summary>
    public int EntryId { get; set; }

    /// <summary>所属するブロック ID（→ credit_role_blocks.block_id）。</summary>
    public int BlockId { get; set; }

    /// <summary>本放送限定フラグ。 false (0) = 円盤・配信用エントリ（本放送では同位置に true 行があればそちらが優先）。 true (1) = 本放送用エントリ（円盤・配信では無視される）。 同 (block_id, entry_seq) 位置に false / true を 2 行並立させて、 本放送と円盤でロゴ画像が違う等の差し替えを表現する。</summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>ブロック内の表示順（1 始まり）。</summary>
    public ushort EntrySeq { get; set; }

    /// <summary>エントリ種別（"PERSON"/"CHARACTER_VOICE"/"COMPANY"/"LOGO"/"TEXT"/"BLANK"）。SONG を撤廃。</summary>
    public string EntryKind { get; set; } = "PERSON";

    /// <summary>人物名義 ID（→ person_aliases.alias_id）。EntryKind が PERSON / CHARACTER_VOICE の場合に必須。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>キャラクター名義 ID（→ character_aliases.alias_id）。EntryKind が CHARACTER_VOICE のときのみ。</summary>
    public int? CharacterAliasId { get; set; }

    /// <summary>マスタ未登録キャラのフリーテキスト名義（モブ等）。CHARACTER_VOICE で character_alias の代わりに使用可。</summary>
    public string? RawCharacterText { get; set; }

    /// <summary>
    /// クレジット時の誤記（人物側）。PERSON / CHARACTER_VOICE で使う。
    /// 「クレジット時の事故」を「名義」とは別管理するためのフリーテキスト。
    /// NULL = 誤記なし。値があれば誤記あり（フラグは設けない）。マスタ（person_aliases）を汚さない。
    /// 表示は「打ち消し線で誤記」+ 半角SP + 「正名義」を並べる。
    /// </summary>
    public string? PersonMisprintText { get; set; }

    /// <summary>
    /// 画面に出た人物名の表記揺れ（DB 列名 person_display_text）。PERSON / CHARACTER_VOICE で使う。
    /// 名義（<see cref="PersonAliasId"/>）とは空白・中黒・記号の有無や位置だけが違う画面の表記を、名義を増やさずに持つ。
    /// 誤記ではないので印を付けず、表示は名義の名前の代わりにこの字を出す（リンク先は名義の人物のまま）。NULL は名義の名前のまま。
    /// </summary>
    public string? PersonDisplayText { get; set; }

    /// <summary>
    /// クレジット時の誤記（キャラクター側）。CHARACTER_VOICE のときのみ意味を持つ。
    /// 用法は <see cref="PersonMisprintText"/> と同じ（NULL = 誤記なし、マスタを汚さない補助情報）。
    /// </summary>
    public string? CharacterMisprintText { get; set; }

    /// <summary>
    /// 画面の組みどおりの役名（DB 列名 character_display_text）。CHARACTER_VOICE のときのみ意味を持つ。
    /// 画面で 1 つの役名が 2 行以上に折れて出るとき、その役名を改行（LF）ごと持ち、表示は改行の位置で折って描く。
    /// 画面の役名が名義の頭を省いた後ろの部分だけのとき（双子の姓を省いた「みつき」＝名義「菓彩みつき」など）も、画面の字を持つ。
    /// キャラは 1 つのまま。使える字は <see cref="IsValidCharacterDisplayText"/>。NULL は名義の名前のまま描く。
    /// </summary>
    public string? CharacterDisplayText { get; set; }

    /// <summary>
    /// <paramref name="displayText"/> が名義 <paramref name="characterName"/> の画面の役名として使えるか。
    /// 改行を抜いた字が、改行を含んで名義と同じ（役名が画面で折れている）か、名義の後ろの部分で名義より短い（頭の姓などを画面で省いている）とき true。
    /// </summary>
    public static bool IsValidCharacterDisplayText(string displayText, string characterName)
    {
        string flat = displayText.Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (flat.Length == 0) return false;
        if (flat == characterName) return displayText.Contains('\n');
        return flat.Length < characterName.Length && characterName.EndsWith(flat, StringComparison.Ordinal);
    }

    /// <summary>
    /// クレジット時の誤記（企業屋号側）。COMPANY / LOGO で使う。
    /// 用法は <see cref="PersonMisprintText"/> と同じ。
    /// </summary>
    public string? CompanyMisprintText { get; set; }

    /// <summary>
    /// 画面に出た企業名の表記揺れ（DB 列名 company_display_text）。COMPANY で使う。
    /// 名義（<see cref="CompanyAliasId"/>）とは空白・中黒・記号の有無や位置だけが違う画面の表記を、名義を増やさずに持つ
    /// （名義「BUG FILMS」に対する画面の「BUGFILMS」など）。
    /// 誤記ではないので印を付けず、表示は名義の名前の代わりにこの字を出す（リンク先は名義の企業のまま）。NULL は名義の名前のまま。
    /// 用法は <see cref="PersonDisplayText"/> と同じ。
    /// </summary>
    public string? CompanyDisplayText { get; set; }

    /// <summary>
    /// 人物名の代わりに画面に出た伏せ字表記（例：声優を伏せた「謎の少女　？」の「？」）。PERSON / CHARACTER_VOICE で使う。
    /// 名義（<see cref="PersonAliasId"/>）は本来の人物に紐付けたまま持ち、集計も通常どおり数える。
    /// NULL = 伏せ字なし。値があれば表示は「伏せ字 (正名義)」の形にする。
    /// </summary>
    public string? PersonMaskedText { get; set; }

    /// <summary>企業名義 ID（→ company_aliases.alias_id）。EntryKind が COMPANY のときのみ。</summary>
    public int? CompanyAliasId { get; set; }

    /// <summary>ロゴ ID（→ logos.logo_id）。EntryKind が LOGO のときのみ。</summary>
    public int? LogoId { get; set; }

    /// <summary>フリーテキスト本文。EntryKind が TEXT のときのみ。</summary>
    public string? RawText { get; set; }

    /// <summary>補助所属（企業名義参照、人物の小カッコ所属用、任意）。</summary>
    public int? AffiliationCompanyAliasId { get; set; }

    /// <summary>補助所属（人物名義参照。ユニット等の所属を構造的に指す、任意）。
    /// <see cref="AffiliationCompanyAliasId"/> と排他（DB トリガで担保）。表示はテキスト同様（リンクなし）だが
    /// マスタを指すことで正規化・逆引きを可能にする。例：「清水 佐紀(Berryz工房)」の Berryz工房 をユニット名義に紐付ける。</summary>
    public int? AffiliationPersonAliasId { get; set; }

    /// <summary>補助所属がロゴで出ているときのロゴの版（→ logos.logo_id、任意）。
    /// TV の OP・ED の「（ABC）」「（ADK）」のように所属が各社のロゴで出ている画面で、どの版のロゴかを持つ。
    /// 所属の会社は <see cref="AffiliationCompanyAliasId"/> で持ち、表示も会社名のまま（ロゴの版は表示に使わない）。</summary>
    public int? AffiliationLogoId { get; set; }

    /// <summary>補助所属（フリーテキスト、任意）。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>所属表記のインライン (true=「名前 (所属)」) / 別行 (false=「名前 / (所属)」) レイアウトフラグ。
    /// 入力時の表現を round-trip 保持するための表示ヒント。
    /// パース時はインライン記法なら true、別行 <c>(所属)</c> 単独行を直前エントリに吸収するときは false になる。
    /// 既定値は true（インライン）。所属を持たない（両カラム NULL）エントリでは意味を持たない。</summary>
    public bool AffiliationInline { get; set; } = true;

    /// <summary>「A / B」併記の相手 entry_id への自参照（任意）。
    /// 値のあるエントリは独立した行にならず、相手（同じブロックの前のエントリ）の行に並べて描く。</summary>
    public int? ParallelWithEntryId { get; set; }

    /// <summary>併記のエントリの名前の前に付く字（「ダークマター／クマタ」の「／」、「野乃はな（キュアエール）」の「（」など）。
    /// NULL は何も付けない。<see cref="ParallelWithEntryId"/> のあるエントリでだけ意味を持つ。</summary>
    public string? ParallelPrefix { get; set; }

    /// <summary>併記のエントリの名前の後ろに付く字（「（キュアエール）」の「）」など）。NULL は何も付けない。</summary>
    public string? ParallelSuffix { get; set; }

    /// <summary>true なら併記のエントリ（前後の字を含む）を小さい字で描く。</summary>
    public bool ParallelSmall { get; set; }

    /// <summary>声の出演の役名の後ろに小さいカッコで出す、キャラのユニットの名義（person_aliases.alias_id。DB 列名 character_unit_alias_id）。
    /// 「ヤギ（サファリア）」の「サファリア」。ユニットは persons 行を持たない名義で、person_alias_members にキャラと声優の組をメンバーとして持つ。
    /// NULL は何も出さない。CHARACTER_VOICE のときのみ意味を持つ。</summary>
    public int? CharacterUnitAliasId { get; set; }

    /// <summary>
    /// 同じ役名が続く行の、役名の出し方（DB 列名 same_character_layout。<see cref="CreditEntrySameLayouts"/> の値）。CHARACTER_VOICE のときのみ意味を持つ。
    /// 値のある行は、同じブロックで直前の CHARACTER_VOICE の行（声の出演の表で直前の行）と同じ役名の続き（ひと続きの 2 行目以降）であることを示す。
    /// ひと続きの先頭の行は NULL。出し方はひと続きの 2 行目の値で決まる：
    /// REPEAT = 毎行書く / TOP = 先頭の行に 1 回だけ書き、続きの行は空ける / MIDDLE = ひと続きの行の真ん中に 1 回だけ書く。
    /// NULL は「直前の行の続きではない」で、ふつうに描く。
    /// </summary>
    public string? SameCharacterLayout { get; set; }

    /// <summary>
    /// 同じ声優が続く行の、声優名の出し方（DB 列名 same_person_layout。<see cref="CreditEntrySameLayouts"/> の値）。CHARACTER_VOICE のときのみ意味を持つ。
    /// 用法は <see cref="SameCharacterLayout"/> と同じで、声優の側を受け持つ。
    /// </summary>
    public string? SamePersonLayout { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// credit_block_entries.same_character_layout / same_person_layout の値（同じ役名・同じ声優が続く行の名前の出し方）。
/// </summary>
public static class CreditEntrySameLayouts
{
    /// <summary>ひと続きの行の毎行に名前を書く。</summary>
    public const string Repeat = "REPEAT";
    /// <summary>ひと続きの先頭の行に 1 回だけ名前を書き、続きの行は空ける。</summary>
    public const string Top = "TOP";
    /// <summary>ひと続きの行の真ん中に 1 回だけ名前を書く（升を縦に結合して上下の中央にそろえる）。</summary>
    public const string Middle = "MIDDLE";

    /// <summary>許される値か（大文字小文字を区別する。NULL は別に扱う）。</summary>
    public static bool IsValid(string? value)
        => value is Repeat or Top or Middle;
}
