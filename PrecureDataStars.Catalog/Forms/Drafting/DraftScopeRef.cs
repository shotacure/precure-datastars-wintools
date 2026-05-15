namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// 一括入力ダイアログの ReplaceScope モードで「どのスコープを置換対象にするか」を指す
/// 参照型。
/// <para>
/// クレジット編集画面のツリー右クリックメニュー「📝 一括入力で編集...」から本ダイアログを起動する際、
/// 選択されているノードに応じて以下のいずれかの粒度を指定する:
/// <list type="bullet">
///   <item><description><see cref="ScopeKind.Credit"/>: クレジット全体（Cards 全部）を置換</description></item>
///   <item><description><see cref="ScopeKind.Card"/>: 1 カードの内部（Tiers / Groups / Roles / Blocks / Entries）を置換、Card 自体は維持</description></item>
///   <item><description><see cref="ScopeKind.Tier"/>: 1 Tier の内部（Groups / Roles 以下）を置換、Tier 自体は維持</description></item>
///   <item><description><see cref="ScopeKind.Group"/>: 1 Group の内部（Roles 以下）を置換、Group 自体は維持</description></item>
///   <item><description><see cref="ScopeKind.Role"/>: 1 Role の内部（Blocks / Entries）を置換、Role 自体（役職コード）は維持</description></item>
/// </list>
/// </para>
/// <para>
/// 各スコープ種別では <see cref="Credit"/> / <see cref="Card"/> / <see cref="Tier"/> / <see cref="Group"/> /
/// <see cref="Role"/> のうち、対応する 1 つだけが非 null になる。<see cref="ScopeKind.Credit"/> の場合は
/// <see cref="Credit"/> が必須で他は null。<see cref="ScopeKind.Card"/> の場合は <see cref="Card"/> 必須で他は null、というように。
/// </para>
/// </summary>
public sealed class DraftScopeRef
{
    /// <summary>このスコープが指す粒度の種別。</summary>
    public ScopeKind Kind { get; init; }

    /// <summary>
    /// クレジット全体を指す場合の参照。
    /// <see cref="Kind"/> = <see cref="ScopeKind.Credit"/> のときのみ非 null。
    /// </summary>
    public DraftCredit? Credit { get; init; }

    /// <summary>
    /// カード単位を指す場合の参照。
    /// <see cref="Kind"/> = <see cref="ScopeKind.Card"/> のときのみ非 null。
    /// </summary>
    public DraftCard? Card { get; init; }

    /// <summary>
    /// Tier 単位を指す場合の参照。
    /// <see cref="Kind"/> = <see cref="ScopeKind.Tier"/> のときのみ非 null。
    /// </summary>
    public DraftTier? Tier { get; init; }

    /// <summary>
    /// Group 単位を指す場合の参照。
    /// <see cref="Kind"/> = <see cref="ScopeKind.Group"/> のときのみ非 null。
    /// </summary>
    public DraftGroup? Group { get; init; }

    /// <summary>
    /// Role 単位を指す場合の参照。
    /// <see cref="Kind"/> = <see cref="ScopeKind.Role"/> のときのみ非 null。
    /// </summary>
    public DraftRole? Role { get; init; }

    /// <summary>クレジット全体スコープを生成するファクトリ。</summary>
    public static DraftScopeRef ForCredit(DraftCredit credit)
        => new() { Kind = ScopeKind.Credit, Credit = credit ?? throw new ArgumentNullException(nameof(credit)) };

    /// <summary>カードスコープを生成するファクトリ。</summary>
    public static DraftScopeRef ForCard(DraftCard card)
        => new() { Kind = ScopeKind.Card, Card = card ?? throw new ArgumentNullException(nameof(card)) };

    /// <summary>Tier スコープを生成するファクトリ。</summary>
    public static DraftScopeRef ForTier(DraftTier tier)
        => new() { Kind = ScopeKind.Tier, Tier = tier ?? throw new ArgumentNullException(nameof(tier)) };

    /// <summary>Group スコープを生成するファクトリ。</summary>
    public static DraftScopeRef ForGroup(DraftGroup group)
        => new() { Kind = ScopeKind.Group, Group = group ?? throw new ArgumentNullException(nameof(group)) };

    /// <summary>Role スコープを生成するファクトリ。</summary>
    public static DraftScopeRef ForRole(DraftRole role)
        => new() { Kind = ScopeKind.Role, Role = role ?? throw new ArgumentNullException(nameof(role)) };

    /// <summary>
    /// このスコープを表す日本語のラベル（ダイアログ上部表示用）。
    /// </summary>
    public string GetDisplayLabel()
    {
        return Kind switch
        {
            ScopeKind.Credit => "クレジット全体",
            ScopeKind.Card => "カード",
            ScopeKind.Tier => "ティア",
            ScopeKind.Group => "グループ",
            ScopeKind.Role => "役職",
            _ => "(不明)",
        };
    }
}

/// <summary>
/// <see cref="DraftScopeRef"/> が指す粒度。
/// </summary>
public enum ScopeKind
{
    /// <summary>クレジット全体。</summary>
    Credit,
    /// <summary>1 カード分。</summary>
    Card,
    /// <summary>1 Tier 分。</summary>
    Tier,
    /// <summary>1 Group 分。</summary>
    Group,
    /// <summary>1 役職分。</summary>
    Role,
}