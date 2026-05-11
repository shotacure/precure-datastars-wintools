namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// エピソード単位で抽出した「主要 5 役職のスタッフ名（HTML 断片）」サマリ
/// （v1.3.0 続編 第 N+3 弾で <see cref="Generators.SeriesGenerator"/> の private nested class から
/// 独立公開クラスに昇格）。
/// <para>
/// 旧来は <c>SeriesGenerator</c> の <c>ExtractStaffSummaryAsync</c> がローカルに使う型だったが、
/// <c>/episodes/</c> ランディングページ生成（<see cref="Generators.EpisodesIndexGenerator"/>）からも
/// 同じデータが必要になったため、両者で共有できるように本ファイルへ外出しした。
/// </para>
/// <para>
/// 各文字列フィールドは「、」で連結済みの HTML 断片を保持する。PERSON エントリは
/// <see cref="StaffNameLinkResolver"/> によって <c>&lt;a href="/persons/{id}/"&gt;表示名&lt;/a&gt;</c>
/// 形式にラップ済み、TEXT エントリは HTML エスケープ済み。テンプレ側ではこれを <c>html.escape</c>
/// を掛けずにそのまま出力する。
/// </para>
/// <para>
/// 抽出ロジック（<c>ExtractStaffSummaryAsync</c>）と PERSON エントリ解決（<c>ResolveStaffEntryAsync</c>）
/// 本体は引き続き <c>SeriesGenerator</c> に置く。<c>SeriesGenerator</c> は本クラスのインスタンスを
/// <c>Dictionary&lt;int, EpisodeStaffSummary&gt;</c> で memoize し、公開メソッド
/// <c>GetEpisodeStaffSummaries()</c> 経由でパイプライン後段（<c>EpisodesIndexGenerator</c>）に渡す。
/// </para>
/// </summary>
public sealed class EpisodeStaffSummary
{
    /// <summary>脚本担当者（PERSON エントリは <c>&lt;a&gt;</c> でリンク化済み、HTML 断片を「、」で連結）。</summary>
    public string Screenplay { get; set; } = "";

    /// <summary>絵コンテ担当者（同上）。</summary>
    public string Storyboard { get; set; } = "";

    /// <summary>演出担当者（同上）。</summary>
    public string EpisodeDirector { get; set; } = "";

    /// <summary>作画監督担当者（同上）。</summary>
    public string AnimationDirector { get; set; } = "";

    /// <summary>美術監督担当者（同上）。</summary>
    public string ArtDirector { get; set; } = "";

    /// <summary>
    /// 絵コンテと演出が同一人物（同一エントリ集合）かどうかのフラグ。
    /// true のとき、テンプレ側で「絵コンテ・演出 ○○」の 1 ライン統合表示を行う。
    /// </summary>
    public bool StoryboardDirectorMerged { get; set; }
}
