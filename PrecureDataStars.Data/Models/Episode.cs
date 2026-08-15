namespace PrecureDataStars.Data.Models;

/// <summary>
/// episodes テーブルに対応するエンティティモデル（PK: episode_id）。
/// 各 TV シリーズの放送回（エピソード）を 1 レコードとして管理する。
/// シリーズ内話数 (<see cref="SeriesEpNo"/>) のほか、
/// 全シリーズ横断の通算話数 (<see cref="TotalEpNo"/>) や
/// ニチアサ枠通算放送回 (<see cref="NitiasaOaNo"/>) など複数の採番体系を持つ。
/// <c>on_air_at</c> は DATETIME（タイムゾーン情報なし）で格納される。
/// アプリケーション側で JST 前提の運用を統一する想定。
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

    /// <summary>ニチアサ枠（『とんがり帽子のメモル』#29〜）通算の放送回（NULL 許可、UNIQUE）。 両者が非 NULL のとき <c>NitiasaOaNo = TotalOaNo + 978</c> の CHECK 制約あり。 978 は『明日のナージャ』までの通算放送回数に相当する。</summary>
    public int? NitiasaOaNo { get; set; }               // = TotalOaNo + 978（両者非NULL時）

    // ── タイトル関連 ──

    /// <summary>サブタイトル（プレーンテキスト）。 DB 上は NULL 許容（NULL = 未確定。放送予定だけ確定している状態）で、 コード上は空文字に正規化して扱う（リポジトリが読み書き時に NULL ⇔ 空文字を変換する）。 未確定を許すのは <see cref="MagazineSubtitleStatus"/> が非公開 / 未定のときのみ （DB 側 CHECK: ck_ep_title_or_magazine_reason）。</summary>
    public string TitleText { get; set; } = string.Empty;

    /// <summary>ルビ付き HTML 表記のサブタイトル。 <c>&lt;ruby&gt;</c> タグ等でふりがなを含む。Web 表示用途。</summary>
    public string? TitleRichHtml { get; set; }

    /// <summary>サブタイトルの全文かな読み。</summary>
    public string? TitleKana { get; set; }

    /// <summary>サブタイトルの文字統計 JSON（DB 側で JSON_VALID チェック）。 <see cref="TitleCharStatsJson.TitleCharStatsBuilder"/> で生成される。</summary>
    public string? TitleCharStats { get; set; }

    // ── 放送日時 ──

    /// <summary>初回放送日時（DATETIME、タイムゾーンなし。JST 前提）。</summary>
    public DateTime OnAirAt { get; set; }

    /// <summary>放送尺（分）。<see cref="OnAirAt"/> を起点とする 1 話分の放送枠の長さ。 既存 TV エピソードはすべて 30 分（マイグレーションでバックフィル済）。 今後の TV / 短尺映画／配信短編で異なる値が設定されることを想定して NULL 許可。 値が入っていれば SiteBuilder 等で「8:30〜9:00」のような開始〜終了表示を組み立てる。</summary>
    public byte? DurationMinutes { get; set; }

    // ── 外部 URL ──

    /// <summary>東映アニメーション公式サイトの各話あらすじページ URL。</summary>
    public string? ToeiAnimSummaryUrl { get; set; }

    /// <summary>東映アニメーション公式サイトのラインナップ（一覧）ページ URL。</summary>
    public string? ToeiAnimLineupUrl { get; set; }

    /// <summary>YouTube 予告（次回予告）動画の URL。</summary>
    public string? YoutubeTrailerUrl { get; set; }

    /// <summary>特別予告（本放送時に流れた特別な予告）の YouTube 動画 URL。</summary>
    public string? YoutubeSpecialTrailerUrl { get; set; }

    // ── アニメ雑誌サブタイトル掲載 ──

    /// <summary>アニメ雑誌でのサブタイトル掲載状態。 <see cref="MagazineSubtitleStatuses"/> のコード（PUBLISHED / NOT_DISCLOSED / UNDECIDED）、 NULL はデータなし（サイトにはセクション自体を出さない）。 どの号に載る（載らなかった）かは magazine_issues マスタの発売日と放送日から導出する。</summary>
    public string? MagazineSubtitleStatus { get; set; }

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

    /// <summary><see cref="OnAirAt"/> から導出される放送日（DB 側の生成列 on_air_date に相当）。</summary>
    public DateOnly OnAirDate => DateOnly.FromDateTime(OnAirAt);

    /// <summary>表示用サブタイトル。確定していれば <see cref="TitleText"/> そのもの、 未確定（空）なら掲載状態に応じたプレースホルダ （（サブタイトル「未定」）/（サブタイトル「非公開」））。 一覧・ラベル系の表示で空欄を出さないための共通導出。</summary>
    public string TitleDisplayText => string.IsNullOrEmpty(TitleText)
        ? MagazineSubtitleStatuses.SubtitlePlaceholderFor(MagazineSubtitleStatus)
        : TitleText;
}
