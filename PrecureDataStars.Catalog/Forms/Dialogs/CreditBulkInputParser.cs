using System.Text.RegularExpressions;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット一括入力テキストの構文解析器（完全可逆化のため大幅拡張）。
/// 仕様（テキスト書式）:
/// <list type="bullet">
///   <item><description><c>XXX:</c> または <c>XXX：</c>（行末コロン）→ 役職開始</description></item>
///   <item><description><c>-</c>（半角ハイフン1個・前後トリム後の単独行）→ ブロック区切り</description></item>
///   <item><description><c>--</c> → グループ区切り</description></item>
///   <item><description><c>---</c> → ティア区切り（最大 tier_no=2）</description></item>
///   <item><description><c>----</c> → カード区切り</description></item>
///   <item><description>空行 → 同一役職内のブロック区切り</description></item>
///   <item><description><c>[XXX]</c>（行全体）→ COMPANY エントリ（位置に関係なく常に COMPANY 扱い）</description></item>
///   <item><description><c>[[XXX]]</c>（行全体）→ グループトップ屋号（<c>leading_company_alias_id</c>）。ブロックの最初の有意行でのみ許可、それ以外は Block 警告</description></item>
///   <item><description>タブ区切り行 → エントリ群（タブ最大数+1 = col_count）</description></item>
///   <item><description><c>&lt;キャラ名義&gt;声優名義</c>（VOICE_CAST 役職内）→ CHARACTER_VOICE</description></item>
///   <item><description><c>&lt;*キャラ名義&gt;声優名義</c>（VOICE_CAST 役職内）→ 強制新規キャラ</description></item>
///   <item><description><c>&lt;*X&gt;</c> 直後の声優名のみ行 → 各行を別個の新規 X として処理</description></item>
///   <item><description>通常テキスト → PERSON</description></item>
/// </list>
/// 追加された拡張構文（一括入力フォーマットの完全可逆化のため）:
/// <list type="bullet">
///   <item><description><c>[屋号#CIバージョン]</c>（行全体またはセル）→ LOGO エントリ。
///     最右の <c>#</c> をセパレータとして「左側=屋号テキスト」「右側=CI バージョンラベル」に分解する。
///     <c>#</c> が含まれない <c>[XXX]</c> は従来どおり COMPANY エントリとして扱う。</description></item>
///   <item><description>エントリ行頭の <c>🎬</c>（U+1F3AC、後続スペースは省略可）→ そのエントリを
///     <c>is_broadcast_only=1</c> として登録（本放送限定エントリ）。</description></item>
///   <item><description>エントリ行末の <c> // 備考</c>（半角スペース + スラッシュ 2 個 + スペース + 任意文字列）
///     → そのエントリの <c>notes</c> に保存。<c>//</c> 自身を備考に含めたい場合は未対応（実用稀のため）。</description></item>
///   <item><description>エントリ行頭の <c>&amp; </c>（半角アンパサンド + スペース）プレフィクス
///     → 直前エントリと A/B 併記関係（適用フェーズで <c>parallel_with_entry_id</c> 自参照を解決）。</description></item>
///   <item><description><c>@cols=N</c>（ブロック先頭の単独行）→ そのブロックの <c>col_count</c> を明示指定。
///     省略時は従来どおりタブ数+1 で推測。</description></item>
///   <item><description><c>@notes=備考</c>（各レベル区切り行直後の単独行）→ 直近で開かれた
///     Card / Tier / Group / Role / Block の <c>notes</c> に保存。同一スコープに対する 2 回目の
///     <c>@notes=</c> は次のスコープ（Role 直後なら Block）にスライドする。</description></item>
/// </list>
/// 追加された拡張構文（既存名義の別表記登録を明示する）:
/// <list type="bullet">
///   <item><description><c>旧名義 =&gt; 新名義</c>（人物名 / キャラ名 / 企業屋号 / LOGO の屋号部のいずれでも可）
///     → 適用フェーズで左側「旧名義」から既存マスタ（<c>person_aliases</c> / <c>character_aliases</c> /
///     <c>company_aliases</c>）を引き当てて、同一人物 / 同一キャラ / 同一企業に対する別名義として
///     右側「新名義」を新規登録する。「左 = 旧、右 = 新」と矢印の向きが「名義が変わった事実」と一致するよう揃えた。
///     左右いずれかが空のセパレータ（<c>" => 山田"</c> / <c>"山田 =&gt;"</c>）はリダイレクト無し扱いにフォールバック。
///     旧名義が引き当たらない場合は警告を出した上で、右側のみで通常の新規作成を行う（タイポしたまま気づかない事故防止）。
///     LOGO エントリの場合は屋号部分（<c>#</c> の左側）に対してのみ <c>=&gt;</c> を解釈する。CI バージョン部は対象外。</description></item>
/// </list>
/// 警告は適用ブロックレベル（<see cref="WarningSeverity.Block"/>）と通常警告に分かれる。
/// 適用ブロックの例: 先頭が役職指定でない／ハイフン4個以上／ティア3個目超／<c>&lt;X&gt;</c> 直後にキャラ指定なし行。
/// </summary>
public static class CreditBulkInputParser
{
    // 役職開始行: 行末がコロン（半角 ':' または全角 '：'）。前後の空白は trim 後判定。
    private static readonly Regex RoleHeadRegex = new(@"^(?<name>.+?)[：:]\s*$", RegexOptions.Compiled);

    // 行全体（or セル）が [XXX] / [XXX#aliasid] / [XXX#CIバージョン] / [XXX#aliasid#CIバージョン] のパターン。
    // COMPANY / LOGO エントリの統合構文。判定ルール（aliasid キャプチャ後にロジック側で再分岐）:
    //   - # が 0 個 → COMPANY、alias_id 明示無し
    //   - # が 1 個、右が純数値 (^\d+$) → COMPANY、alias_id 明示
    //   - # が 1 個、右が非純数値 → LOGO、CI バージョン
    //   - # が 2 個、1 個目の右が純数値、2 個目の右が任意 → LOGO、alias_id 明示 + CI バージョン
    //
    // 正規表現上は alias_id を `\d+` 制限で取りに行き、CI バージョンを後置で取る。
    // 1 つ目の # の右側が非純数値（CI バージョン）の場合、aliasid グループはマッチせず ci のみが取れる。
    // 末尾の "×誤記" / "✕誤記" 後置はクレジット時の屋号誤記を記録する任意キャプチャ（misprint グループ）。
    // CI バージョン側に対しては誤記を許可しない（CI バージョン違いは別 logos 行で表現するため）。
    // 単一ブラケット [XXX] と LOGO [XXX#YYY] を同じ正規表現で受け止める統合形に整理（過去は別正規表現で
    // 2 段判定していたが、alias_id 明示記法の追加で分岐が複雑化したため統合）。
    private static readonly Regex BracketEntryRegex = new(
        @"^\[(?<name>[^\[\]#]+)(?:#(?<aliasid>\d+))?(?:#(?<ci>[^#\[\]]+))?\](?:[×✕]\s*(?<misprint>.+))?$",
        RegexOptions.Compiled);

    // 行全体が [[XXX]] のパターン。グループトップ屋号 (leading_company_alias_id) を
    // 明示するための専用構文。ブロック内最初の有意行でのみ許可される。
    // 単一ブラケット [XXX] とは構文上完全に区別されるため、誤読の心配なく両方を併用できる。
    private static readonly Regex LeadingCompanyBracketRegex = new(@"^\[\[(?<name>[^\[\]]+)\]\]$", RegexOptions.Compiled);

    // VOICE_CAST 構文: <キャラ>声優 / <*キャラ>声優 / <キャラ#aliasid>声優 / <*キャラ#aliasid>声優
    // - aster: 先頭の * フラグ（強制新規キャラ、モブ用途）
    // - chara: キャラ名本体。素の '<' '>' '#' は含まないが、名義変更リダイレクト記法 "=>" は
    //   キャラ名内に出現し得るため特例で許容する（`(?:=>|[^<>#])*`）。これがないと
    //   `<旧名 => 新名>声優` の `=>` の `>` が閉じ括弧と誤解釈され、chara が「旧名 =」で
    //   切れて actor 側に「 新名>声優」が紛れ込む。
    // - aliasid: alias_id 明示参照（純数値、エンコーダが「DB に同名 alias が複数存在するとき」に出力する）
    // - actor: 声優名（その後の所属抽出と誤記分解は後段ロジックで処理）
    // chara 部分は空でも警告対象として後段で捕まえる。
    private static readonly Regex VoiceCastRegex = new(
        @"^<(?<aster>\*)?(?<chara>(?:=>|[^<>#])*)(?:#(?<aliasid>\d+))?>(?<actor>.*)$",
        RegexOptions.Compiled);

