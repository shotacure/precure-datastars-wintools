using System.Text;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// Draft レイヤを一括入力フォーマット文字列に逆翻訳するエンコーダ。
/// <see cref="CreditBulkInputParser"/> の構文をそのまま反転させて、Draft の Card / Tier / Group / Role / Block / Entry を
/// プレーンテキストに直す。生成されたテキストを再度 <see cref="CreditBulkInputParser.Parse"/> に通すと、
/// マスタ ID 解決後の状態で同じ構造が再現できる（ラウンドトリップ性）。
/// 主用途:
/// <list type="bullet">
///   <item><description>右クリック「📝 一括入力で編集...」（Phase 4 で実装予定）でツリー上のスコープを
///     文字列化し、<c>CreditBulkInputDialog</c> の ReplaceScope モードに既定値として渡す。</description></item>
///   <item><description>クレジット全体を初期テキストとしてダンプし、テキストエディタで一括編集する用途。</description></item>
///   <item><description>クレジットのバックアップ / 移植用テキスト出力（人間可読の中間表現）。</description></item>
/// </list>
/// 出力する構文（<see cref="CreditBulkInputParser"/> 仕様の鏡像）:
/// <list type="bullet">
///   <item><description>カード区切り: <c>----</c>（2 枚目以降の Card の前に挿入）</description></item>
///   <item><description>ティア区切り: <c>---</c>（1 カード内の 2 つ目以降の Tier の前）</description></item>
///   <item><description>グループ区切り: <c>--</c>（1 Tier 内の 2 つ目以降の Group の前）</description></item>
///   <item><description>役職開始: <c>役職名:</c>（<see cref="LookupCache.LookupRoleNameJaAsync"/> で名前解決）</description></item>
///   <item><description>ブロック区切り: 空行（同一役職内の 2 つ目以降の Block の前）</description></item>
///   <item><description>各レベルの Notes: <c>@notes=備考</c>（区切り行直後）</description></item>
///   <item><description>ブロックの ColCount: <c>@cols=N</c>（<see cref="DraftBlock.Entity"/>.<c>ColCount</c> &gt; 1 の場合のみ出力。
///     <c>ColCount = 1</c> は省略時のデフォルトと同じなので明示しない）</description></item>
///   <item><description>グループトップ屋号: <c>[[屋号]]</c>（ブロックの最初の有意行）</description></item>
///   <item><description>エントリ: 種別ごとの構文（PERSON は素のテキスト、CHARACTER_VOICE は <c>&lt;キャラ&gt;声優</c>、
///     COMPANY は <c>[屋号]</c>、LOGO は <c>[屋号#CIバージョン]</c>、TEXT は raw_text のまま）</description></item>
///   <item><description>エントリ前後修飾子: 行頭 <c>🎬 </c>（IsBroadcastOnly）／<c>&amp; </c>（A/B 併記）、
///     行末 <c> // 備考</c>（Notes）</description></item>
/// </list>
/// マスタ未引きエントリ（TEXT 種別、または ID が解決不能なケース）は <c>raw_text</c> をそのまま出力する。
/// 再度パースすると PERSON 素案として認識される可能性があるため、必要に応じて <c>raw_text</c> 側に
/// 識別記号（<c>[XXX]</c> や <c>&lt;X&gt;</c>）が含まれていれば、Parser 側でその記号を読み取って
/// 種別が再構築される（ラウンドトリップ性は概ね保たれる）。
/// </summary>
internal static class CreditBulkInputEncoder
{
    // 一括入力フォーマットの行終端コード。
    // パーサ側（CreditBulkInputParser）は \r\n / \r / \n 全部を分割キーとして受け付けるため、
    // 出力としてはどれを使ってもラウンドトリップ性に影響はない。
    // ただし WinForms TextBox は LF 単独を改行として表示しない（カーソル送り扱いされない）ため、
    // 「右クリック → 一括入力で編集」でダイアログに初期テキストとして表示される際に
    // 全行が 1 行に潰れて見える表示バグが発生する。
    // この問題を回避するため、プラットフォーム標準の改行（Windows 上では \r\n）を採用する。
    // const から static readonly へ変更しているのは Environment.NewLine がコンパイル時定数でないため。
    private static readonly string LineSeparator = Environment.NewLine;

