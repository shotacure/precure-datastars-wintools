namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集セッション全体を保持するルートクラス（v1.2.0 工程 H-8 で導入）。
/// <para>
/// クレジット編集画面（<c>CreditEditorForm</c>）が「即時 DB 反映」から「メモリ上で編集 → 保存ボタンで一括確定」
/// 方式に移行するための中核データ構造。クレジットを 1 件選択するごとに 1 つの <see cref="CreditDraftSession"/>
/// インスタンスが作られ、配下の全階層（カード／Tier／グループ／役職／ブロック／エントリ）が
/// メモリ上の Draft オブジェクトとして展開される。
/// </para>
/// <para>
/// 主な責務:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Root"/>: 編集中のクレジット本体（DraftCredit）。</description></item>
///   <item><description><see cref="AllocateTempId"/>: メモリ上の新規行用に -1, -2, -3, ... と一意 ID を払い出す。</description></item>
///   <item><description>削除済み Draft の蓄積: 既存行を削除した場合、Draft をリストから取り除くと同時に
///     本セッションの <see cref="DeletedCards"/> 等のバケットに退避する。これは保存時に DB DELETE を発行するために必要。
///     Added だった Draft が削除された場合は DB に行が無いので退避しない（捨てるだけ）。</description></item>
///   <item><description><see cref="HasUnsavedChanges"/>: 保存待ちの変更があるかを判定（クレジット切替 / フォーム閉じる時の確認用）。</description></item>
/// </list>
/// </summary>
public sealed class CreditDraftSession
{
    /// <summary>編集中のクレジット本体。</summary>
    public DraftCredit Root { get; init; } = null!;

    /// <summary>
    /// 次に払い出す Temp ID の符号付き値。負数で <c>-1, -2, -3, ...</c> と進む。
    /// メモリ上の新規行のみが Temp ID を持ち、保存時に DB の自動採番で実 ID に置き換わる。
    /// </summary>
    private int _nextTempId = -1;

    /// <summary>新しい Temp ID を 1 つ払い出して返す。</summary>
    public int AllocateTempId()
    {
        int id = _nextTempId;
        _nextTempId--;
        return id;
    }

    // ─── 削除済み Draft の蓄積 ───
    // 既存行（RealId あり）を削除した場合、ツリーから取り除いて以下のバケットに退避する。
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

    /// <summary>
    /// 保存待ちの変更があるか判定する。
    /// (a) ルートの State が Modified 以上、
    /// (b) 配下のいずれかの Draft が Modified / Added、
    /// (c) いずれかの Deleted バケットに既存行が積まれている、
    /// のいずれかが成立すれば true。
    /// </summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (Root.State != DraftState.Unchanged) return true;
            if (DeletedCards.Count > 0 || DeletedTiers.Count > 0 || DeletedGroups.Count > 0
                || DeletedRoles.Count > 0 || DeletedBlocks.Count > 0 || DeletedEntries.Count > 0)
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