    // 人物名末尾の所属 "(...)" / "（...）" 抽出。
    // 名前の途中に括弧がある場合（"山田(本名)"等）は厳密性より素朴さを優先し、最右の括弧を採用。
    private static readonly Regex AffiliationRegex = new(@"^(?<name>.+?)\s*[(（](?<aff>[^()（）]+)[)）]\s*$", RegexOptions.Compiled);

    // ディレクティブ行: @notes=備考 形式。値は = の右側全部（trim 済み）。
    // 値が空文字なら notes クリアの意味になる。
    private static readonly Regex NotesDirectiveRegex = new(@"^@notes=(?<value>.*)$", RegexOptions.Compiled);

    // ディレクティブ行: @cols=N 形式。N は 1 以上の整数。
    private static readonly Regex ColsDirectiveRegex = new(@"^@cols=(?<n>\d+)$", RegexOptions.Compiled);

    // 行頭の本放送限定マーカー: 🎬（U+1F3AC、サロゲートペアで2文字分）+ 任意の半角スペース。
    // C# の文字列リテラルではサロゲートペアをそのまま埋め込めるが、明示性のためエスケープ表記も併記する。
    // U+1F3AC = High surrogate D83C + Low surrogate DFAC
    private const string BroadcastOnlyMarker = "\uD83C\uDFAC";

    // 行頭の A/B 併記マーカー: 半角アンパサンド + 半角スペース。
    // スペース必須としているのは、人物名が偶然 "&" で始まる稀なケース（"AB&CD"等の単語混合）と
    // 衝突しないようにするため。"&山田" は通常の人物名扱い。
    private const string ParallelContinuationMarker = "& ";

    // 行末の備考コメント区切り: 半角スペース + スラッシュ 2 個 + 半角スペース。
    // 例: "山田 太郎 // 旧名義あり" → 名前部分 "山田 太郎" + 備考 "旧名義あり"。
    // 区切り自体に半角スペースを前後に含めることで、URL 等の "https://" との誤マッチを避ける。
    private const string EntryNotesSeparator = " // ";

    /// <summary>
    /// 「旧名義 =&gt; 新名義」記法のセパレータ。
    /// 名義リダイレクトを明示するための演算子で、人物名 / キャラクター名 / 企業屋号 / LOGO の屋号部の
    /// いずれでも統一して使える。最後の <c>=&gt;</c> で分割し、左を旧表記（既存マスタ参照キー）、
    /// 右を新表記（このエントリで実際に表示する別名義）として扱う。
    /// 矢印の向きは「旧 → 新」の遷移方向に揃えており、ユーザーが「名義が変わった」事実を直感的に書ける。
    /// </summary>
    private const string OldNewRedirectSeparator = "=>";

    /// <summary>
    /// 「名義 × 誤記」記法のセパレータ候補。クレジット時の誤記（事故）をエントリ単位で記録する。
    /// 左側 = 正名義（マスタ参照キー）、右側 = 誤記表記（フリーテキスト、マスタを汚さない）。
    /// 受付文字は U+00D7（×: MULTIPLICATION SIGN）と U+2715（✕: MULTIPLICATION X）の 2 種類。
    /// どちらが入力されても同じ扱い（出力は U+00D7 に統一）。
    /// </summary>
    private static readonly char[] MisprintSeparators = { '×', '✕' };

    /// <summary>パース中の「次に @notes= が来たらどのスコープに割り当てるか」を表す状態。</summary>
    private enum NotesTarget
    {
        /// <summary>受け入れ先なし（@notes= が来ても警告対象）。</summary>
        None,
        /// <summary>直近のカード（<c>----</c> 区切り直後）。</summary>
        Card,
        /// <summary>直近のティア（<c>---</c> 区切り直後）。</summary>
        Tier,
        /// <summary>直近のグループ（<c>--</c> 区切り直後）。</summary>
        Group,
        /// <summary>直近のロール（<c>XXX:</c> 直後）。@notes= 消費後は <see cref="Block"/> に遷移する。</summary>
        Role,
        /// <summary>直近のブロック（<c>-</c> または空行直後、または Role 後の二回目）。</summary>
        Block,
    }

    /// <summary>入力テキストを構文解析して <see cref="BulkParseResult"/> を返す。 入力 null/空文字は空の結果を返す（警告 0）。</summary>
    public static BulkParseResult Parse(string? input)
    {
        var result = new BulkParseResult();
        if (string.IsNullOrEmpty(input)) return result;

        // 改行コード混在に強くするため \r\n / \r / \n 全部を分割キーにする。
        var lines = input.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // ─── 状態機械 ───
        // 暗黙の Card 1 / Tier 1 / Group 1 を最初の役職検出時に作る。
        ParsedCard? curCard = null;
        ParsedTier? curTier = null;
        ParsedGroup? curGroup = null;
        ParsedRole? curRole = null;
        ParsedBlock? curBlock = null;

        // 「直前の <*X> 構文を覚えておいて、次行以降の声優名のみ行に流用する」用の状態。
        // 直前行が <*chara>actor 形式だったときのみ chara を保持する。<chara> アスタなしの場合は流用しない仕様。
        string? carryOverForcedCharacter = null;

        // 「直近で <X>（アスタなし）が来た」フラグ。直後にキャラ指定なし行が来たら警告対象。
        bool lastEntryWasNonAsterCharacterVoice = false;

        // 直前のロールの表示名を保持する。
        // -/-- /--- でロールが閉じられた直後にエントリ行が来た場合、明示的な役職指定が無くても
        // 「直前と同じ役職名で新カード/新ティア/新グループ配下に同名ロールを暗黙再作成」する用途。
        // たとえば「声の出演:」が長く続く場合に、ユーザーがカード区切りごとに「声の出演:」を
        // 書き直す手間を省くための仕様。区切りの種別（カード/ティア/グループ）に関わらず動作する。
        string? lastRoleDisplayName = null;

        // テキスト先頭が役職指定でない場合の警告は最初の有意行で 1 回だけ出す。
        bool firstMeaningfulLineSeen = false;

        // @notes= ディレクティブの割り当て先スコープ。
        NotesTarget pendingNotesTarget = NotesTarget.None;

        // @cols=N ディレクティブを受け付ける状態か。
        // 役職開始 / ブロック区切り（'-' or 空行）直後の「ブロックセットアップフェーズ」中のみ true。
        // エントリ / leading_company / 既存ブロックへの追加 が起きた時点で false に戻す。
        bool pendingColsForBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string trimmed = raw.Trim();
            int lineNo = i + 1;

            // ─── 空行 ───
            //   役職内なら次のエントリで新ブロックを切るマーカー。役職外なら無視。
            if (trimmed.Length == 0)
            {
                if (curRole is not null && curBlock is not null && curBlock.Rows.Count > 0)
                {
                    // 次の有意行で新ブロックが必要、というシグナル。curBlock を切り離して null に戻す。
                    curBlock = null;
                    // 新しいブロックの直前なので、次の @notes= はそのブロックを対象とする。
                    // @cols= も同様にブロック開始直後の専用ディレクティブとして受け付け可能にする。
                    pendingNotesTarget = NotesTarget.Block;
                    pendingColsForBlock = true;
                }
                // 連続空行は何もしない。
                continue;
            }

            // ─── ハイフン区切り検出───
            // 「行全体がハイフンのみ」かつ前後トリム後の判定。
            //
            // 仕様:
            //   -    → ブロック区切り（ロールは閉じない、同ロール内で次のブロック開始）
            //   --   → グループ区切り（ロールを閉じる、新グループ開始）
            //   ---  → ティア区切り（ロールを閉じる、新ティア＋ Group 1 開始）
            //   ---- → カード区切り（ロールを閉じる、新カード＋ Tier 1 + Group 1 開始）
            //   -----以上 → Block 警告
            //
            // 「-」が一番頻出するブロック区切りに割り当てられたことで、ユーザーがクレジット 1 件の
            // 中で複数ブロックを書き分けるとき空行を入れる代わりにハイフン 1 個でも区切れるよう
            // 利便性が上がる。
            if (IsAllHyphens(trimmed))
            {
                int hyphens = trimmed.Length;

                if (hyphens >= 5)
                {
                    result.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Block,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: ハイフンは最大 4 個まで（カード区切り）。{hyphens} 個は不正です。"
                    });
                    continue;
                }

