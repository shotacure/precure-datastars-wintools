using System.Text.RegularExpressions;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット一括入力テキストの構文解析器（v1.2.1 追加）。
/// <para>
/// 仕様（テキスト書式）:
/// <list type="bullet">
///   <item><description><c>XXX:</c> または <c>XXX：</c>（行末コロン）→ 役職開始</description></item>
///   <item><description><c>-</c>（半角ハイフン1個・前後トリム後の単独行）→ グループ区切り</description></item>
///   <item><description><c>--</c> → ティア区切り（最大 tier_no=2）</description></item>
///   <item><description><c>---</c> → カード区切り</description></item>
///   <item><description>空行 → 同一役職内のブロック区切り</description></item>
///   <item><description><c>[XXX]</c>（行全体）→ ブロック先頭なら leading_company_alias_id、それ以外は COMPANY エントリ</description></item>
///   <item><description>タブ区切り行 → エントリ群（タブ最大数+1 = col_count）</description></item>
///   <item><description><c>&lt;キャラ名義&gt;声優名義</c>（VOICE_CAST 役職内）→ CHARACTER_VOICE</description></item>
///   <item><description><c>&lt;*キャラ名義&gt;声優名義</c>（VOICE_CAST 役職内）→ 強制新規キャラ</description></item>
///   <item><description><c>&lt;*X&gt;</c> 直後の声優名のみ行 → 各行を別個の新規 X として処理</description></item>
///   <item><description>通常テキスト → PERSON</description></item>
/// </list>
/// </para>
/// <para>
/// 警告は適用ブロックレベル（<see cref="WarningSeverity.Block"/>）と通常警告に分かれる。
/// 適用ブロックの例: 先頭が役職指定でない／ハイフン4個以上／ティア3個目超／<c>&lt;X&gt;</c> 直後にキャラ指定なし行。
/// </para>
/// </summary>
public static class CreditBulkInputParser
{
    // 役職開始行: 行末がコロン（半角 ':' または全角 '：'）。前後の空白は trim 後判定。
    private static readonly Regex RoleHeadRegex = new(@"^(?<name>.+?)[：:]\s*$", RegexOptions.Compiled);

    // 行全体が [XXX] のパターン（先頭・末尾は trim 済みを期待）
    private static readonly Regex BracketCompanyRegex = new(@"^\[(?<name>[^\[\]]+)\]$", RegexOptions.Compiled);

    // VOICE_CAST 構文: <キャラ>声優 または <*キャラ>声優
    // キャラ部分は閉じ角括弧以外、空でも警告対象として捕まえる。
    private static readonly Regex VoiceCastRegex = new(@"^<(?<aster>\*)?(?<chara>[^<>]*)>(?<actor>.*)$", RegexOptions.Compiled);

