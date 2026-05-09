
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

    public TemplateContext(
        string roleCode,
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        string scopeKind,
        int? scopeEpisodeId,
        string creditKind)
    {
        RoleCode = roleCode ?? "";
        RoleName = roleName ?? "";
        Blocks = blocks ?? Array.Empty<BlockSnapshot>();
        ScopeKind = scopeKind ?? "";
        ScopeEpisodeId = scopeEpisodeId;
        CreditKind = creditKind ?? "";
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
