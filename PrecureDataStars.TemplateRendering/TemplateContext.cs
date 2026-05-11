using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 役職テンプレ展開時のコンテキスト（v1.2.0 工程 H 追加）。
/// <para>
/// 1 つの役職をテンプレ展開する際に必要な情報をまとめて持ち運ぶための DTO。
/// レンダラ（<see cref="RoleTemplateRenderer"/>）は本コンテキストを受け取り、
/// AST ノードを順に走査しながらプレースホルダや BLOCKS ループを解決する。
/// </para>
/// </summary>
public sealed class TemplateContext
{
    /// <summary>役職コード（ロールタイプ判別に使用、例：<c>SERIALIZED_IN</c>、<c>THEME_SONGS</c>）。</summary>
    public string RoleCode { get; }

    /// <summary>役職表示名（<c>{ROLE_NAME}</c> プレースホルダの値）。</summary>
    public string RoleName { get; }

    /// <summary>
    /// 役職配下のブロック列。<c>{#BLOCKS}</c> ループはこの順序で展開される。
    /// 各ブロックは <see cref="BlockSnapshot"/> として「ブロック行 + 同ブロック配下のエントリ群」を保持。
    /// </summary>
    public IReadOnlyList<BlockSnapshot> Blocks { get; }

    /// <summary>クレジットのスコープ（シリーズ単位 or エピソード単位）。主題歌役職で <see cref="ScopeEpisodeId"/> 解決に使う。</summary>
    public string ScopeKind { get; }

    /// <summary>scope_kind=EPISODE のときのエピソード ID（無ければ null）。<c>{THEME_SONGS}</c> 解決で使う。</summary>
    public int? ScopeEpisodeId { get; }

    /// <summary>クレジットの種別（OP/ED/...）。<c>{THEME_SONGS}</c> の絞り込みに使う候補（v1.2.0 では未使用）。</summary>
    public string CreditKind { get; }

    /// <summary>
    /// 同じ Group 配下の sibling 役職の <see cref="BlockSnapshot"/> 群を引くコールバック（v1.3.0 stage 19 追加）。
    /// <para>
    /// <c>{ROLE:CODE.PLACEHOLDER}</c> 構文の解決時に、現在の役職が属する Group 内で
    /// <paramref name="role_code"/> に一致する別役職を検索し、その役職配下の <see cref="BlockSnapshot"/>
    /// 群を返す。見つからなければ null を返す。null を返した場合、<c>{ROLE:…}</c> プレースホルダは空文字に展開される。
    /// </para>
    /// <para>
    /// レンダラ側（<c>TemplateRendering</c> プロジェクト）は Group / Role の DB 取得経路に依存しないため、
    /// 呼び出し側（Catalog の <c>CreditPreviewRenderer</c> や SiteBuilder の <c>CreditTreeRenderer</c>）が
    /// Group ループ内で各役職の BlockSnapshot を辞書化し、そこから引くクロージャを供給する設計。
    /// </para>
    /// <para>
    /// このプロパティが null の場合（旧来の呼び出し経路）は <c>{ROLE:…}</c> 構文がすべて空文字で評価される。
    /// 後方互換のため必須プロパティにはしていない。
    /// </para>
    /// </summary>
    public Func<string, IReadOnlyList<BlockSnapshot>?>? SiblingRoleResolver { get; }

    /// <summary>
    /// 訪問済み role_code セット（v1.3.0 stage 19 追加）。
    /// <c>{ROLE:CODE.PLACEHOLDER}</c> 経由の再帰評価を 1 段で打ち止めにするため、現在の評価コンテキストで
    /// すでに ROLE 参照経由で展開中の role_code を保持する。レンダラ側で <c>{ROLE:X.Y}</c> を展開する際に
    /// <c>X</c> がこのセットに含まれていれば空文字を返す（無限ループ防止）。
    /// 通常のテンプレ評価では空セット（仕様上「ROLE 参照は再帰しない」）。
    /// </summary>
    public IReadOnlySet<string> VisitedRoleCodes { get; }

    public TemplateContext(
        string roleCode,
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        string scopeKind,
        int? scopeEpisodeId,
        string creditKind)
        : this(roleCode, roleName, blocks, scopeKind, scopeEpisodeId, creditKind,
               siblingRoleResolver: null, visitedRoleCodes: null)
    {
    }

    /// <summary>
    /// v1.3.0 stage 19 追加コンストラクタ：sibling-role 解決のためのコールバックと訪問済みセットを受け取る版。
    /// 既存呼び出し（<c>siblingRoleResolver</c> なし）はそのまま、新仕様の呼び出し元はこちらを使う。
    /// </summary>
    public TemplateContext(
        string roleCode,
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        string scopeKind,
        int? scopeEpisodeId,
        string creditKind,
        Func<string, IReadOnlyList<BlockSnapshot>?>? siblingRoleResolver,
        IReadOnlySet<string>? visitedRoleCodes)
    {
        RoleCode = roleCode ?? "";
        RoleName = roleName ?? "";
        Blocks = blocks ?? Array.Empty<BlockSnapshot>();
        ScopeKind = scopeKind ?? "";
        ScopeEpisodeId = scopeEpisodeId;
        CreditKind = creditKind ?? "";
        SiblingRoleResolver = siblingRoleResolver;
        VisitedRoleCodes = visitedRoleCodes ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 自身を雛形に「sibling role を仮想的に現在のスコープにする」派生コンテキストを生成する（v1.3.0 stage 19 追加）。
    /// <c>{ROLE:CODE.PLACEHOLDER}</c> 評価時にレンダラが内部的に呼び、<paramref name="targetRoleCode"/> 配下の
    /// blocks を <see cref="Blocks"/> に差し替えた一時コンテキストを作る。VisitedRoleCodes には
    /// <paramref name="targetRoleCode"/> が追加される（再帰 ROLE 参照を打ち止めるため）。
    /// </summary>
    public TemplateContext WithSiblingRoleScope(string targetRoleCode, IReadOnlyList<BlockSnapshot> targetBlocks)
    {
        var visited = new HashSet<string>(VisitedRoleCodes, StringComparer.Ordinal);
        visited.Add(targetRoleCode);
        return new TemplateContext(
            roleCode: targetRoleCode,
            roleName: RoleName, // 表示名は引き継がない（{ROLE_NAME} は外側コンテキストの値が使われる想定だが、
                                 // sibling スコープでは呼ぶ機会が稀。明示的に必要なら後で拡張）
            blocks: targetBlocks,
            scopeKind: ScopeKind,
            scopeEpisodeId: ScopeEpisodeId,
            creditKind: CreditKind,
            siblingRoleResolver: SiblingRoleResolver,
            visitedRoleCodes: visited);
    }
}

/// <summary>
/// 1 ブロック分のスナップショット（v1.2.0 工程 H 追加）。
/// <see cref="Block"/> はブロック行そのもの、<see cref="Entries"/> はエントリ群（<see cref="CreditBlockEntry.EntrySeq"/> 昇順）。
/// </summary>
public sealed class BlockSnapshot
{
    public CreditRoleBlock Block { get; }
    public IReadOnlyList<CreditBlockEntry> Entries { get; }

    public BlockSnapshot(CreditRoleBlock block, IReadOnlyList<CreditBlockEntry> entries)
    {
        Block = block;
        Entries = entries ?? Array.Empty<CreditBlockEntry>();
    }
}