    // 人物名末尾の所属 "(...)" / "（...）" 抽出。
    // 名前の途中に括弧がある場合（"山田(本名)"等）は厳密性より素朴さを優先し、最右の括弧を採用。
    private static readonly Regex AffiliationRegex = new(@"^(?<name>.+?)\s*[(（](?<aff>[^()（）]+)[)）]\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 入力テキストを構文解析して <see cref="BulkParseResult"/> を返す。
    /// 入力 null/空文字は空の結果を返す（警告 0）。
    /// </summary>
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

        // v1.2.1 追加: 直前のロールの表示名を保持する。
        // -/-- /--- でロールが閉じられた直後にエントリ行が来た場合、明示的な役職指定が無くても
        // 「直前と同じ役職名で新カード/新ティア/新グループ配下に同名ロールを暗黙再作成」する用途。
        // たとえば「声の出演:」が長く続く場合に、ユーザーがカード区切りごとに「声の出演:」を
        // 書き直す手間を省くための仕様。区切りの種別（カード/ティア/グループ）に関わらず動作する。
        string? lastRoleDisplayName = null;

        // テキスト先頭が役職指定でない場合の警告は最初の有意行で 1 回だけ出す。
        bool firstMeaningfulLineSeen = false;

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
                }
                // 連続空行は何もしない。
                continue;
            }

            // ─── ハイフン区切り検出（- / -- / --- / ---- 以上）───
            // 「行全体がハイフンのみ」かつ前後トリム後の判定。
            if (IsAllHyphens(trimmed))
            {
                int hyphens = trimmed.Length;

                if (hyphens >= 4)
                {
                    result.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Block,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: ハイフンは最大 3 個まで（カード区切り）。{hyphens} 個は不正です。"
                    });
                    continue;
                }

                EnsureScaffold(ref curCard, ref curTier, ref curGroup, result);

                // v1.2.1 追加: 区切り直後の同名ロール自動継承用に、直前ロールの表示名を保存しておく。
                // ハイフン 1/2/3 個（グループ／ティア／カード区切り）すべてで同じ動作にする。
                // curRole が null（明示的に区切り直前で空ロール状態だった）の場合は更新せず、
                // 既に保持している値を持ち越す（連続区切り間でも継承し続ける挙動）。
                if (curRole is not null)
                {
                    lastRoleDisplayName = curRole.DisplayName;
                }

                if (hyphens == 3)
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
                }
                else if (hyphens == 2)
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
                }
                else // hyphens == 1
                {
                    // グループ区切り: 同 Tier 内で新グループを作る。
                    curGroup = new ParsedGroup();
                    curTier!.Groups.Add(curGroup);
                    curRole = null;
                    curBlock = null;
                }

                carryOverForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = false;
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

                // v1.2.1 追加: 明示的な役職指定があった場合は、自動継承の追跡名を新しい名前で更新する
                // （後続区切り後の自動継承はこの新ロール名を引き継ぐ）。
                lastRoleDisplayName = curRole.DisplayName;

                carryOverForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = false;
                continue;
            }

            // ─── ここに到達したら「エントリ行」相当 ───
            // 役職が未開始の状態でエントリ行が来た場合、2 つの分岐がある:
            //   (a) v1.2.1 仕様: 直前に同名ロールがあり、ハイフン区切りで閉じられた直後 →
            //       新カード/新ティア/新グループ配下に同名ロールを暗黙再作成して続行する。
            //   (b) そもそもテキスト先頭が役職指定でない場合 → Block 警告を出してスキップ。
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

            // ─── ブロック先頭の [XXX] 行は leading_company として吸収 ───
            // ブロック先頭 = curBlock.Rows.Count == 0 かつ LeadingCompanyText 未設定。
            var bracketMatch = BracketCompanyRegex.Match(trimmed);
            if (bracketMatch.Success && curBlock.Rows.Count == 0 && curBlock.LeadingCompanyText is null)
            {
                curBlock.LeadingCompanyText = bracketMatch.Groups["name"].Value.Trim();

                carryOverForcedCharacter = null;
                lastEntryWasNonAsterCharacterVoice = false;
                continue;
            }

            // ─── タブ区切りで複数エントリを取り出す ───
            // タブ最大数 + 1 が ColCount の意図。最大値はブロック内全行で集計する。
            string[] cols = raw.Split('\t');

            // 行頭・行末の空白も trim する（タブ含まず）。
            //   ※ raw を直接 split するのは「行先頭にスペースを入れて『字下げで複数行が同じグループ』」という
            //      旧仕様の名残を残す可能性があるが、本パーサは字下げを意味として使わない（タブだけが意味を持つ）。
            for (int c = 0; c < cols.Length; c++) cols[c] = cols[c].Trim();

            // 全カラムが空（タブだけの行）→ 何もしない。
            if (cols.All(string.IsNullOrEmpty)) continue;

            int rowColCount = cols.Length;
            if (rowColCount > curBlock.ColCount) curBlock.ColCount = rowColCount;

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
        return result;
    }

    /// <summary>
    /// 最初の有意行が来たタイミングで暗黙の Card / Tier / Group を 1 段ずつ作って状態を整える。
    /// 既に作られている場合は何もしない。
    /// </summary>
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
    /// VOICE_CAST 構文 / 角括弧 [企業] / 通常テキスト / アスタ流用 を判定する。
    /// </summary>
    private static ParsedEntry? ParseEntryCell(
        string cell, int lineNo, ParsedRole curRole, BulkParseResult result,
        ref string? carryOverForcedCharacter, ref bool lastEntryWasNonAsterCharacterVoice,
        ref string? rowLevelForcedCharacter, ref bool rowLevelLastWasNonAster,
        bool isFirstCellInRow)
    {
        // ─── [XXX] 形式 → COMPANY エントリ（既にブロック先頭の leading として吸収されていない場合） ───
        var bracketMatch = BracketCompanyRegex.Match(cell);
        if (bracketMatch.Success)
        {
            // bracket がブロック先頭で leading に吸収される判定は呼び出し前で済んでいる。
            // ここに来たということは「行内の途中」または「ブロック 2 行目以降」で [XXX] が現れた、というケース。
            return new ParsedEntry
            {
                Kind = ParsedEntryKind.Company,
                CompanyRawText = bracketMatch.Groups["name"].Value.Trim(),
                LineNumber = lineNo,
            };
        }

        // ─── VOICE_CAST 構文 <キャラ>声優 / <*キャラ>声優 ───
        var vcMatch = VoiceCastRegex.Match(cell);
        if (vcMatch.Success)
        {
            string chara = vcMatch.Groups["chara"].Value.Trim();
            string actor = vcMatch.Groups["actor"].Value.Trim();
            bool aster = vcMatch.Groups["aster"].Success;

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

            // v1.2.1 追加: 声優名が半角SP / 全角SP / 「・」のいずれも含まない場合、姓・名に
            // 機械的に分解できない（family_name / given_name のいずれも NULL で投入される）
            // ため、Warning レベルの警告を出してユーザーに気付かせる。
            // 例: 「ゆかな」「矢島晶子」のような芸名 1 単語。データ投入は許容する（Warning なので
            // 適用ボタンは無効化されない）が、人物管理画面で姓・名を後から補正できることを示唆する。
            EmitNameSplitWarningIfNeeded(personName, lineNo, "声優名", result);

            var entry = new ParsedEntry
            {
                Kind = ParsedEntryKind.CharacterVoice,
                CharacterRawText = chara,
                IsForcedNewCharacter = aster,
                PersonRawText = personName,
                AffiliationRawText = affiliation,
                LineNumber = lineNo,
            };

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

            return entry;
        }

        // ─── キャラ指定なしのプレーン行（VOICE_CAST 役職内かどうかで処理分岐） ───
        // 役職が VOICE_CAST っぽい かつ 直前 <*X> がある場合 → 別個の新規 X + 当該声優のペアエントリ
        // 役職が VOICE_CAST っぽい かつ 直前 <X>（アスタなし）の場合 → 警告（曖昧）
        // それ以外 → PERSON エントリ
        if (LooksLikeVoiceCastRole(curRole))
        {
            // 流用判定（行頭セルなら carryOver、それ以外なら rowLevel を見る）
            string? forcedChar = isFirstCellInRow ? carryOverForcedCharacter : rowLevelForcedCharacter;
            bool lastWasNonAster = isFirstCellInRow ? lastEntryWasNonAsterCharacterVoice : rowLevelLastWasNonAster;

            if (forcedChar is not null)
            {
                // <*X> 流用: 「別個の新規 X」+「このセルが声優名」のペアエントリ
                var (personName, affiliation) = SplitAffiliation(cell);
                // v1.2.1 追加: 姓名分割不能名義の Warning。
                EmitNameSplitWarningIfNeeded(personName, lineNo, "声優名", result);
                return new ParsedEntry
                {
                    Kind = ParsedEntryKind.CharacterVoice,
                    CharacterRawText = forcedChar,
                    IsForcedNewCharacter = true,
                    PersonRawText = personName,
                    AffiliationRawText = affiliation,
                    LineNumber = lineNo,
                };
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
            var (personName, affiliation) = SplitAffiliation(cell);

            // v1.2.1 追加: 姓名分割不能名義の Warning（PERSON 種別）。
            EmitNameSplitWarningIfNeeded(personName, lineNo, "人物名", result);

            // 役職が COMPANY_ONLY や LOGO_ONLY の場合は適用フェーズで再解釈する。
            // パース時点では PERSON 素案で持ち、適用時に role_format_kind を見て調整。
            return new ParsedEntry
            {
                Kind = ParsedEntryKind.Person,
                PersonRawText = personName,
                AffiliationRawText = affiliation,
                LineNumber = lineNo,
            };
        }
    }

    /// <summary>
    /// 役職が VOICE_CAST 系かどうかを推定する。
    /// パース時点では <see cref="ParsedRole.ResolvedFormatKind"/> がまだ null のため、
    /// 表示名から「声」「キャスト」などの語を含むかで素朴判定する。
    /// 厳密な判定は適用フェーズで <c>roles.role_format_kind</c> 引き当て後に再評価。
    /// </summary>
    private static bool LooksLikeVoiceCastRole(ParsedRole role)
    {
        if (role.ResolvedFormatKind is not null)
            return role.ResolvedFormatKind == "VOICE_CAST";

        // 名前ベースの素朴判定。「声の出演」「キャスト」「VOICE CAST」「Voice」を含むか。
        string n = role.DisplayName;
        return n.Contains("声") || n.Contains("キャスト") || n.Contains("CAST", StringComparison.OrdinalIgnoreCase) || n.Contains("Voice", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 行全体がハイフン（半角 '-'）のみで構成されるかを判定する。
    /// </summary>
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
    /// 旨を Warning レベルで警告する（v1.2.1 追加）。
    /// <para>
    /// PersonsRepository.QuickAddWithSingleAliasAsync は呼び出し側で family_name / given_name を
    /// 渡す必要があるが、Apply フェーズで姓名分解する際にこれら 3 つの区切り文字のいずれも無いと
    /// 「分解不能 = family_name / given_name の両方が NULL」になる。データ整合性は壊れないが、
    /// 後続の人物検索や姓・名ベースのソートで使えないことになるため、ユーザーに気付かせる目的。
    /// </para>
    /// <para>
    /// Warning レベルなので適用ボタンは無効化されない。「ゆかな」「矢島晶子」のような芸名 1 単語の
    /// 名義は普通にあり得るため、Block にすると現実的でない。
    /// </para>
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

    /// <summary>
    /// 名前の末尾の "(...)" / "（...）" を所属として切り出して返す。
    /// 該当なしの場合は (input, null) を返す。
    /// </summary>
    private static (string Name, string? Affiliation) SplitAffiliation(string text)
    {
        var m = AffiliationRegex.Match(text);
        if (!m.Success) return (text, null);
        return (m.Groups["name"].Value.Trim(), m.Groups["aff"].Value.Trim());
    }
}