                EnsureScaffold(ref curCard, ref curTier, ref curGroup, result);

                if (hyphens == 1)
                {
                    // ブロック区切り: ロールを閉じず、curBlock だけリセット。
                    curBlock = null;
                    carryOverForcedCharacter = null;
                    lastEntryWasNonAsterCharacterVoice = false;
                    // 注意: 「-」では同名ロール自動継承の lastRoleDisplayName 更新は行わない。
                    // ロールが閉じないので「区切り後にエントリ行 → 暗黙ロール再作成」という流れが
                    // そもそも発生しない。
                    // 新しいブロックの直前なので、次の @notes= / @cols= はそのブロックを対象とする。
                    pendingNotesTarget = NotesTarget.Block;
                    pendingColsForBlock = true;
                    continue;
                }

                // 「--」以降はロールを閉じる区切り。同名ロール自動継承用に直前ロール名を保存。
                if (curRole is not null)
                {
                    lastRoleDisplayName = curRole.DisplayName;
                }

                if (hyphens == 4)
                {
                    // カード区切り: 新カードを作って Tier 1 / Group 1 を先行投入し、役職は閉じる。
                    curCard = new ParsedCard();
                    result.Cards.Add(curCard);
                    curTier = new ParsedTier();
                    curCard.Tiers.Add(curTier);
                    curGroup = new ParsedGroup();
                    curTier.Groups.Add(curGroup);
                    curRole = null;
                    curBlock = null;
                    // 直後の @notes= は新カード（最も外側）を対象とする。
                    // ブロックセットアップはまだ開始していない（役職もブロックも無いため）。
                    pendingNotesTarget = NotesTarget.Card;
                    pendingColsForBlock = false;
                }
                else if (hyphens == 3)
                {
                    // ティア区切り: 同カード内で次の Tier を作る（最大 2 まで）。
                    if (curCard!.Tiers.Count >= 2)
                    {
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Block,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: ティアは最大 2 つまで。3 つ目以降は使えません（カードを分けてください）。"
                        });
                        continue;
                    }
                    curTier = new ParsedTier();
                    curCard.Tiers.Add(curTier);
                    curGroup = new ParsedGroup();
                    curTier.Groups.Add(curGroup);
                    curRole = null;
                    curBlock = null;
                    // 直後の @notes= は新ティアを対象とする。
                    pendingNotesTarget = NotesTarget.Tier;
                    pendingColsForBlock = false;
                }
                else // hyphens == 2
                {
                    // グループ区切り: 同 Tier 内で新グループを作る。
                    curGroup = new ParsedGroup();
                    curTier!.Groups.Add(curGroup);
                    curRole = null;
                    curBlock = null;
                    // 直後の @notes= は新グループを対象とする。
                    pendingNotesTarget = NotesTarget.Group;
                    pendingColsForBlock = false;
                }

                carryOverForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = false;
                continue;
            }

            // ─── ディレクティブ行の検出 ───
            // @notes= / @cols= は @ プレフィクスで始まる単独行。エントリ行と区別するため、
            // ロール開始や区切り行と並ぶ「制御行」の一種として最優先で処理する。
            //   - @notes= : 現在の pendingNotesTarget が指すスコープに備考を割り当てる
            //   - @cols=  : 現在のブロックセットアップ中なら ColCount を明示指定する
            // どちらも消費後に状態を更新し、エントリ行とは扱わない。
            //
            // 配置位置の妥当性チェック:
            //   - @notes= は pendingNotesTarget == None のときに来た場合、Block 警告を出す
            //     （直前にスコープを開いていない、または既に消費済みのため）
            //   - @cols=  は pendingColsForBlock == false のとき Block 警告を出す
            if (trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                var notesMatch = NotesDirectiveRegex.Match(trimmed);
                if (notesMatch.Success)
                {
                    // 値部分は = の右側全部（前後の空白は trim 済みの行から取るので末尾空白のみ trim）
                    string value = notesMatch.Groups["value"].Value.TrimEnd();

                    // タブや改行を含む値は仕様外。検出時は警告を出して値からは除去する。
                    // （実装上タブを含む @notes= 行はそもそもタブ位置で配列分割されないが、
                    //  念のため明示的にチェックしておく。）
                    if (value.IndexOf('\t') >= 0)
                    {
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Warning,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: @notes= の値にタブが含まれています。タブは無視されます。"
                        });
                        value = value.Replace("\t", " ");
                    }

                    bool consumed = ApplyNotesDirective(
                        value, lineNo,
                        curCard, curTier, curGroup, curRole, ref curBlock,
                        ref pendingNotesTarget, ref pendingColsForBlock,
                        result);

                    if (consumed)
                    {
                        // 後続のエントリ行で carryOverForcedCharacter / lastWasNonAster を流用しないよう
                        // クリアはしないが、エントリ行の文脈外なのでそのまま continue する。
                        continue;
                    }
                    // consumed=false の場合（pendingNotesTarget==None で警告発出済み）も、
                    // 当該行自体は無視（エントリ行として扱わない）。
                    continue;
                }

                var colsMatch = ColsDirectiveRegex.Match(trimmed);
                if (colsMatch.Success)
                {
                    if (!int.TryParse(colsMatch.Groups["n"].Value, out int colN) || colN < 1)
                    {
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Block,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: @cols= の値は 1 以上の整数で指定してください。"
                        });
                        continue;
                    }

                    if (!pendingColsForBlock)
                    {
                        // ブロックセットアップ中でない（既にエントリが入ったブロック内、または役職外）。
                        // 安全側に倒して警告を出して無視する（既存ブロックの ColCount を後から書き換えると
                        // 解釈が混乱するため）。
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Block,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: @cols= はブロックの先頭（役職指定または区切り直後）でのみ指定できます。"
                        });
                        continue;
                    }

                    // 役職が無いと意味を成さない（pendingColsForBlock の前提条件として役職開始直後 or
                    // ロール内のブロック区切り直後があるため、ここでは curRole は必ず非 null）。
                    if (curRole is null)
                    {
                        // 念のためのガード。論理的には到達しないはず。
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Block,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: @cols= が役職外で指定されました。役職指定後に書いてください。"
                        });
                        continue;
                    }

                    // 既存または新規ブロックに ColCount を明示適用する。
                    if (curBlock is null)
                    {
                        curBlock = new ParsedBlock();
                        curRole.Blocks.Add(curBlock);
                    }
                    curBlock.ColCount = colN;
                    curBlock.ColCountExplicit = true;

                    // @cols= の二重指定を防ぐため、消費後はフラグを下ろす。
                    // ただし @notes= はまだ受付可能（pendingColsForBlock と pendingNotesTarget は独立）。
                    pendingColsForBlock = false;
                    continue;
                }

                // @ で始まるが既知ディレクティブでない → Block 警告。
                result.Warnings.Add(new ParseWarning
                {
                    Severity = WarningSeverity.Block,
                    LineNumber = lineNo,
                    Message = $"{lineNo} 行目: 未知のディレクティブ「{trimmed}」。@notes= / @cols= のみサポートします。"
                });
                continue;
            }

            // ─── 役職開始行（行末コロン）───
            var roleMatch = RoleHeadRegex.Match(trimmed);
            // 「[XXX]:」のような複合は役職側を優先しない（[XXX] の方が固有なので先に判定）。
            // 「[XXX]」単独は別ルールで処理。コロンを含む [XXX]: は誤入力扱いで、役職側にマッチさせる
            // と意図と違う動きになるので、ここでは括弧開始は除外する。
            if (roleMatch.Success && !trimmed.StartsWith("["))
            {
                EnsureScaffold(ref curCard, ref curTier, ref curGroup, result);

                if (!firstMeaningfulLineSeen)
                {
                    // テキスト先頭が役職指定: OK
                    firstMeaningfulLineSeen = true;
                }

                curRole = new ParsedRole
                {
                    DisplayName = roleMatch.Groups["name"].Value.Trim(),
                    LineNumber = lineNo,
                };
                curGroup!.Roles.Add(curRole);
                curBlock = null;

                // 明示的な役職指定があった場合は、自動継承の追跡名を新しい名前で更新する
                // （後続区切り後の自動継承はこの新ロール名を引き継ぐ）。
                lastRoleDisplayName = curRole.DisplayName;

                // 役職開始直後の @notes= は役職を対象とし、
                pendingNotesTarget = NotesTarget.Role;
                pendingColsForBlock = true;

                carryOverForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = false;
                continue;
            }

            // ─── ここに到達したら「エントリ行」相当 ───
            if (curRole is null)
            {
                if (lastRoleDisplayName is not null)
                {
                    // (a) 自動継承: 区切り直後の同名ロール暗黙再作成。
                    // 現在の curGroup（区切りで作り直された新カード/ティア/グループ配下）に
                    // 同じ DisplayName の ParsedRole を新規生成する。
                    // LineNumber は本行（エントリ行）を起点にすることで、警告メッセージ等で
                    // 「このロールがどこから始まったか」がユーザーに辿れるようにする。
                    EnsureScaffold(ref curCard, ref curTier, ref curGroup, result);
                    curRole = new ParsedRole
                    {
                        DisplayName = lastRoleDisplayName,
                        LineNumber = lineNo,
                    };
                    curGroup!.Roles.Add(curRole);
                    curBlock = null;
                    firstMeaningfulLineSeen = true;
                    // 暗黙再作成されたロールに対しても @notes= の受付は有効にする。
                    // ただしブロックセットアップ中に既に入っているはずなので、ここで上書きはしない
                    // （ブロック側の pending が活きている場合があるため）。
                    // 実装上はそのまま下のエントリ処理に流すと entry が来た瞬間に pending がクリアされる。
                    // 以降は通常のエントリ行処理にフォールスルーする（continue しない）。
                }
                else
                {
                    // (b) 先頭から役職指定がなくエントリ行が来た: Block 警告。
                    if (!firstMeaningfulLineSeen)
                    {
                        result.Warnings.Add(new ParseWarning
                        {
                            Severity = WarningSeverity.Block,
                            LineNumber = lineNo,
                            Message = $"{lineNo} 行目: 先頭は役職指定（例: 「脚本:」）から始めてください。"
                        });
                        firstMeaningfulLineSeen = true;
                    }
                    // 役職に紐付けようがないので、行をスキップ（追加警告は出さない）。
                    continue;
                }
            }

            firstMeaningfulLineSeen = true;

            // ─── ブロックの確保 ───
            // 空行で curBlock が null になっている、またはまだ無い場合は新ブロックを作る。
            if (curBlock is null)
            {
                curBlock = new ParsedBlock();
                curRole.Blocks.Add(curBlock);
            }

            // ─── 二重ブラケット [[XXX]] = leading_company（グループトップ屋号） ───
            // ブロックの最初の有意行（curBlock.Rows.Count == 0 かつ LeadingCompanyText 未設定）でのみ許可。
            // それ以外の位置（途中で出現）または重複指定は Block 警告を出してスキップする。
            // 単一ブラケット [XXX] は別ルールで COMPANY エントリとして扱う（後続のエントリ化分岐で処理）。
            var leadingBracketMatch = LeadingCompanyBracketRegex.Match(trimmed);
            if (leadingBracketMatch.Success)
            {
                if (curBlock.Rows.Count == 0 && curBlock.LeadingCompanyText is null)
                {
                    // ブロック先頭の有意行 → leading_company として吸収。
                    curBlock.LeadingCompanyText = leadingBracketMatch.Groups["name"].Value.Trim();

                    // leading_company 行はエントリではないため、ブロックセットアップは継続する。
                    // ただし @cols= はもう受け付けない（エントリ行直前まで来ている認識のため）、
                    // @notes= は引き続きブロック対象として受け付け可能。
                    pendingColsForBlock = false;
                    if (pendingNotesTarget == NotesTarget.Role || pendingNotesTarget == NotesTarget.Block)
                    {
                        // Role 直後で leading_company が来た場合、ロールに @notes= はもう振れないため
                        // ブロック側へ移しておく。Block であれば変更なし。
                        pendingNotesTarget = NotesTarget.Block;
                    }

                    carryOverForcedCharacter = null;
                    lastEntryWasNonAsterCharacterVoice = false;
                    continue;
                }
                else
                {
                    // 途中での [[XXX]] または重複指定 → Block 警告。
                    string reason = curBlock.LeadingCompanyText is not null
                        ? "ブロックに既に [[XXX]] のグループトップ屋号が指定されています"
                        : "[[XXX]] はブロックの最初の有意行でのみ指定できます（このブロックには既にエントリ行があります）";
                    result.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Block,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: {reason}。"
                    });
                    // 警告を出した上で、当該行は無視（適用されない）して処理を続行。
                    carryOverForcedCharacter = null;
                    lastEntryWasNonAsterCharacterVoice = false;
                    continue;
                }
            }

            // ここに到達した時点でエントリ行確定なので、ディレクティブ系の pending を解除する。
            // これ以降に @notes= や @cols= が来たら警告対象（次のスコープ開始までは無効）。
            pendingNotesTarget = NotesTarget.None;
            pendingColsForBlock = false;

            // ─── タブ区切りで複数エントリを取り出す ───
            // タブ最大数 + 1 が ColCount の意図。最大値はブロック内全行で集計する。
            string[] cols = raw.Split('\t');

            // 行頭・行末の空白も trim する（タブ含まず）。
            for (int c = 0; c < cols.Length; c++) cols[c] = cols[c].Trim();

            // 全カラムが空（タブだけの行）→ 何もしない。
            if (cols.All(string.IsNullOrEmpty)) continue;

            int rowColCount = cols.Length;
            // ColCountExplicit が true の場合は @cols= で明示された値を尊重し、
            // タブ数推測値で上書きしない。明示指定が無い従来運用ではタブ最大数で更新する。
            if (!curBlock.ColCountExplicit && rowColCount > curBlock.ColCount)
            {
                curBlock.ColCount = rowColCount;
            }

            var row = new ParsedEntryRow();
            curBlock.Rows.Add(row);

            // 各カラムをエントリ化。
            //   ※ 「<*X>」モードのキャラ流用は同一行内で完結する設計（行を跨いだ流用は次行で別判定）。
            //   ※ 同一行のタブ区切りでは「最初のセル」だけを <*X> 流用判定の材料に使う。
            string? rowLevelForcedCharacter = null;
            bool rowLevelLastWasNonAster = false;

            for (int c = 0; c < cols.Length; c++)
            {
                string cell = cols[c];
                if (string.IsNullOrEmpty(cell)) continue;

                var entry = ParseEntryCell(cell, lineNo, curRole, result,
                    ref carryOverForcedCharacter, ref lastEntryWasNonAsterCharacterVoice,
                    ref rowLevelForcedCharacter, ref rowLevelLastWasNonAster,
                    isFirstCellInRow: c == 0);

                if (entry is not null) row.Entries.Add(entry);
            }
        }

        // 末尾整理: 暗黙 Card / Tier / Group を作っていたが何も無いケースは結果から除く必要は無い
        // （IsEmpty プロパティで判定可能）。

        // 「協力」役職の文脈依存リネーム。
        // 同一カード内に VOICE_CAST 系の役職（DisplayName が「声の出演」「キャスト」等を含む役職）
        // が存在する場合、そのカード内の DisplayName=「協力」のロールは
        // 「キャスティング協力」役職の意味として書かれていると解釈し、DisplayName を書き換える。
        //
        // パーサ自身はマスタを知らないため、ここでは ResolvedRoleCode に直接 "CASTING_COOPERATION"
        // をセットせず、DisplayName を変える形で後段（CreditBulkApplyService.ResolveRolesAsync）に
        // マスタ name_ja 完全一致での引き当てを任せる：
        //   - マスタに name_ja="キャスティング協力" の役職があれば → 自動引き当て成功
        //   - 無ければ → UnresolvedRoles に残り、QuickAddRoleDialog 起動時に
        //     PrefilledNameJa="キャスティング協力" として運用者に追加を促す
        // どちらの場合も最終的に role_code="CASTING_COOPERATION" 相当の役職に紐付くことを期待した運用。
        //
        // VOICE_CAST 役職の判定はパーサ単独ではマスタを引けないので、DisplayName が以下のいずれかを
        // 含むことで近似する：「声の出演」「キャスト」「声」（最後の「声」だけだと誤判定が多い恐れがあるため、
        // ここでは「声の出演」と「キャスト」の 2 語に限定）。
        ApplyCastingCooperationContextRename(result);

        return result;
    }

    /// <summary>
    /// @notes=... ディレクティブの値を、現在の <paramref name="pendingNotesTarget"/> が指す スコープ（Card / Tier / Group / Role / Block）の 
    /// Notes プロパティに割り当てる。
    /// </summary>
    /// <returns>true なら適用成功、false ならスコープ未開始（警告を出す）。</returns>
    private static bool ApplyNotesDirective(
        string value, int lineNo,
        ParsedCard? curCard, ParsedTier? curTier, ParsedGroup? curGroup, ParsedRole? curRole,
        ref ParsedBlock? curBlock,
        ref NotesTarget pendingNotesTarget, ref bool pendingColsForBlock,
        BulkParseResult result)
    {
        // 値が空文字なら null（明示クリア）として扱う。
        string? notesValue = value.Length == 0 ? null : value;

        switch (pendingNotesTarget)
        {
            case NotesTarget.Card:
                if (curCard is null)
                {
                    EmitNotesNoTargetWarning(lineNo, result);
                    return false;
                }
                curCard.Notes = notesValue;
                pendingNotesTarget = NotesTarget.None;
                return true;

            case NotesTarget.Tier:
                if (curTier is null)
                {
                    EmitNotesNoTargetWarning(lineNo, result);
                    return false;
                }
                curTier.Notes = notesValue;
                pendingNotesTarget = NotesTarget.None;
                return true;

            case NotesTarget.Group:
                if (curGroup is null)
                {
                    EmitNotesNoTargetWarning(lineNo, result);
                    return false;
                }
                curGroup.Notes = notesValue;
                pendingNotesTarget = NotesTarget.None;
                return true;

            case NotesTarget.Role:
                if (curRole is null)
                {
                    EmitNotesNoTargetWarning(lineNo, result);
                    return false;
                }
                curRole.Notes = notesValue;
                // 役職への割り当て直後はブロック対象に遷移する。@cols= の受付状態（pendingColsForBlock）
                // はそのまま維持（ブロックセットアップフェーズはまだ続いている認識）。
                pendingNotesTarget = NotesTarget.Block;
                return true;

            case NotesTarget.Block:
                if (curRole is null)
                {
                    EmitNotesNoTargetWarning(lineNo, result);
                    return false;
                }
                if (curBlock is null)
                {
                    // 遅延作成: 役職開始直後にいきなり @notes= で Block 備考が来たケース。
                    // 以降の leading_company / エントリ行はこの新ブロックに集約される。
                    curBlock = new ParsedBlock();
                    curRole.Blocks.Add(curBlock);
                }
                curBlock.Notes = notesValue;
                pendingNotesTarget = NotesTarget.None;
                return true;

            case NotesTarget.None:
            default:
                EmitNotesNoTargetWarning(lineNo, result);
                return false;
        }
    }

    /// <summary><c>@notes=...</c> ディレクティブが「直近のスコープ開始イベント」の直後でない位置に現れた場合の Block 警告を発出する。</summary>
    private static void EmitNotesNoTargetWarning(int lineNo, BulkParseResult result)
    {
        result.Warnings.Add(new ParseWarning
        {
            Severity = WarningSeverity.Block,
            LineNumber = lineNo,
            Message = $"{lineNo} 行目: @notes= はカード／ティア／グループ／役職／ブロックの開始直後でのみ指定できます（既にエントリ行が来ているか、対象スコープが開いていません）。"
        });
    }

    /// <summary>同一カード内に「声の出演」/「キャスト」相当の役職があるカードに限り、そのカードの 「協力」ロールを「キャスティング協力」に書き換える後処理。 パーサ単独ではマスタを引けないため、表示名のキーワード一致で近似する： VOICE_CAST 系の指標は「声の出演」「キャスト」を DisplayName に含むこと。</summary>
    private static void ApplyCastingCooperationContextRename(BulkParseResult result)
    {
        foreach (var card in result.Cards)
        {
            // このカードに VOICE_CAST 系役職が含まれているかをまず判定。
            // 含まれていなければリネーム対象外（同名の「協力」が他コンテキストで使われている場合に
            // 誤って書き換えてしまわないようにする）。
            bool hasVoiceCast = false;
            foreach (var tier in card.Tiers)
            {
                foreach (var group in tier.Groups)
                {
                    foreach (var role in group.Roles)
                    {
                        if (LooksLikeVoiceCastRole(role.DisplayName))
                        {
                            hasVoiceCast = true;
                            break;
                        }
                    }
                    if (hasVoiceCast) break;
                }
                if (hasVoiceCast) break;
            }

            if (!hasVoiceCast) continue;

            // VOICE_CAST 系を含むカード内の「協力」ロールを「キャスティング協力」に変える。
            // ParsedRole.DisplayName は init 専用ではあるが、後処理用のリネーム手段として
            // 別経路で持たせるのは複雑になりすぎる。Parser 自身が生成した型なので、後処理段で
            // 同じファイル内から書き換える運用は許容する（ただし API 経由ではなくフィールド差し替え）。
            for (int t = 0; t < card.Tiers.Count; t++)
            {
                var tier = card.Tiers[t];
                for (int g = 0; g < tier.Groups.Count; g++)
                {
                    var group = tier.Groups[g];
                    for (int r = 0; r < group.Roles.Count; r++)
                    {
                        var role = group.Roles[r];
                        if (string.Equals(role.DisplayName, "協力", StringComparison.Ordinal))
                        {
                            // 既存 ParsedRole の中身（Blocks 等）を保持したまま DisplayName だけ差し替える。
                            // ParsedRole.DisplayName は init 専用なので新インスタンスに詰め替える必要がある。
                            var renamed = new ParsedRole
                            {
                                DisplayName = "キャスティング協力",
                                LineNumber = role.LineNumber,
                            };
                            // Blocks は List のため、参照を引き継ぐ（中身そのまま）。
                            renamed.Blocks.AddRange(role.Blocks);
                            renamed.ResolvedRoleCode = role.ResolvedRoleCode;
                            renamed.ResolvedFormatKind = role.ResolvedFormatKind;
                            // 役職備考も引き継ぐ（@notes= で設定されていた場合）。
                            renamed.Notes = role.Notes;
                            group.Roles[r] = renamed;
                        }
                    }
                }
            }
        }
    }

    /// <summary>役職の DisplayName が VOICE_CAST 系（声の出演／キャスト等）かを近似判定する。 パーサ単独ではマスタを引けないため、表示名のキーワード一致で代用する。</summary>
    private static bool LooksLikeVoiceCastRole(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return false;
        // 「声の出演」「キャスト」を含む役職を VOICE_CAST 相当とみなす。
        return displayName.Contains("声の出演", StringComparison.Ordinal)
            || displayName.Contains("キャスト", StringComparison.Ordinal);
    }

    /// <summary>最初の有意行が来たタイミングで暗黙の Card / Tier / Group を 1 段ずつ作って状態を整える。 既に作られている場合は何もしない。</summary>
    private static void EnsureScaffold(
        ref ParsedCard? curCard, ref ParsedTier? curTier, ref ParsedGroup? curGroup,
        BulkParseResult result)
    {
        if (curCard is null)
        {
            curCard = new ParsedCard();
            result.Cards.Add(curCard);
        }
        if (curTier is null)
        {
            curTier = new ParsedTier();
            curCard.Tiers.Add(curTier);
        }
        if (curGroup is null)
        {
            curGroup = new ParsedGroup();
            curTier.Groups.Add(curGroup);
        }
    }

    /// <summary>
    /// 1 セル分のテキストを 1 つの <see cref="ParsedEntry"/> に変換する。
    /// VOICE_CAST 構文 / 角括弧 [企業] / [屋号#CIバージョン] LOGO / 通常テキスト / アスタ流用 を判定する。
    /// 追加のセル前後修飾子:
    /// <list type="bullet">
    ///   <item><description>行頭 <c>🎬</c>（U+1F3AC）→ <see cref="ParsedEntry.IsBroadcastOnly"/> = true</description></item>
    ///   <item><description>行頭 <c>&amp; </c> → <see cref="ParsedEntry.IsParallelContinuation"/> = true</description></item>
    ///   <item><description>行末 <c> // 備考</c> → <see cref="ParsedEntry.Notes"/> に値をセット</description></item>
    /// </list>
    /// これらは順序を問わず重ねて指定可能（例: <c>🎬 &amp; 山田 // 備考</c>）。
    /// </summary>
    private static ParsedEntry? ParseEntryCell(
        string cell, int lineNo, ParsedRole curRole, BulkParseResult result,
        ref string? carryOverForcedCharacter, ref bool lastEntryWasNonAsterCharacterVoice,
        ref string? rowLevelForcedCharacter, ref bool rowLevelLastWasNonAster,
        bool isFirstCellInRow)
    {
        // ─── エントリ前後修飾子の剥がし ───
        string? entryNotes = null;
        int notesPos = cell.LastIndexOf(EntryNotesSeparator, StringComparison.Ordinal);
        if (notesPos >= 0)
        {
            entryNotes = cell.Substring(notesPos + EntryNotesSeparator.Length).Trim();
            // 値が空（"山田 // " のような末尾セパレータだけ）の場合は notes クリア相当。
            // 仕様簡略化のため、空文字を null として扱う（既存値を消す扱い）。
            if (entryNotes.Length == 0) entryNotes = null;
            cell = cell.Substring(0, notesPos).TrimEnd();
            if (cell.Length == 0)
            {
                // セルが備考だけだった（"// 備考" のみ）場合はエントリ化できないので警告を出して捨てる。
                result.Warnings.Add(new ParseWarning
                {
                    Severity = WarningSeverity.Warning,
                    LineNumber = lineNo,
                    Message = $"{lineNo} 行目: 備考コメント（// ...）の本体（人物名等）が空です。"
                });
                return null;
            }
        }

        // 2) 行頭の 🎬 / "& " プレフィクスを順序問わず順次剥がす。
        //    両方付いていた場合は両方有効。重複（"🎬 🎬 山田"）も認める（後者はそのまま name に残す形）。
        bool isBroadcastOnly = false;
        bool isParallelContinuation = false;
        // 何度かループして、剥がせるだけ剥がす（順序問わず）。
        bool stripped;
        do
        {
            stripped = false;
            // 🎬 マーカー（後続スペースは省略可）
            if (cell.StartsWith(BroadcastOnlyMarker, StringComparison.Ordinal))
            {
                isBroadcastOnly = true;
                cell = cell.Substring(BroadcastOnlyMarker.Length).TrimStart();
                stripped = true;
            }
            // & マーカー（半角スペース必須）
            if (cell.StartsWith(ParallelContinuationMarker, StringComparison.Ordinal))
            {
                isParallelContinuation = true;
                cell = cell.Substring(ParallelContinuationMarker.Length).TrimStart();
                stripped = true;
            }
        } while (stripped);

        if (cell.Length == 0)
        {
            result.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Warning,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: マーカー（🎬 / & / //）以外の本体が空です。"
            });
            return null;
        }

        // 修飾子を反映するヘルパー（生成された ParsedEntry に flags / notes を後付けする）。
        ParsedEntry AttachModifiers(ParsedEntry e)
        {
            if (isBroadcastOnly) e.IsBroadcastOnly = true;
            if (isParallelContinuation) e.IsParallelContinuation = true;
            if (entryNotes is not null) e.Notes = entryNotes;
            return e;
        }

        // ─── [XXX] / [XXX#aliasid] / [XXX#CIバージョン] / [XXX#aliasid#CIバージョン] 形式
        // → COMPANY または LOGO エントリ ───
        // BracketEntryRegex で統合判定し、ci の有無で COMPANY / LOGO を分岐する。
        //   - ci あり → LOGO（CI バージョン違いは別 logos 行で表現する仕様）
        //   - ci なし → COMPANY
        // aliasid が取れていれば alias_id 明示参照として CompanyAliasIdOverride に乗せる。
        // グループトップ屋号 (leading_company_alias_id) の指定は二重ブラケット [[XXX]] 側の別構文で、
        // 呼び出し前のブロック処理段で先に消費されているため、ここに来る時点で [[XXX]] は存在しない。
        // 末尾の "×誤記" 後置（任意）でクレジット時の屋号誤記を記録する。CI バージョン側は誤記対象外。
        var bracketEntryMatch = BracketEntryRegex.Match(cell);
        if (bracketEntryMatch.Success)
        {
            // 屋号部分に "旧 => 新" 記法を適用する。CI バージョン部分は対象外。
            var (companyOld, companyNew) = SplitOldNewRedirect(bracketEntryMatch.Groups["name"].Value.Trim());

            int? companyAliasIdOverride = null;
            if (bracketEntryMatch.Groups["aliasid"].Success
                && int.TryParse(bracketEntryMatch.Groups["aliasid"].Value, out var parsedAliasId))
            {
                companyAliasIdOverride = parsedAliasId;
            }

            string? ci = bracketEntryMatch.Groups["ci"].Success
                ? bracketEntryMatch.Groups["ci"].Value.Trim()
                : null;
            if (string.IsNullOrEmpty(ci)) ci = null;

            string? misprint = bracketEntryMatch.Groups["misprint"].Success
                ? bracketEntryMatch.Groups["misprint"].Value.Trim()
                : null;
            if (string.IsNullOrEmpty(misprint)) misprint = null;

            return AttachModifiers(new ParsedEntry
            {
                Kind = ci is not null ? ParsedEntryKind.Logo : ParsedEntryKind.Company,
                CompanyRawText = companyNew,
                CompanyOldName = companyOld,
                CompanyMisprintText = misprint,
                CompanyAliasIdOverride = companyAliasIdOverride,
                LogoCiVersionLabel = ci,
                LineNumber = lineNo,
            });
        }

        // ─── VOICE_CAST 構文 <キャラ>声優 / <*キャラ>声優 / <キャラ#aliasid>声優 ───
        var vcMatch = VoiceCastRegex.Match(cell);
        if (vcMatch.Success)
        {
            string chara = vcMatch.Groups["chara"].Value.Trim();
            string actor = vcMatch.Groups["actor"].Value.Trim();
            bool aster = vcMatch.Groups["aster"].Success;

            // キャラ alias_id 明示参照（VoiceCastRegex で <chara#aliasid> としてキャプチャ済み）。
            // エンコーダが「DB に同名キャラ alias が複数存在するとき」に出力する記法、
            // 本フィールドに乗せて Apply 時にマスタ引き当てをスキップ・直接 ID 指定する。
            int? characterAliasIdOverride = null;
            if (vcMatch.Groups["aliasid"].Success
                && int.TryParse(vcMatch.Groups["aliasid"].Value, out var parsedCharaAliasId))
            {
                characterAliasIdOverride = parsedCharaAliasId;
            }

            if (chara.Length == 0)
            {
                result.Warnings.Add(new ParseWarning
                {
                    Severity = WarningSeverity.Block,
                    LineNumber = lineNo,
                    Message = $"{lineNo} 行目: <...> 内のキャラ名が空です。"
                });
                return null;
            }

            if (actor.Length == 0)
            {
                // 声優名が空 = 仕様外（声優名は必須）。
                result.Warnings.Add(new ParseWarning
                {
                    Severity = WarningSeverity.Block,
                    LineNumber = lineNo,
                    Message = $"{lineNo} 行目: <{(aster ? "*" : "")}{chara}> の後ろに声優名がありません。"
                });
                return null;
            }

            // 声優名から所属 (xxx) を切り出す。
            var (personName, affiliation) = SplitAffiliation(actor);

            // 声優側の先頭 "*" 強制新規マーカーを剥がす（PERSON 構文 *X と同じ流儀）。
            var (personAfterAster, isForcedNewPerson) = SplitForcedNewMarker(personName);

            // キャラ名・声優名それぞれに "名義 × 誤記" 記法を先に適用する（× は名義に対する補助情報）。
            // 例: <キュアブラック×キュアブラッグ>菊池 心×菊地 心
            //   → chara_main="キュアブラック", chara_misprint="キュアブラッグ"
            //   → person_main="菊池 心",       person_misprint="菊地 心"
            // × の左側がさらに "旧 => 新" 記法の対象になり得るため、× を先に剥がす。
            var (charaBeforeRedirect, charaMisprint) = SplitMisprint(chara);
            var (personBeforeRedirect, personMisprint) = SplitMisprint(personAfterAster);

            // キャラ名・声優名それぞれに "旧 => 新" 記法を適用する。
            // キャラ名側で <キュアブラック旧 => キュアブラック新>、声優名側で 本名 旧 => 本名 新 のように
            // 独立して指定可能。両側同時の併用も許容する。
            var (charaOld, charaNew) = SplitOldNewRedirect(charaBeforeRedirect);
            var (personOld, personNew) = SplitOldNewRedirect(personBeforeRedirect);

            // 声優名末尾の "#数値" を person_alias_id 明示参照として抜き出す
            // （SplitMisprint / SplitOldNewRedirect の後段で実施。誤記やリダイレクトの "=>" を先に剥がしてから
            //  純粋な名前末尾の #数値 を判定する方が安全）。
            var (personPureName, personAliasIdOverride) = SplitAliasIdOverride(personNew);

            // 声優名が半角SP / 全角SP / 「・」のいずれも含まない場合、姓・名に
            // 機械的に分解できない（family_name / given_name のいずれも NULL で投入される）
            // ため、Warning レベルの警告を出してユーザーに気付かせる。
            // 例: 「ゆかな」「矢島晶子」のような芸名 1 単語。データ投入は許容する（Warning なので
            // 適用ボタンは無効化されない）が、人物管理画面で姓・名を後から補正できることを示唆する。
            // 警告対象は純粋名前（personPureName）。旧表記 / alias_id 明示参照側は判定不要。
            EmitNameSplitWarningIfNeeded(personPureName, lineNo, "声優名", result);

            var entry = new ParsedEntry
            {
                Kind = ParsedEntryKind.CharacterVoice,
                CharacterRawText = charaNew,
                CharacterOldName = charaOld,
                CharacterMisprintText = charaMisprint,
                IsForcedNewCharacter = aster,
                CharacterAliasIdOverride = characterAliasIdOverride,
                PersonRawText = personPureName,
                PersonOldName = personOld,
                PersonMisprintText = personMisprint,
                IsForcedNewPerson = isForcedNewPerson,
                PersonAliasIdOverride = personAliasIdOverride,
                AffiliationRawText = affiliation,
                LineNumber = lineNo,
            };

            // 流用フラグ更新時に保持する「キャラ参照キー」も新表記側に統一する
            // （後続の <*X> 流用行で同じキャラの別声優を入れる用途では「新表記＝今回登録するキャラ」を流用するのが自然）。
            chara = charaNew;

            // 流用フラグ更新: アスタ付きの場合のみ後続行で流用可能。
            if (aster)
            {
                carryOverForcedCharacter = chara;
                rowLevelForcedCharacter = chara;
                lastEntryWasNonAsterCharacterVoice = false;
                rowLevelLastWasNonAster = false;
            }
            else
            {
                carryOverForcedCharacter = null;
                rowLevelForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = true;
                rowLevelLastWasNonAster = true;
            }

            return AttachModifiers(entry);
        }

        // ─── キャラ指定なしのプレーン行（VOICE_CAST 役職内かどうかで処理分岐） ───
        if (LooksLikeVoiceCastRole(curRole))
        {
            // 流用判定（行頭セルなら carryOver、それ以外なら rowLevel を見る）
            string? forcedChar = isFirstCellInRow ? carryOverForcedCharacter : rowLevelForcedCharacter;
            bool lastWasNonAster = isFirstCellInRow ? lastEntryWasNonAsterCharacterVoice : rowLevelLastWasNonAster;

            if (forcedChar is not null)
            {
                // <*X> 流用: 「別個の新規 X」+「このセルが声優名」のペアエントリ
                var (personName, affiliation) = SplitAffiliation(cell);
                // 声優側の先頭 "*" 強制新規マーカーを剥がす。
                var (personAfterAster, isForcedNewPerson) = SplitForcedNewMarker(personName);
                // 声優名側に "名義 × 誤記" 記法を先に適用してから "旧 => 新" を適用する。
                var (personBeforeRedirect, personMisprint) = SplitMisprint(personAfterAster);
                var (personOld, personNew) = SplitOldNewRedirect(personBeforeRedirect);
                // 末尾の #数値 を person_alias_id 明示参照として抜き出す。
                var (personPureName, personAliasIdOverride) = SplitAliasIdOverride(personNew);
                // 姓名分割不能名義の Warning。
                EmitNameSplitWarningIfNeeded(personPureName, lineNo, "声優名", result);
                return AttachModifiers(new ParsedEntry
                {
                    Kind = ParsedEntryKind.CharacterVoice,
                    CharacterRawText = forcedChar,
                    IsForcedNewCharacter = true,
                    PersonRawText = personPureName,
                    PersonOldName = personOld,
                    PersonMisprintText = personMisprint,
                    IsForcedNewPerson = isForcedNewPerson,
                    PersonAliasIdOverride = personAliasIdOverride,
                    AffiliationRawText = affiliation,
                    LineNumber = lineNo,
                });
            }

            if (lastWasNonAster)
            {
                // <X>（アスタなし）後にキャラ指定なし行 → Block 警告
                result.Warnings.Add(new ParseWarning
                {
                    Severity = WarningSeverity.Block,
                    LineNumber = lineNo,
                    Message = $"{lineNo} 行目: <X>（アスタリスクなし）の直後にキャラ指定なし行は使えません。<*X> で書き直すか、各行に <キャラ名> を明示してください。"
                });
                return null;
            }

            // 何の <...> 文脈も無くいきなり声優名のみ行 → Block 警告
            result.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Block,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: VOICE_CAST 役職でキャラ指定なし行が、<...> の文脈なしに現れました。"
            });
            return null;
        }

        // ─── 通常 PERSON エントリ ───
        {
            // セル先頭の "*" 強制新規マーカーを最初に剥がす（所属抜き出しより先に行う：
            // 所属括弧の中身に "*" が偶然含まれていても、それは強制新規マーカーではないため）。
            var (cellAfterAster, isForcedNewPerson) = SplitForcedNewMarker(cell);

            var (personName, affiliation) = SplitAffiliation(cellAfterAster);

            // 人物名側に "名義 × 誤記" 記法を先に適用してから "旧 => 新" を適用する。
            // 所属 "(...)" は SplitAffiliation で既に剥がれているため、× の左右は純粋に人物名表記に限られる。
            var (personBeforeRedirect, personMisprint) = SplitMisprint(personName);
            var (personOld, personNew) = SplitOldNewRedirect(personBeforeRedirect);

            // 末尾の "#数値" を person_alias_id 明示参照として抜き出す
            // （CreditBulkInputEncoder が「DB に同名 alias が複数」のときに出力する記法を読み戻す）。
            var (personPureName, personAliasIdOverride) = SplitAliasIdOverride(personNew);

            // 姓名分割不能名義の Warning（PERSON 種別）。
            EmitNameSplitWarningIfNeeded(personPureName, lineNo, "人物名", result);

            // 役職が COMPANY_ONLY や LOGO_ONLY の場合は適用フェーズで再解釈する。
            // パース時点では PERSON 素案で持ち、適用時に role_format_kind を見て調整。
            return AttachModifiers(new ParsedEntry
            {
                Kind = ParsedEntryKind.Person,
                PersonRawText = personPureName,
                PersonOldName = personOld,
                PersonMisprintText = personMisprint,
                IsForcedNewPerson = isForcedNewPerson,
                PersonAliasIdOverride = personAliasIdOverride,
                AffiliationRawText = affiliation,
                LineNumber = lineNo,
            });
        }
    }

    /// <summary>役職が VOICE_CAST 系かどうかを推定する。 パース時点では <see cref="ParsedRole.ResolvedFormatKind"/> がまだ null のため、 表示名から「声」「キャスト」などの語を含むかで素朴判定する。 厳密な判定は適用フェーズで <c>roles.role_format_kind</c> 引き当て後に再評価。</summary>
    private static bool LooksLikeVoiceCastRole(ParsedRole role)
    {
        if (role.ResolvedFormatKind is not null)
            return role.ResolvedFormatKind == "VOICE_CAST";

        // 名前ベースの素朴判定。「声の出演」「キャスト」「VOICE CAST」「Voice」を含むか。
        string n = role.DisplayName;
        return n.Contains("声") || n.Contains("キャスト") || n.Contains("CAST", StringComparison.OrdinalIgnoreCase) || n.Contains("Voice", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>行全体がハイフン（半角 '-'）のみで構成されるかを判定する。</summary>
    private static bool IsAllHyphens(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s)
        {
            if (c != '-') return false;
        }
        return true;
    }

    /// <summary>
    /// 人物名が「半角SP / 全角SP / 中黒（・）」のいずれも含まない場合、姓・名に機械的に分解できない
    /// 旨を Warning レベルで警告する。
    /// PersonsRepository.QuickAddWithSingleAliasAsync は呼び出し側で family_name / given_name を
    /// 渡す必要があるが、Apply フェーズで姓名分解する際にこれら 3 つの区切り文字のいずれも無いと
    /// 「分解不能 = family_name / given_name の両方が NULL」になる。データ整合性は壊れないが、
    /// 後続の人物検索や姓・名ベースのソートで使えないことになるため、ユーザーに気付かせる目的。
    /// Warning レベルなので適用ボタンは無効化されない。「ゆかな」「矢島晶子」のような芸名 1 単語の
    /// 名義は普通にあり得るため、Block にすると現実的でない。
    /// </summary>
    /// <param name="name">判定対象の人物名（所属切り出し後の純粋な氏名表記）。</param>
    /// <param name="lineNo">警告を紐付ける行番号。</param>
    /// <param name="kindLabel">警告メッセージ中の種別ラベル（例: 「人物名」「声優名」）。</param>
    /// <param name="result">警告を追加する先の <see cref="BulkParseResult"/>。</param>
    private static void EmitNameSplitWarningIfNeeded(string name, int lineNo, string kindLabel, BulkParseResult result)
    {
        if (string.IsNullOrEmpty(name)) return;

        // 半角SP / 全角SP / 中黒（・） のいずれかが含まれていれば分解可能とみなす。
        bool hasSeparator = name.IndexOf(' ') >= 0
                         || name.IndexOf('\u3000') >= 0
                         || name.IndexOf('・') >= 0;

        if (!hasSeparator)
        {
            result.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Warning,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: {kindLabel}「{name}」は姓・名に分割できません（半角SP / 全角SP / 「・」のいずれも含まないため、family_name / given_name は NULL で投入されます）。"
            });
        }
    }

    /// <summary>名前の末尾の "(...)" / "（...）" を所属として切り出して返す。 該当なしの場合は (input, null) を返す。</summary>
    private static (string Name, string? Affiliation) SplitAffiliation(string text)
    {
        var m = AffiliationRegex.Match(text);
        if (!m.Success) return (text, null);
        return (m.Groups["name"].Value.Trim(), m.Groups["aff"].Value.Trim());
    }

    /// <summary>
    /// 「名義 × 誤記」記法のセパレータで文字列を左右分割する。
    /// 左側 = 正名義（マスタ参照キー）、右側 = 誤記表記（フリーテキスト）。
    /// <see cref="MisprintSeparators"/> のいずれの文字でも分割対象とし、最初に現れた誤記セパレータで切る
    /// （複数並べる用途は無いが、最初の × 以降をすべて誤記文字列として扱う安全側仕様）。
    /// 片側が空（<c>"山田×"</c> / <c>"×山田"</c>）の場合は誤記指定なしとして扱い、(text, null) を返す。
    /// 誤記分割は <see cref="SplitOldNewRedirect"/> よりも先に呼ぶこと
    /// （× の左側がさらに <c>=&gt;</c> 記法の対象になる「旧 =&gt; 新×誤記」もあり得るため）。
    /// </summary>
    /// <param name="text">対象文字列（既に行頭マーカー / 行末備考は剥がれている前提）。</param>
    /// <returns>(正名義部分 = 左側, 誤記表記 or null = 右側)。</returns>
    private static (string MainName, string? MisprintText) SplitMisprint(string text)
    {
        if (string.IsNullOrEmpty(text)) return (string.Empty, null);

        int sep = text.IndexOfAny(MisprintSeparators);
        if (sep < 0) return (text.Trim(), null);

        string left = text.Substring(0, sep).Trim();
        string right = text.Substring(sep + 1).Trim();

        // 片側が空のセパレータ（"山田×" や "×山田"）は誤記指定なしとして扱う。
        if (left.Length == 0 || right.Length == 0) return (text.Trim(), null);

        return (left, right);
    }

    /// <summary>
    /// 末尾の「<c>#数値</c>」を alias_id 明示参照として抜き出す。
    /// CreditBulkInputEncoder が「DB に同名 alias が複数存在するエントリ」のラウンドトリップ性を確保する
    /// ために逆変換時に出力する記法を、逆方向（テキスト → データ）で読み戻すヘルパ。
    /// <para>
    /// 末尾の <c>#数値</c> が見つかれば「数値（純整数のみ）」を <see cref="ParsedEntry.PersonAliasIdOverride"/>
    /// 等の override 値として返し、<c>#数値</c> 自身は名前文字列から除去する。
    /// <c>#</c> の右に非整数（CI バージョン等）が来ているケースは alias_id ではないので素通し。
    /// </para>
    /// 呼び出し順序：<see cref="SplitMisprint"/> / <see cref="SplitOldNewRedirect"/> よりも後段で実施する
    /// （誤記 × / リダイレクト => を先に剥がしてから、純粋な名前末尾の #数値 を抜き出す方が安全）。
    /// </summary>
    private static (string PureName, int? AliasIdOverride) SplitAliasIdOverride(string text)
    {
        if (string.IsNullOrEmpty(text)) return (string.Empty, null);
        var m = Regex.Match(text, @"^(?<name>.+?)#(?<id>\d+)\s*$");
        if (!m.Success) return (text, null);
        return (m.Groups["name"].Value.TrimEnd(), int.Parse(m.Groups["id"].Value));
    }

    /// <summary>
    /// 先頭の「<c>*</c>」を強制新規マーカーとして抜き出す。
    /// PERSON 構文 <c>*山田 太郎</c> で同姓同名の別人を強制新規登録するための記法。
    /// CHARACTER_VOICE の <c>&lt;*X&gt;</c> は VoiceCastRegex 側で既に専用キャプチャしているので、
    /// 本ヘルパは主に PERSON エントリの先頭処理で使う。
    /// </summary>
    private static (string PureName, bool IsForcedNew) SplitForcedNewMarker(string text)
    {
        if (string.IsNullOrEmpty(text)) return (string.Empty, false);
        if (!text.StartsWith('*')) return (text, false);
        return (text.Substring(1).TrimStart(), true);
    }

    /// <summary>「旧名義 =&gt; 新名義」記法のセパレータで文字列を左右分割する。</summary>
    /// <param name="text">対象文字列（既に行頭マーカー / 行末備考は剥がれている前提）。</param>
    /// <returns>(旧表記 or null, 新表記 = この行で実際に表示する名義)。</returns>
    private static (string? OldName, string NewName) SplitOldNewRedirect(string text)
    {
        if (string.IsNullOrEmpty(text)) return (null, string.Empty);

        // 最後の "=>" を採用する（左右の意図性を尊重するため。複数並べる用途は無いが安全側）。
        int sep = text.LastIndexOf(OldNewRedirectSeparator, StringComparison.Ordinal);
        if (sep < 0) return (null, text.Trim());

        string left = text.Substring(0, sep).Trim();
        string right = text.Substring(sep + OldNewRedirectSeparator.Length).Trim();

        // 片側が空のセパレータ書き間違いは「リダイレクト無し」として扱う。
        // ただしユーザーが意図して空にしているケース（"=> 山田" や "山田 =>"）は実害低いので
        // 黙ってフォールバックに倒す（警告は出さない）。
        if (left.Length == 0 || right.Length == 0) return (null, text.Trim());

        return (left, right);
    }
}