    // CreditBulkInputParser と同じマーカー定義（実体は同じだが、Encoder 側で再宣言することで
    // Parser 側の private const に依存せずに完結させる）。
    private const string BroadcastOnlyMarker = "\uD83C\uDFAC"; // 🎬 (U+1F3AC)
    private const string ParallelContinuationMarker = "& ";
    private const string EntryNotesSeparator = " // ";

    // 「名義 × 誤記」記法のセパレータ（出力は U+00D7 に統一）。Parser 側は U+00D7 / U+2715 のいずれも受け付ける。
    private const char MisprintSeparator = '×';

    /// <summary>クレジット全体（<see cref="DraftCredit"/> の Cards 全部）を一括入力フォーマット文字列に変換する。</summary>
    /// <param name="credit">対象のクレジット Draft。</param>
    /// <param name="cache">マスタ名解決用キャッシュ。</param>
    /// <param name="ct">キャンセル。</param>
    internal static async Task<string> EncodeFullAsync(
        DraftCredit credit, LookupCache cache, CancellationToken ct = default)
    {
        if (credit is null) throw new ArgumentNullException(nameof(credit));
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        var sb = new StringBuilder();
        // 削除マーク済みカードはエンコード対象外（ツリー表示と整合させる）。
        var liveCards = credit.Cards.Where(c => c.State != DraftState.Deleted).ToList();

        for (int i = 0; i < liveCards.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // 2 枚目以降のカードの前にカード区切りを置く（先頭カードは区切りなし）。
            if (i > 0)
            {
                sb.Append("----").Append(LineSeparator);
            }
            await EncodeCardBodyAsync(liveCards[i], cache, sb, isFirstCardInOutput: i == 0, ct);
        }

        return sb.ToString();
    }

