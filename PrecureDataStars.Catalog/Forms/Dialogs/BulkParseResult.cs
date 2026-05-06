namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット一括入力（<see cref="CreditBulkInputDialog"/>）のパース結果モデル（v1.2.1 追加）。
/// <para>
/// プレーンテキストとして貼り付けられたクレジット列を <see cref="CreditBulkInputParser"/> が
/// 構文解析した結果。最終的に <see cref="CreditBulkApplyService"/> が本オブジェクトを
/// Draft レイヤ（<see cref="Drafting.DraftCredit"/>）に流し込む。
/// </para>
/// <para>
/// 階層構造はテキスト上の区切り記号で決まる:
/// <list type="bullet">
///   <item><description><c>---</c> 単独行 → カード区切り（<see cref="ParsedCard"/>）</description></item>
///   <item><description><c>--</c> 単独行 → ティア区切り（<see cref="ParsedTier"/>、最大 tier_no=2）</description></item>
///   <item><description><c>-</c> 単独行 → グループ区切り（<see cref="ParsedGroup"/>）</description></item>
///   <item><description><c>XXX:</c> または <c>XXX：</c> 行末コロン → 役職開始（<see cref="ParsedRole"/>）</description></item>
///   <item><description>空行 → 同一役職内のブロック区切り（<see cref="ParsedBlock"/>）</description></item>
///   <item><description>タブ区切り行 → 1 ブロック内のエントリ群（<see cref="ParsedEntryRow"/>）。
///     タブ最大数 + 1 が <c>col_count</c> になる</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class BulkParseResult
{
    /// <summary>カード群（順序保持）。テキスト先頭にいきなり役職が来た場合は暗黙の Card 1 が用意される。</summary>
    public List<ParsedCard> Cards { get; } = new();

    /// <summary>パース中に検出された警告メッセージ群。</summary>
    public List<ParseWarning> Warnings { get; } = new();

    /// <summary>
    /// 「適用ボタン無効化」レベルの警告が 1 件以上あるか。
    /// true のときダイアログ側は適用ボタンを Disabled にしてユーザーに修正を促す。
    /// </summary>
    public bool HasBlockingWarnings => Warnings.Any(w => w.Severity == WarningSeverity.Block);

    /// <summary>パース対象になった有効な行が無いか。</summary>
    public bool IsEmpty => Cards.Count == 0 || Cards.All(c => c.Tiers.All(t => t.Groups.All(g => g.Roles.Count == 0)));
}

/// <summary>
/// パース結果における 1 カード分の塊（v1.2.1 追加）。
/// テキスト中の <c>---</c> 単独行で区切られる。
/// </summary>
public sealed class ParsedCard
{
    /// <summary>このカード配下の Tier 群（順序保持）。先頭は暗黙の TierNo=1。</summary>
    public List<ParsedTier> Tiers { get; } = new();
}

/// <summary>
/// パース結果における 1 Tier 分の塊（v1.2.1 追加）。
/// テキスト中の <c>--</c> 単独行で区切られる。tier_no は 1 と 2 のみが有効。
/// </summary>
public sealed class ParsedTier
{
    /// <summary>この Tier 配下のグループ群（順序保持）。先頭は暗黙の GroupNo=1。</summary>
    public List<ParsedGroup> Groups { get; } = new();
}

/// <summary>
/// パース結果における 1 Group 分の塊（v1.2.1 追加）。
/// テキスト中の <c>-</c> 単独行で区切られる。
/// </summary>
public sealed class ParsedGroup
{
    /// <summary>この Group 配下の役職群（順序保持）。</summary>
    public List<ParsedRole> Roles { get; } = new();
}

/// <summary>
/// パース結果における 1 役職分の塊（v1.2.1 追加）。
/// <para>
/// テキスト中の「行末コロン」行（例: <c>"脚本:"</c> や <c>"声の出演："</c>）で開始し、
/// 次の役職または区切り行が現れるまで配下のエントリを集める。
/// </para>
/// </summary>
public sealed class ParsedRole
{
    /// <summary>役職表示名（テキスト上の見出し。例: "脚本", "演出", "声の出演"）。コロンは含まない。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// 適用フェーズで解決された role_code（roles テーブルの主キー）。
    /// パース直後は null。<see cref="CreditBulkApplyService"/> が name 一致 / 部分一致で引き当てる。
    /// </summary>
    public string? ResolvedRoleCode { get; set; }

    /// <summary>
    /// 適用フェーズで解決された <c>role_format_kind</c>（"NORMAL"/"VOICE_CAST"/etc.）。
    /// VOICE_CAST のときだけ <c>&lt;キャラ&gt;声優</c> 構文を許す等、パーサ側でも参照する場合がある。
    /// パース直後は null（解決前は VOICE_CAST 構文も無条件に許す＝後段で警告）。
    /// </summary>
    public string? ResolvedFormatKind { get; set; }

    /// <summary>役職配下のブロック群（空行で区切られる）。</summary>
    public List<ParsedBlock> Blocks { get; } = new();

