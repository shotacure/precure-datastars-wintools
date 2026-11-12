namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// エピソード単位で抽出した「各話のスタッフの札」のサマリ。
/// <c>SeriesGenerator</c> と <c>/episodes/</c> ランディングページ生成
/// （<see cref="Generators.EpisodesIndexGenerator"/>）・トップページ生成（<see cref="Generators.HomeGenerator"/>）から共有する独立公開クラス。
/// この型は
/// 両者で共有するため本ファイルに外出ししている。
/// 札は作品ごとの表（<c>series_episode_staff_roles</c>）の行のある役職だけを、行の並び順・行の色で持つ
/// （組み立ては <see cref="Pipeline.EpisodeStaffCards.BuildBadges"/>。エピソード詳細のスタッフ欄と共通）。
/// 担当者の文字列は「、」で連結済みの HTML 断片を保持する。PERSON エントリは
/// <see cref="StaffNameLinkResolver"/> によって <c>&lt;a href="/people/{名前}/"&gt;表示名&lt;/a&gt;</c>
/// 形式にラップ済み、TEXT エントリは HTML エスケープ済み。テンプレ側ではこれを <c>html.escape</c>
/// を掛けずにそのまま出力する。
/// 抽出（<c>ExtractStaffSummary</c>）と PERSON エントリ解決（<c>ResolveStaffEntry</c>）
/// は <c>SeriesGenerator</c> に置く。<c>SeriesGenerator</c> は本クラスのインスタンスを
/// <c>Dictionary&lt;int, EpisodeStaffSummary&gt;</c> で memoize し、公開メソッド
/// <c>GetEpisodeStaffSummaries()</c> 経由でパイプライン後段（<c>EpisodesIndexGenerator</c>・<c>HomeGenerator</c>）に渡す。
/// </summary>
public sealed class EpisodeStaffSummary
{
    /// <summary>その回の札（表の並び順。担当者のいない役職は入らない）。空なら一覧のスタッフ列ごと描画しない。</summary>
    public IReadOnlyList<EpisodeStaffBadge> Badges { get; set; } = Array.Empty<EpisodeStaffBadge>();
}

/// <summary>
/// 各話のスタッフの札 1 つ（役職の札と担当者の HTML 断片）。
/// ふつうは役職 1 つ。絵コンテと演出が同じ顔ぶれの回は、2 つの役職（絵コンテ・演出の順）を 1 つにまとめ、名前は 1 回だけ持つ。
/// </summary>
public sealed class EpisodeStaffBadge
{
    /// <summary>札に並べる役職（ふつうは 1 つ。絵コンテと演出をまとめた札は 2 つ）。</summary>
    public IReadOnlyList<EpisodeStaffBadgeRole> Roles { get; set; } = Array.Empty<EpisodeStaffBadgeRole>();

    /// <summary>担当者の HTML 断片（PERSON は <c>&lt;a&gt;</c> でリンク化済み、「、」で連結）。</summary>
    public string NamesHtml { get; set; } = "";
}

/// <summary>札に並べる役職 1 つ分（札の文字・色・リンク先）。</summary>
public sealed class EpisodeStaffBadgeRole
{
    /// <summary>役職コード（CSS の <c>data-role-code</c>）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>札の文字。画面の役職の表記（role_label_text）があればそれ、無ければ役職マスタの名前。</summary>
    public string Label { get; set; } = "";

    /// <summary>札のリンク先（系譜の代表の役職のページ）。</summary>
    public string RoleUrl { get; set; } = "";

    /// <summary>札の style 属性の値（作品の行の色から作った地・字・枠の色。<see cref="Rendering.RoleBadgeColor.InlineStyle"/>）。</summary>
    public string BadgeStyle { get; set; } = "";

    /// <summary>作品の行の色（<c>#rrggbb</c> の小文字。<see cref="Rendering.RoleBadgeColor.Normalize"/>）。OGP カードの役職名の色に使う。</summary>
    public string ColorHex { get; set; } = "";
}
