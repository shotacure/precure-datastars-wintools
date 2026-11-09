namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// エピソード単位で抽出した「主要 5 役職のスタッフ名（HTML 断片）」サマリ
/// <c>SeriesGenerator</c> と <c>/episodes/</c> ランディングページ生成
/// （<see cref="Generators.EpisodesIndexGenerator"/>）の両方から共有する独立公開クラス。
/// この型は
/// 両者で共有するため本ファイルに外出ししている。
/// 各文字列フィールドは「、」で連結済みの HTML 断片を保持する。PERSON エントリは
/// <see cref="StaffNameLinkResolver"/> によって <c>&lt;a href="/people/{名前}/"&gt;表示名&lt;/a&gt;</c>
/// 形式にラップ済み、TEXT エントリは HTML エスケープ済み。テンプレ側ではこれを <c>html.escape</c>
/// を掛けずにそのまま出力する。
/// 抽出ロジック（<c>ExtractStaffSummaryAsync</c>）と PERSON エントリ解決（<c>ResolveStaffEntryAsync</c>）
/// 本体は引き続き <c>SeriesGenerator</c> に置く。<c>SeriesGenerator</c> は本クラスのインスタンスを
/// <c>Dictionary&lt;int, EpisodeStaffSummary&gt;</c> で memoize し、公開メソッド
/// <c>GetEpisodeStaffSummaries()</c> 経由でパイプライン後段（<c>EpisodesIndexGenerator</c>）に渡す。
/// </summary>
public sealed class EpisodeStaffSummary
{
    /// <summary>脚本担当者（PERSON エントリは <c>&lt;a&gt;</c> でリンク化済み、HTML 断片を「、」で連結）。</summary>
    public string Screenplay { get; set; } = "";

    /// <summary>絵コンテ担当者（同上）。</summary>
    public string Storyboard { get; set; } = "";

    /// <summary>演出担当者（同上）。</summary>
    public string EpisodeDirector { get; set; } = "";

    /// <summary>総作画監督担当者（同上）。本編の作品だけで、テンプレ側は作画監督の札の直前に出す。オトナシリーズの作品は空（<see cref="AnimationCardRoles"/> に入る）。</summary>
    public string ChiefAnimationDirector { get; set; } = "";

    /// <summary>総作画監督の札のリンク先（総作画監督の役職のページ）。</summary>
    public string ChiefAnimationDirectorUrl { get; set; } = "";

    /// <summary>作画監督担当者（同上）。</summary>
    public string AnimationDirector { get; set; } = "";

    /// <summary>美術担当者（同上）。本編の作品は美術監督（区分 5 の役職）、オトナシリーズの作品は各話の美術デザイン。</summary>
    public string ArtDirector { get; set; } = "";

    /// <summary>美術の札の文字。本編の作品は「美術」、オトナシリーズの作品は「美術デザイン」。</summary>
    public string ArtLabel { get; set; } = "美術";

    /// <summary>美術の札の色分けに使う役職コード（CSS の <c>data-role-code</c>）。本編の作品は <c>ART_DIRECTOR_TV</c>、オトナシリーズの作品は <c>ART_DESIGN</c>。どちらも黄。</summary>
    public string ArtRoleCode { get; set; } = "ART_DIRECTOR_TV";

    /// <summary>美術の札のリンク先。本編の作品は TV の美術の役職詳細、オトナシリーズの作品は美術デザインのオトナシリーズの役職のページ（無ければ本編の役職詳細）。</summary>
    public string ArtRoleUrl { get; set; } = "/creators/roles/art_director_tv/";

    /// <summary>
    /// オトナシリーズの作品で、作画監督の札の代わりに並べる札。作画監督と同じカードにある作画監督以外の役職
    /// （総作画監督・料理パート作画監督・サブキャラクターデザインなど）を、クレジットの順に 1 役職 1 札で持つ。
    /// 本編の作品は空で、作画監督は <see cref="AnimationDirector"/> に入る。
    /// </summary>
    public IReadOnlyList<EpisodeStaffBadge> AnimationCardRoles { get; set; } = Array.Empty<EpisodeStaffBadge>();

    /// <summary>絵コンテと演出が同一人物（同一エントリ集合）かどうかのフラグ。 true のとき、テンプレ側で「絵コンテ・演出 ○○」の 1 ライン統合表示を行う。</summary>
    public bool StoryboardDirectorMerged { get; set; }
}

/// <summary>エピソード一覧のスタッフ段の札 1 つ（役職の札と担当者の HTML 断片）。</summary>
public sealed class EpisodeStaffBadge
{
    /// <summary>札の文字。クレジットの見出しの文字（role_label_text）があればそれ、無ければ役職マスタの名前。</summary>
    public string Label { get; set; } = "";

    /// <summary>札の色分けに使う役職コード（CSS の <c>data-role-code</c>）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>札のリンク先（役職のページ）。</summary>
    public string RoleUrl { get; set; } = "";

    /// <summary>担当者の HTML 断片（PERSON は <c>&lt;a&gt;</c> でリンク化済み、「、」で連結）。</summary>
    public string NamesHtml { get; set; } = "";
}