    /// <summary>役職開始行のテキスト行番号（1 始まり、警告で行番号を出す用）。</summary>
    public int LineNumber { get; set; }
}

/// <summary>
/// パース結果における 1 ブロック分の塊（v1.2.1 追加）。
/// 同一役職内で空行を跨ぐと新しいブロックが生まれる。
/// </summary>
public sealed class ParsedBlock
{
    /// <summary>
    /// 先頭の <c>[XXX]</c> 行があった場合に企業屋号として保持する生テキスト。
    /// 設定されていれば適用時に <c>credit_role_blocks.leading_company_alias_id</c> に解決する。
    /// </summary>
    public string? LeadingCompanyText { get; set; }

    /// <summary>ブロック内の行群（タブ区切り展開済み）。</summary>
    public List<ParsedEntryRow> Rows { get; } = new();

    /// <summary>
    /// ブロック内の各行のタブ数の最大値 + 1 = 表示カラム数の意図。
    /// 既定 1（縦並び）。タブが含まれる行があれば 2 以上になる。
    /// </summary>
    public int ColCount { get; set; } = 1;
}

/// <summary>
/// パース結果における 1 行分のエントリ群（v1.2.1 追加）。タブ区切りで複数エントリを持つ。
/// </summary>
public sealed class ParsedEntryRow
{
    /// <summary>同一行内のエントリ（タブで区切られた左から右の順）。</summary>
    public List<ParsedEntry> Entries { get; } = new();
}

/// <summary>
/// パース結果における 1 エントリ（v1.2.1 追加）。
/// </summary>
public sealed class ParsedEntry
{
    /// <summary>エントリ種別の素案（テキストからの推定値）。適用時に最終決定する。</summary>
    public ParsedEntryKind Kind { get; set; } = ParsedEntryKind.Person;

    /// <summary>人物名（または声優名）の生テキスト。マスタ未引きの状態で保持。</summary>
    public string? PersonRawText { get; set; }

    /// <summary>キャラクター名の生テキスト（VOICE_CAST 行のみ）。</summary>
    public string? CharacterRawText { get; set; }

    /// <summary>
    /// VOICE_CAST 構文 <c>&lt;*キャラ&gt;声優</c> のように、キャラ部分にアスタリスクが付いていたか。
    /// true の場合、たとえ同名既存キャラが居ても引き当てを行わず必ず新規作成する（モブ用途）。
    /// </summary>
    public bool IsForcedNewCharacter { get; set; }

    /// <summary>企業屋号の生テキスト（COMPANY 行）。</summary>
    public string? CompanyRawText { get; set; }

    /// <summary>所属表記の生テキスト（人物名末尾の小カッコ内など。例: "(東映アニメーション)"）。</summary>
    public string? AffiliationRawText { get; set; }

    /// <summary>
    /// マスタに引き当てできない場合に <c>credit_block_entries.raw_text</c> に退避するテキスト。
    /// パース時には使われない（適用時に必要に応じて埋められる）。
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>テキスト上の行番号（1 始まり、警告で行番号を出す用）。</summary>
    public int LineNumber { get; set; }
}

/// <summary>
/// パースエントリの種別（v1.2.1 追加）。<see cref="Data.Models.CreditBlockEntry.EntryKind"/> と概ね対応するが、
/// パース結果は「素案」で、適用時にマスタ引き当て結果に応じて TEXT に降格することがある。
/// </summary>
public enum ParsedEntryKind
{
    /// <summary>人物名（PERSON）。</summary>
    Person,

    /// <summary>キャラクター × 声優のペア（CHARACTER_VOICE）。VOICE_CAST 役職内のみ。</summary>
    CharacterVoice,

    /// <summary>企業屋号（COMPANY）。<c>[XXX]</c> 形式または COMPANY_ONLY 役職内など。</summary>
    Company,

    /// <summary>ロゴ（LOGO）。一括入力フォーマットでは現状直接指定不能、将来拡張用。</summary>
    Logo,

    /// <summary>マスタ未登録時の退避テキスト（TEXT）。</summary>
    Text
}

/// <summary>
/// パース時に検出された警告 1 件（v1.2.1 追加）。
/// </summary>
public sealed class ParseWarning
{
    /// <summary>警告レベル。<see cref="WarningSeverity.Block"/> なら適用ボタンを無効化する。</summary>
    public WarningSeverity Severity { get; init; }

    /// <summary>関連するテキスト行番号（1 始まり、無関係なら 0）。</summary>
    public int LineNumber { get; init; }

    /// <summary>ユーザー向けの警告メッセージ。</summary>
    public string Message { get; init; } = "";
}

/// <summary>警告の重大度（v1.2.1 追加）。</summary>
public enum WarningSeverity
{
    /// <summary>情報レベル（適用は可能）。</summary>
    Info,

    /// <summary>警告レベル（適用は可能だがユーザー注意）。</summary>
    Warning,

    /// <summary>適用ブロックレベル。1 件でもあると「適用」ボタンを Disabled にする。</summary>
    Block
}
