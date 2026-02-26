namespace PrecureDataStars.Data.Models;

/// <summary>
/// episodes テーブルに対応するエンティティモデル（PK: episode_id）。
/// <para>
/// 各 TV シリーズの放送回（エピソード）を 1 レコードとして管理する。
/// シリーズ内話数 (<see cref="SeriesEpNo"/>) のほか、
/// 全シリーズ横断の通算話数 (<see cref="TotalEpNo"/>) や
/// ニチアサ枠通算放送回 (<see cref="NitiasaOaNo"/>) など複数の採番体系を持つ。
/// </para>
/// <remarks>
/// <c>on_air_at</c> は DATETIME（タイムゾーン情報なし）で格納される。
/// アプリケーション側で JST 前提の運用を統一する想定。
/// </remarks>
/// </summary>
public sealed class Episode
{
    // ── 主キー・外部キー ──

    /// <summary>エピソードの主キー（AUTO_INCREMENT）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>所属する TV シリーズの ID。series.series_id への外部キー。</summary>
    public int SeriesId { get; set; }

    // ── 各種ナンバリング ──

    /// <summary>シリーズ内の話数（1 始まり、NOT NULL）。</summary>
    public int SeriesEpNo { get; set; }                 // >= 1

    /// <summary>全シリーズ通算の話数（NULL 許可、UNIQUE）。</summary>
    public int? TotalEpNo { get; set; }                 // NULL or >= 1 (unique)

    /// <summary>全シリーズ通算の放送回（NULL 許可、UNIQUE）。</summary>
    public int? TotalOaNo { get; set; }                 // NULL or >= 1 (unique)

    /// <summary>
    /// ニチアサ枠（『とんがり帽子のメモル』#29〜）通算の放送回（NULL 許可、UNIQUE）。
    /// 両者が非 NULL のとき <c>NitiasaOaNo = TotalOaNo + 978</c> の CHECK 制約あり。
    /// 978 は『明日のナージャ』までの通算放送回数に相当する。
    /// </summary>
    public int? NitiasaOaNo { get; set; }               // = TotalOaNo + 978（両者非NULL時）

    // ── タイトル関連 ──

    /// <summary>サブタイトル（プレーンテキスト、NOT NULL）。</summary>
    public string TitleText { get; set; } = string.Empty;

    /// <summary>
    /// ルビ付き HTML 表記のサブタイトル。
    /// <c>&lt;ruby&gt;</c> タグ等でふりがなを含む。Web 表示用途。
    /// </summary>
    public string? TitleRichHtml { get; set; }

    /// <summary>サブタイトルの全文かな読み。</summary>
    public string? TitleKana { get; set; }

    /// <summary>
    /// サブタイトルの文字統計 JSON（DB 側で JSON_VALID チェック）。
    /// <see cref="TitleCharStatsJson.TitleCharStatsBuilder"/> で生成される。
    /// </summary>
    public string? TitleCharStats { get; set; }

    // ── 放送日時 ──

    /// <summary>初回放送日時（DATETIME、タイムゾーンなし。JST 前提）。</summary>
    public DateTime OnAirAt { get; set; }

    // ── 外部 URL ──

    /// <summary>東映アニメーション公式サイトの各話あらすじページ URL。</summary>
    public string? ToeiAnimSummaryUrl { get; set; }

    /// <summary>東映アニメーション公式サイトのラインナップ（一覧）ページ URL。</summary>
    public string? ToeiAnimLineupUrl { get; set; }

    /// <summary>YouTube 予告（次回予告）動画の URL。</summary>
    public string? YoutubeTrailerUrl { get; set; }

    // ── その他 ──

    /// <summary>備考（自由テキスト）。</summary>
    public string? Notes { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ（0: 有効、1: 削除済み）。</summary>
    public bool IsDeleted { get; set; }

    // ── 計算プロパティ ──

    /// <summary>
    /// <see cref="OnAirAt"/> から導出される放送日（DB 側の生成列 on_air_date に相当）。
    /// </summary>
    public DateOnly OnAirDate => DateOnly.FromDateTime(OnAirAt);
}
