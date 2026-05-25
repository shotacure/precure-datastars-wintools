namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集セッション全体を保持するルートクラス（導入）。
/// クレジット編集画面（<c>CreditEditorForm</c>）が「即時 DB 反映」から「メモリ上で編集 → 保存ボタンで一括確定」
/// 方式に移行するための中核データ構造。クレジットを 1 件選択するごとに 1 つの <see cref="CreditDraftSession"/>
/// インスタンスが作られ、配下の全階層（カード／Tier／グループ／役職／ブロック／エントリ）が
/// メモリ上の Draft オブジェクトとして展開される。
/// 主な責務:
/// <list type="bullet">
///   <item><description><see cref="Root"/>: 編集中のクレジット本体（DraftCredit）。</description></item>
///   <item><description><see cref="AllocateTempId"/>: メモリ上の新規行用に -1, -2, -3, ... と一意 ID を払い出す。</description></item>
///   <item><description>削除済み Draft の蓄積: 既存行を削除する際は、Draft をリストから取り除くと同時に
///     本セッションの <see cref="DeletedCards"/> 等のバケットに退避する。これは保存時に DB DELETE を発行するために必要。
///     Added だった Draft が削除された場合は DB に行が無いので退避しない（捨てるだけ）。</description></item>
///   <item><description><see cref="HasUnsavedChanges"/>: 保存待ちの変更があるかを判定（クレジット切替 / フォーム閉じる時の確認用）。</description></item>
/// </list>
/// </summary>
public sealed class CreditDraftSession
{
    /// <summary>編集中のクレジット本体。</summary>
    public DraftCredit Root { get; init; } = null!;

    /// <summary>次に払い出す Temp ID の符号付き値。負数で <c>-1, -2, -3, ...</c> と進む。 メモリ上の新規行のみが Temp ID を持ち、保存時に DB の自動採番で実 ID に置き換わる。</summary>
    private int _nextTempId = -1;

    /// <summary>新しい Temp ID を 1 つ払い出して返す。</summary>
    public int AllocateTempId()
    {
        int id = _nextTempId;
        _nextTempId--;
        return id;
    }

    // ─── 削除済み Draft の蓄積 ───
    // 既存行（RealId あり）を削除する際は、ツリーから取り除いて以下のバケットに退避する。
    // 保存時にこれらを DELETE 文で DB から削除する。
    // 順序: 深い階層から削除する必要があるため、保存ロジックは Entries → Blocks → Roles → Groups → Tiers → Cards の順で処理する。

    /// <summary>削除マーク済みのカード（既存行 = RealId 値あり、のもの）。</summary>
    public List<DraftCard> DeletedCards { get; } = new();

    /// <summary>削除マーク済みの Tier。</summary>
    public List<DraftTier> DeletedTiers { get; } = new();

    /// <summary>削除マーク済みのグループ。</summary>
    public List<DraftGroup> DeletedGroups { get; } = new();

    /// <summary>削除マーク済みの役職。</summary>
    public List<DraftRole> DeletedRoles { get; } = new();

    /// <summary>削除マーク済みのブロック。</summary>
    public List<DraftBlock> DeletedBlocks { get; } = new();

    /// <summary>削除マーク済みのエントリ。</summary>
    public List<DraftEntry> DeletedEntries { get; } = new();

    // ─── ペンディング・マスタ投入（ステージB 導入） ───
    // 「クレジット一括入力 Apply」「+ 新規...」などでマスタ未ヒットだった名義・ロゴを、
    // 保存ボタンを押すまで DB に投入せず Draft 内に保留するためのバケット群。
    // キーは AllocateTempId() で払い出した負数 ID で、対応する DraftEntry.Entity.*AliasId 等にも
    // その負数が入る。保存時に CreditSaveService Phase 0 が
    //   1) Person/Character/Company を実 INSERT して負数 → 実 ID の置換マップを構築
    //   2) Logo の company_alias_id が負数なら 1) のマップで実 ID に置換 → Logo INSERT
    //   3) Draft 全 Block/Entry の負数 ID 列を実 ID に置換
    // の順序で消化する。

    /// <summary>保存時に person_aliases（必要なら persons + person_alias_persons も）へ投入予定の人物名義。
    /// キーは <see cref="PendingPersonAlias.TempAliasId"/>（負数）。</summary>
    public Dictionary<int, PendingPersonAlias> PendingPersonAliases { get; } = new();

    /// <summary>保存時に character_aliases（必要なら characters も）へ投入予定のキャラ名義。</summary>
    public Dictionary<int, PendingCharacterAlias> PendingCharacterAliases { get; } = new();

    /// <summary>保存時に company_aliases（必要なら companies も）へ投入予定の企業屋号。</summary>
    public Dictionary<int, PendingCompanyAlias> PendingCompanyAliases { get; } = new();

    /// <summary>保存時に logos へ投入予定のロゴ。<c>CompanyAliasId</c> が負数なら同セッションの
    /// PendingCompanyAliases を参照するため、Phase 0 内で先に CompanyAlias を解決してから Logo を INSERT する。</summary>
    public Dictionary<int, PendingLogo> PendingLogos { get; } = new();

    /// <summary>保存待ちの変更があるか判定する。</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (Root.State != DraftState.Unchanged) return true;
            if (DeletedCards.Count > 0 || DeletedTiers.Count > 0 || DeletedGroups.Count > 0
                || DeletedRoles.Count > 0 || DeletedBlocks.Count > 0 || DeletedEntries.Count > 0)
                return true;
            // ペンディング・マスタ投入が積まれている場合も「未保存」扱い。
            if (PendingPersonAliases.Count > 0 || PendingCharacterAliases.Count > 0
                || PendingCompanyAliases.Count > 0 || PendingLogos.Count > 0)
                return true;
            // 配下を再帰的に走査
            foreach (var card in Root.Cards)
            {
                if (card.State != DraftState.Unchanged) return true;
                foreach (var tier in card.Tiers)
                {
                    if (tier.State != DraftState.Unchanged) return true;
                    foreach (var group in tier.Groups)
                    {
                        if (group.State != DraftState.Unchanged) return true;
                        foreach (var role in group.Roles)
                        {
                            if (role.State != DraftState.Unchanged) return true;
                            foreach (var block in role.Blocks)
                            {
                                if (block.State != DraftState.Unchanged) return true;
                                foreach (var entry in block.Entries)
                                {
                                    if (entry.State != DraftState.Unchanged) return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