    /// <summary>単一カードを一括入力フォーマット文字列に変換する（ReplaceScope モード用）。 出力先頭にカード区切り <c>----</c> は付かない（呼び出し側のコンテキストで決まるため）。</summary>
    internal static async Task<string> EncodeCardAsync(
        DraftCard card, LookupCache cache, CancellationToken ct = default)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        var sb = new StringBuilder();
        await EncodeCardBodyAsync(card, cache, sb, isFirstCardInOutput: true, ct);
        return sb.ToString();
    }

    /// <summary>単一 Tier を一括入力フォーマット文字列に変換する（ReplaceScope モード用）。 出力先頭に Tier 区切り <c>---</c> は付かない。</summary>
    internal static async Task<string> EncodeTierAsync(
        DraftTier tier, LookupCache cache, CancellationToken ct = default)
    {
        if (tier is null) throw new ArgumentNullException(nameof(tier));
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        var sb = new StringBuilder();
        await EncodeTierBodyAsync(tier, cache, sb, isFirstTierInOutput: true, ct);
        return sb.ToString();
    }

    /// <summary>単一 Group を一括入力フォーマット文字列に変換する（ReplaceScope モード用）。 出力先頭に Group 区切り <c>--</c> は付かない。</summary>
    internal static async Task<string> EncodeGroupAsync(
        DraftGroup group, LookupCache cache, CancellationToken ct = default)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        var sb = new StringBuilder();
        await EncodeGroupBodyAsync(group, cache, sb, isFirstGroupInOutput: true, ct);
        return sb.ToString();
    }

    /// <summary>単一の役職（Role）配下を一括入力フォーマット文字列に変換する（ReplaceScope モード用）。 役職開始行（<c>役職名:</c>）から出力される。</summary>
    internal static async Task<string> EncodeRoleAsync(
        DraftRole role, LookupCache cache, CancellationToken ct = default)
    {
        if (role is null) throw new ArgumentNullException(nameof(role));
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        var sb = new StringBuilder();
        await EncodeRoleBodyAsync(role, cache, sb, ct);
        return sb.ToString();
    }

    //  内部実装：階層別エンコーダ

    /// <summary>1 カード分の本体を出力する（区切り行 <c>----</c> は呼び出し側が前置する）。 カード備考 → Tier 群 の順に書き出す。</summary>
    private static async Task EncodeCardBodyAsync(
        DraftCard card, LookupCache cache, StringBuilder sb,
        bool isFirstCardInOutput, CancellationToken ct)
    {
        // カード備考（@notes=...）。空文字 / null の場合は出力省略（パーサも空値を null として扱う）。
        EmitNotesDirective(card.Entity.Notes, sb);

        // 削除マーク済み Tier はエンコード対象外。
        var liveTiers = card.Tiers.Where(t => t.State != DraftState.Deleted).ToList();

        for (int i = 0; i < liveTiers.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // 2 つ目以降の Tier の前にティア区切りを置く。
            if (i > 0)
            {
                sb.Append("---").Append(LineSeparator);
            }
            await EncodeTierBodyAsync(liveTiers[i], cache, sb, isFirstTierInOutput: i == 0, ct);
        }
    }

    /// <summary>1 Tier 分の本体を出力する。Tier 備考 → Group 群 の順。</summary>
    private static async Task EncodeTierBodyAsync(
        DraftTier tier, LookupCache cache, StringBuilder sb,
        bool isFirstTierInOutput, CancellationToken ct)
    {
        // Tier 備考。
        EmitNotesDirective(tier.Entity.Notes, sb);

        var liveGroups = tier.Groups.Where(g => g.State != DraftState.Deleted).ToList();

        for (int i = 0; i < liveGroups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                sb.Append("--").Append(LineSeparator);
            }
            await EncodeGroupBodyAsync(liveGroups[i], cache, sb, isFirstGroupInOutput: i == 0, ct);
        }
    }

    /// <summary>
    /// 1 Group 分の本体を出力する。Group 備考 → Role 群 の順。
    /// 役職と役職の間には空行を 1 行挟む。これは「人間が読みやすくする」ための整形であり、
    /// Parser 仕様上は <c>XXX:</c> 行で新役職開始は認識される。空行を 1 行挟むこと自体は
    /// Parser の挙動に影響しない（同役職内の暗黙ブロック区切り扱いになるが、直後が <c>XXX:</c>
    /// なので新役職に切り替わるため結果的に同等）。
    /// </summary>
    private static async Task EncodeGroupBodyAsync(
        DraftGroup group, LookupCache cache, StringBuilder sb,
        bool isFirstGroupInOutput, CancellationToken ct)
    {
        // Group 備考。
        EmitNotesDirective(group.Entity.Notes, sb);

        var liveRoles = group.Roles.Where(r => r.State != DraftState.Deleted).ToList();

        for (int ri = 0; ri < liveRoles.Count; ri++)
        {
            ct.ThrowIfCancellationRequested();
            // 2 つ目以降の役職の前に空行を 1 行入れる（読みやすさのため）。
            // Parser 側はこの空行を「同役職内ブロック区切り」と解釈するが、直後が
            // XXX: 役職開始行なので、結局新役職に切り替わる挙動になる。
            if (ri > 0)
            {
                sb.Append(LineSeparator);
            }
            await EncodeRoleBodyAsync(liveRoles[ri], cache, sb, ct);
        }
    }

    /// <summary>1 役職分の本体を出力する。 役職名行 → 役職備考 → ブロック群（2 つ目以降のブロックは <c>-</c> 行で明示的に区切る）の順。 役職内のブロック区切りは <c>-</c> 行（導入されたハイフン 1 個区切り）を使う。 空行はロール間の区切りと重なって紛らわしいため、 出力では役職内ブロック区切り＝ <c>-</c>、役職と役職の境目＝空行、と使い分ける。</summary>
    private static async Task EncodeRoleBodyAsync(
        DraftRole role, LookupCache cache, StringBuilder sb, CancellationToken ct)
    {
        // 役職表示名解決。マスタに無い場合は role_code をそのまま見出しに使う（ラウンドトリップ性は犠牲になるが、
        // 役職コードを示すことで運用者が手当てできるようにする）。
        string? nameJa = await cache.LookupRoleNameJaAsync(role.Entity.RoleCode);
        string headerName = !string.IsNullOrEmpty(nameJa)
            ? nameJa
            : role.Entity.RoleCode ?? "(自由記述)";

        // 所属表記レイアウトが PREFIX なら役職ヘッダ行末尾にインラインで @affil_layout=prefix を付ける。
        // 役職別行の @affil_layout=... ディレクティブも受け付けるが、ラウンドトリップではインライン形式に統一する
        // （短く、視覚的にも 1 行で完結するため）。
        bool isPrefix = string.Equals(role.Entity.AffiliationLayout, "PREFIX", StringComparison.Ordinal);
        if (isPrefix)
        {
            sb.Append(headerName).Append(": @affil_layout=prefix").Append(LineSeparator);
        }
        else
        {
            sb.Append(headerName).Append(':').Append(LineSeparator);
        }

        // 役職備考。役職開始行直後に @notes= があれば Role.Notes として復元される。
        EmitNotesDirective(role.Entity.Notes, sb);

        // 削除マーク済みブロックはエンコード対象外。
        var liveBlocks = role.Blocks.Where(b => b.State != DraftState.Deleted).ToList();

        for (int bi = 0; bi < liveBlocks.Count; bi++)
        {
            ct.ThrowIfCancellationRequested();
            // 2 つ目以降のブロックは "-" 行で明示的に区切る。
            if (bi > 0)
            {
                sb.Append('-').Append(LineSeparator);
            }
            await EncodeBlockBodyAsync(liveBlocks[bi], cache, sb, ct);
        }
    }

    /// <summary>1 ブロック分の本体を出力する。 出力順: <c>@cols=N</c>（必要時）→ ブロック備考 → <c>[[leading_company]]</c>（必要時）→ エントリ行群（タブ整列）。</summary>
    private static async Task EncodeBlockBodyAsync(
        DraftBlock block, LookupCache cache, StringBuilder sb, CancellationToken ct)
    {
        // @cols=N: ColCount が 2 以上のときに明示出力する（ColCount=1 は省略時のデフォルト）。
        // タブ数推測との二重表現を避けるため、出力時は常に明示する方針（曖昧性を排除）。
        // CreditRoleBlock.ColCount は byte 型なので、Math.Max(int, byte) のオーバーロード解決
        // 曖昧（Math.Max(byte,byte) と Math.Max(int,int) のどちらに合わせるか）を避けるため
        // 第 2 引数を int に明示キャストする。
        int colCount = Math.Max(1, (int)block.Entity.ColCount);
        if (colCount > 1)
        {
            sb.Append("@cols=").Append(colCount).Append(LineSeparator);
        }

        // ブロック備考。
        EmitNotesDirective(block.Entity.Notes, sb);

        // グループトップ屋号 [[XXX]]（leading_company_alias_id）。
        // 解決不能（マスタから消えた等）の場合は <unknown alias_id=N> 形式で残し、運用者が気付けるようにする。
        if (block.Entity.LeadingCompanyAliasId is int leadingId)
        {
            string? aliasName = await cache.LookupCompanyAliasNameAsync(leadingId);
            string nameOrFallback = !string.IsNullOrEmpty(aliasName)
                ? aliasName
                : $"alias#{leadingId}";
            sb.Append("[[").Append(nameOrFallback).Append("]]").Append(LineSeparator);
        }

        // エントリ群を ColCount で行に分割しながらタブ結合で出力する。
        var liveEntries = block.Entries.Where(e => e.State != DraftState.Deleted).ToList();
        if (liveEntries.Count == 0) return;

        // PREFIX レイアウトの場合は「屋号TAB名前」形式で 1 行 1 エントリ。
        // 屋号は affiliation_company_alias_id（マスタ）優先、無ければ affiliation_text（フリーテキスト）。
        // どちらも空なら屋号セル省略（タブも出さず、名前のみ）。
        bool isPrefix = string.Equals(block.Parent.Entity.AffiliationLayout, "PREFIX", StringComparison.Ordinal);
        if (isPrefix)
        {
            for (int ei = 0; ei < liveEntries.Count; ei++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = liveEntries[ei];
                string nameCell = await EncodeEntryAsCellAsync(
                    entry,
                    isParallelWithPrevious: IsParallelWithPrevious(liveEntries, ei),
                    cache, ct,
                    suppressAffiliation: true);

                string affilLabel = string.Empty;
                if (entry.Entity.AffiliationCompanyAliasId is int affAliasId)
                {
                    string? affName = await cache.LookupCompanyAliasNameAsync(affAliasId);
                    affilLabel = !string.IsNullOrEmpty(affName) ? affName : $"alias#{affAliasId}";
                }
                else if (!string.IsNullOrEmpty(entry.Entity.AffiliationText))
                {
                    affilLabel = entry.Entity.AffiliationText!;
                }

                if (affilLabel.Length > 0)
                {
                    sb.Append(affilLabel).Append('\t').Append(nameCell).Append(LineSeparator);
                }
                else
                {
                    sb.Append(nameCell).Append(LineSeparator);
                }
            }
            return;
        }

        // 各エントリをセル文字列にエンコードしてから ColCount 個ずつ集約。
        // affiliation_inline = false（所属を別行表示するフラグ）は ColCount=1 のときだけ尊重する：
        // 1 セル/行のレイアウトなら名前の次行に「(所属)」を独立出力すればきれいに並ぶ。
        // ColCount>1 のときはタブ整列が崩れるため、強制的にインライン表記にフォールバックする。
        bool canEmitAffiliationBelow = colCount == 1;
        var cells = new List<string>(liveEntries.Count);
        var affilBelowExprs = new List<string?>(liveEntries.Count);
        for (int ei = 0; ei < liveEntries.Count; ei++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = liveEntries[ei];
            bool wantBelow = canEmitAffiliationBelow
                             && !entry.Entity.AffiliationInline
                             && (entry.Entity.AffiliationCompanyAliasId.HasValue
                                 || !string.IsNullOrEmpty(entry.Entity.AffiliationText));
            string cell = await EncodeEntryAsCellAsync(
                entry,
                isParallelWithPrevious: IsParallelWithPrevious(liveEntries, ei),
                cache, ct,
                suppressAffiliation: wantBelow);
            cells.Add(cell);

            // 別行出力したい所属は、ResolveAffiliationStringAsync で得た「(...)」完全表現をそのまま使う。
            // suppressAffiliation でセルから取り除いた分を次行で補う形。
            if (wantBelow)
            {
                affilBelowExprs.Add(await ResolveAffiliationStringAsync(entry.Entity, cache));
            }
            else
            {
                affilBelowExprs.Add(null);
            }
        }

        // 行ごとに ColCount 個ずつまとめてタブで連結。
        // 最終行が ColCount 未満の場合は短いまま出力（パーサ側でタブ最大数より短い行も許容）。
        // 所属別行モード（ColCount=1 + AffiliationInline=false）のとき、各セル行の直後に「(所属)」行を出力する。
        for (int row = 0; row < cells.Count; row += colCount)
        {
            int take = Math.Min(colCount, cells.Count - row);
            sb.Append(string.Join('\t', cells.GetRange(row, take))).Append(LineSeparator);
            if (canEmitAffiliationBelow)
            {
                string? affExpr = affilBelowExprs[row];
                if (!string.IsNullOrEmpty(affExpr))
                {
                    sb.Append(affExpr).Append(LineSeparator);
                }
            }
        }
    }

    /// <summary>直前エントリと A/B 併記関係（parallel_with_entry_id）にあるかを判定する。</summary>
    private static bool IsParallelWithPrevious(List<DraftEntry> entries, int index)
    {
        if (index <= 0) return false;
        var cur = entries[index];
        var prev = entries[index - 1];

        // Draft の一時フラグが立っていれば true（未保存状態の入力ストリームに対応）。
        if (cur.RequestParallelWithPrevious) return true;

        // 永続済み（または既に解決済み）の場合は ParallelWithEntryId と prev.RealId/EntryId の照合。
        if (cur.Entity.ParallelWithEntryId is int linked)
        {
            int? prevId = prev.RealId ?? prev.Entity.EntryId;
            if (prevId is int p && p != 0 && p == linked) return true;
        }

        return false;
    }

    /// <summary>1 エントリを 1 セル文字列に変換する。 行頭プレフィクス（🎬 / & ）と行末サフィックス（ // 備考）を含む完全な単一セル表現を返す。
    /// <paramref name="suppressAffiliation"/> = true のとき、PERSON / CHARACTER_VOICE の所属表記 <c>(屋号)</c> 後置を抑止する
    /// （PREFIX レイアウト下では所属は屋号列として左に置くため、セル本体には含めない）。</summary>
    private static async Task<string> EncodeEntryAsCellAsync(
        DraftEntry entry, bool isParallelWithPrevious, LookupCache cache, CancellationToken ct,
        bool suppressAffiliation = false)
    {
        var e = entry.Entity;

        // ─── 種別ごとの本体生成 ───
        string body = e.EntryKind switch
        {
            "PERSON" => await BuildPersonCellAsync(e, cache, suppressAffiliation),
            "CHARACTER_VOICE" => await BuildCharacterVoiceCellAsync(e, cache, suppressAffiliation),
            "COMPANY" => await BuildCompanyCellAsync(e, cache),
            "LOGO" => await BuildLogoCellAsync(e, cache),
            "TEXT" => e.RawText ?? "",
            _ => $"<unknown_kind:{e.EntryKind}>",
        };

        // ─── 修飾子の前後付加 ───
        var sb = new StringBuilder();

        // 行頭プレフィクス: 🎬（本放送限定）→ &（併記継続）の順で連結。
        // 順序自体に意味は無いが、出力の一貫性のため固定順とする。
        if (e.IsBroadcastOnly)
        {
            sb.Append(BroadcastOnlyMarker).Append(' ');
        }
        if (isParallelWithPrevious)
        {
            sb.Append(ParallelContinuationMarker);
        }

        sb.Append(body);

        // 行末サフィックス: 備考。空文字は省略（パーサ仕様で空 notes は明示クリア扱いになるため、
        // 値が無い場合に余計な " // " を付けない）。
        if (!string.IsNullOrEmpty(e.Notes))
        {
            sb.Append(EntryNotesSeparator).Append(e.Notes);
        }

        return sb.ToString();
    }

    /// <summary>
    /// PERSON エントリの本体（人物名 + 任意の <c>#alias_id</c> + 任意の誤記 + 所属）を生成する。
    /// 誤記 (<see cref="CreditBlockEntry.PersonMisprintText"/>) が非 NULL なら「名義×誤記」記法で連結。
    /// DB に同名 alias が複数存在するときのみ「<c>名前#alias_id</c>」記法で alias_id を明示し、
    /// ラウンドトリップで意図しない別 alias への統合を防ぐ（同名 1 件のみなら無印）。
    /// </summary>
    private static async Task<string> BuildPersonCellAsync(CreditBlockEntry e, LookupCache cache, bool suppressAffiliation)
    {
        string name = "";
        if (e.PersonAliasId is int paId)
        {
            name = await cache.LookupPersonAliasNameAsync(paId)
                ?? $"alias#{paId}";
        }

        string nameWithAliasId = await AppendAliasIdSuffixIfAmbiguousPersonAsync(name, e.PersonAliasId, cache);

        // 誤記は所属より内側、人物名の直後に連結する（パーサの SplitAffiliation が末尾の (...) を先に剥がすため、
        // × は「人物名」フィールドの中で完結する必要がある）。
        string nameWithMisprint = AppendMisprintSuffix(nameWithAliasId, e.PersonMisprintText);

        if (suppressAffiliation) return nameWithMisprint;
        // ResolveAffiliationStringAsync は括弧込みの完全な「(...)」文字列を返す（または null）。
        string? affExpr = await ResolveAffiliationStringAsync(e, cache);
        return string.IsNullOrEmpty(affExpr) ? nameWithMisprint : $"{nameWithMisprint}{affExpr}";
    }

    /// <summary>
    /// CHARACTER_VOICE エントリの本体（<c>&lt;キャラ&gt;声優</c> + 所属）を生成する。
    /// キャラ名は character_alias_id 経由で解決するが、マスタ未引き（<see cref="CreditBlockEntry.RawCharacterText"/>
    /// に退避されている）場合は raw を使う。
    /// DB に同名キャラ alias / 同名声優 alias が複数存在するときのみ「<c>名前#alias_id</c>」記法で
    /// alias_id を明示し、ラウンドトリップで意図しない別 alias への統合を防ぐ（同名 1 件のみなら無印）。
    /// </summary>
    private static async Task<string> BuildCharacterVoiceCellAsync(CreditBlockEntry e, LookupCache cache, bool suppressAffiliation)
    {
        string charaName = "";
        if (e.CharacterAliasId is int caId)
        {
            charaName = await cache.LookupCharacterAliasNameAsync(caId)
                ?? $"alias#{caId}";
        }
        else if (!string.IsNullOrEmpty(e.RawCharacterText))
        {
            charaName = e.RawCharacterText;
        }

        string charaWithAliasId = await AppendAliasIdSuffixIfAmbiguousCharacterAsync(charaName, e.CharacterAliasId, cache);

        string actorName = "";
        if (e.PersonAliasId is int paId)
        {
            actorName = await cache.LookupPersonAliasNameAsync(paId)
                ?? $"alias#{paId}";
        }

        string actorWithAliasId = await AppendAliasIdSuffixIfAmbiguousPersonAsync(actorName, e.PersonAliasId, cache);

        // キャラ側・声優側それぞれに誤記が立っていれば「×」で連結する。
        // 例: <キュアブラック×キュアブラッグ>菊池 心×菊地 心(東映アニメーション)
        string charaWithMisprint = AppendMisprintSuffix(charaWithAliasId, e.CharacterMisprintText);
        string actorWithMisprint = AppendMisprintSuffix(actorWithAliasId, e.PersonMisprintText);

        string actorPart;
        if (suppressAffiliation)
        {
            actorPart = actorWithMisprint;
        }
        else
        {
            // ResolveAffiliationStringAsync は括弧込みの完全な「(...)」文字列を返す（または null）。
            string? affExpr = await ResolveAffiliationStringAsync(e, cache);
            actorPart = string.IsNullOrEmpty(affExpr) ? actorWithMisprint : $"{actorWithMisprint}{affExpr}";
        }
        return $"<{charaWithMisprint}>{actorPart}";
    }

    /// <summary>COMPANY エントリの本体（<c>[屋号]</c> または <c>[屋号#alias_id]</c>、任意で <c>×誤記</c> を後置）を生成する。 DB に同名屋号 alias が複数存在するときのみ <c>#alias_id</c> を後置する（同名 1 件のみなら無印）。</summary>
    private static async Task<string> BuildCompanyCellAsync(CreditBlockEntry e, LookupCache cache)
    {
        string body;
        if (e.CompanyAliasId is int caId)
        {
            string? name = await cache.LookupCompanyAliasNameAsync(caId);
            string nameWithAliasId = await AppendAliasIdSuffixIfAmbiguousCompanyAsync(name ?? $"alias#{caId}", caId, cache);
            body = $"[{nameWithAliasId}]";
        }
        else
        {
            body = "[]";
        }
        // 屋号誤記は ] の外側に "×誤記" として連結する（COMPANY 例: "[タバック]×タボック"）。
        return AppendMisprintSuffix(body, e.CompanyMisprintText);
    }

    /// <summary>LOGO エントリの本体（<c>[屋号#CIバージョン]</c> または <c>[屋号#alias_id#CIバージョン]</c>、 任意で <c>×誤記</c> を後置）を生成する。 LOGO は CI バージョンで logos マスタを分離しているため通常は屋号同名でも CI バージョンで区別できるが、 ロゴエントリが参照する company_alias 側で同名衝突がある場合に備えて <c>#alias_id</c> を後置する。</summary>
    private static async Task<string> BuildLogoCellAsync(CreditBlockEntry e, LookupCache cache)
    {
        if (e.LogoId is not int lgId) return AppendMisprintSuffix("[]", e.CompanyMisprintText);

        var components = await cache.LookupLogoComponentsAsync(lgId);
        string body;
        if (components is null)
        {
            body = $"[logo#{lgId}]";
        }
        else
        {
            string companyName = components.Value.CompanyAliasName;
            string companyWithAliasId = await AppendAliasIdSuffixIfAmbiguousCompanyAsync(companyName, components.Value.CompanyAliasId, cache);
            body = $"[{companyWithAliasId}#{components.Value.CiVersionLabel}]";
        }

        // 屋号誤記は ] の外側に "×誤記" として連結する（CI バージョン側は誤記対象外）。
        return AppendMisprintSuffix(body, e.CompanyMisprintText);
    }

    /// <summary>DB に同名 person_alias が複数存在する場合のみ「<c>名前#alias_id</c>」形式の alias_id 明示記法を後置する。 名前が空 or alias_id が不明 / 負数 Pending のときは何もせず元名を返す。</summary>
    private static async Task<string> AppendAliasIdSuffixIfAmbiguousPersonAsync(string name, int? aliasId, LookupCache cache)
    {
        if (string.IsNullOrEmpty(name) || aliasId is not int id || id <= 0) return name;
        int sameCount = await cache.GetSameNamePersonAliasCountAsync(name);
        return sameCount >= 2 ? $"{name}#{id}" : name;
    }

    /// <summary>DB に同名 character_alias が複数存在する場合のみ「<c>名前#alias_id</c>」形式の alias_id 明示記法を後置する。</summary>
    private static async Task<string> AppendAliasIdSuffixIfAmbiguousCharacterAsync(string name, int? aliasId, LookupCache cache)
    {
        if (string.IsNullOrEmpty(name) || aliasId is not int id || id <= 0) return name;
        int sameCount = await cache.GetSameNameCharacterAliasCountAsync(name);
        return sameCount >= 2 ? $"{name}#{id}" : name;
    }

    /// <summary>DB に同名 company_alias が複数存在する場合のみ「<c>名前#alias_id</c>」形式の alias_id 明示記法を後置する。</summary>
    private static async Task<string> AppendAliasIdSuffixIfAmbiguousCompanyAsync(string name, int? aliasId, LookupCache cache)
    {
        if (string.IsNullOrEmpty(name) || aliasId is not int id || id <= 0) return name;
        int sameCount = await cache.GetSameNameCompanyAliasCountAsync(name);
        return sameCount >= 2 ? $"{name}#{id}" : name;
    }

    /// <summary>名義テキストに「×誤記」記法を後置する補助メソッド。 <paramref name="misprint"/> が null / 空の場合は <paramref name="mainText"/> をそのまま返す。</summary>
    private static string AppendMisprintSuffix(string mainText, string? misprint)
    {
        if (string.IsNullOrEmpty(misprint)) return mainText;
        return $"{mainText}{MisprintSeparator}{misprint}";
    }

    /// <summary>所属表記を「人物名末尾に追記する括弧付き文字列」として解決する。
    /// DB 状態 (<see cref="CreditBlockEntry.AffiliationCompanyAliasId"/> / <see cref="CreditBlockEntry.AffiliationText"/>) を
    /// パーサ側の 4 パターン記法に逆翻訳する：
    /// <list type="bullet">
    ///   <item>両方 null → null（呼び出し側は所属を付けない）</item>
    ///   <item>ID 屋号のみ（alias_id あり、text なし） → <c>(屋号名)</c></item>
    ///   <item>強制テキストのみ（alias_id なし、text あり） → <c>("テキスト")</c></item>
    ///   <item>両持ち（alias_id あり、text あり） → <c>(屋号名 / "テキスト")</c></item>
    /// </list>
    /// 戻り値はそのまま <c>名前 + これ</c> で連結可能（先頭括弧を含む）。</summary>
    private static async Task<string?> ResolveAffiliationStringAsync(CreditBlockEntry e, LookupCache cache)
    {
        bool hasAlias = e.AffiliationCompanyAliasId.HasValue;
        bool hasText = !string.IsNullOrEmpty(e.AffiliationText);

        if (!hasAlias && !hasText) return null;

        string? aliasName = null;
        if (hasAlias)
        {
            aliasName = await cache.LookupCompanyAliasNameAsync(e.AffiliationCompanyAliasId!.Value)
                        ?? $"alias#{e.AffiliationCompanyAliasId.Value}";
        }

        if (hasAlias && hasText)
        {
            // 両持ち: (屋号名 / "テキスト")
            return $"({aliasName} / \"{e.AffiliationText}\")";
        }
        if (hasAlias)
        {
            // ID のみ: (屋号名)
            return $"({aliasName})";
        }
        // 強制テキストのみ: ("テキスト")
        return $"(\"{e.AffiliationText}\")";
    }

    /// <summary><c>@notes=値</c> ディレクティブ行を出力する補助メソッド。 <paramref name="notes"/> が null / 空文字の場合は何も出力しない（パーサ仕様で 「<c>@notes=</c> 自体は空値クリア指示」だが、Encoder 側では「Notes が無い = 行を出さない」運用とする ことで、未指定状態と明示クリア状態を見た目で区別しやすくする）。</summary>
    private static void EmitNotesDirective(string? notes, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(notes)) return;
        sb.Append("@notes=").Append(notes).Append(LineSeparator);
    }
}
