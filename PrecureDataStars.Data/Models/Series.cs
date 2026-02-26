namespace PrecureDataStars.Data.Models;

/// <summary>
/// series テーブルに対応するエンティティモデル（PK: series_id）。
/// <para>
/// プリキュアシリーズの各作品（TV シリーズ・劇場版・配信短編など）を管理する。
/// <see cref="KindCode"/> で作品種別を、<see cref="ParentSeriesId"/> /
/// <see cref="RelationToParent"/> / <see cref="SeqInParent"/> で親子関係（併映・分割放送等）を表現する。
/// </para>
/// <remarks>
/// slug は <c>^[a-z0-9-]+$</c> の CHECK 制約で URL セーフな文字列に限定されている。
/// </remarks>
/// </summary>
public sealed class Series
{
    // ── 主キー ──

    /// <summary>シリーズの主キー（AUTO_INCREMENT）。</summary>
    public int SeriesId { get; set; }

    // ── 種別 ──

    /// <summary>作品種別コード（FK → series_kinds.kind_code、例: "TV", "MOVIE"）。</summary>
    public string KindCode { get; set; } = string.Empty;

    // ── 親子関係 ──

    /// <summary>親シリーズの ID（FK → series.series_id）。ルートシリーズの場合は NULL。</summary>
    public int? ParentSeriesId { get; set; }

    /// <summary>
    /// 親シリーズとの関係種別（FK → series_relation_kinds.relation_code）。
    /// 例: "COFEATURE"（併映）, "SEGMENT"（分割放送）。
    /// <see cref="ParentSeriesId"/> が NULL のとき、本プロパティも NULL になる（CHECK 制約）。
    /// </summary>
    public string? RelationToParent { get; set; }

    /// <summary>
    /// 親シリーズ内の並び順（COFEATURE / SEGMENT のとき 1 以上必須、CHECK 制約）。
    /// </summary>
    public byte? SeqInParent { get; set; }

    // ── タイトル群 ──

    /// <summary>正式タイトル（NOT NULL）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>正式タイトルのかな読み。</summary>
    public string? TitleKana { get; set; }

    /// <summary>略称（短縮タイトル）。</summary>
    public string? TitleShort { get; set; }

    /// <summary>略称のかな読み。</summary>
    public string? TitleShortKana { get; set; }

    /// <summary>英語タイトル。</summary>
    public string? TitleEn { get; set; }

    /// <summary>英語略称。</summary>
    public string? TitleShortEn { get; set; }

    // ── スラッグ ──

    /// <summary>URL 用スラッグ（UNIQUE、<c>^[a-z0-9-]+$</c>）。</summary>
    public string Slug { get; set; } = string.Empty;

    // ── 放送・公開期間 ──

    /// <summary>放送／公開開始日（NOT NULL）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>放送／公開終了日（NULL は放送中を示す）。start_date 以降でなければならない。</summary>
    public DateOnly? EndDate { get; set; }

    // ── メタ情報 ──

    /// <summary>TV シリーズの総話数、または映画のパート数。</summary>
    public ushort? Episodes { get; set; }

    /// <summary>映画等の上映時間（秒）。TV シリーズでは通常 NULL。</summary>
    public ushort? RunTimeSeconds { get; set; }

    // ── 外部 URL 群 ──

    /// <summary>東映アニメーション公式サイト URL。</summary>
    public string? ToeiAnimOfficialSiteUrl { get; set; }

    /// <summary>東映アニメーション ラインナップページ URL。</summary>
    public string? ToeiAnimLineupUrl { get; set; }

    /// <summary>ABC テレビ（朝日放送）公式サイト URL。</summary>
    public string? AbcOfficialSiteUrl { get; set; }

    /// <summary>Amazon プライム・ビデオ 配信ページ URL。</summary>
    public string? AmazonPrimeDistributionUrl { get; set; }

    // ── 配信・表示設定 ──

    /// <summary>配信版のイントロ尺（秒）。配信プラットフォーム向けの情報。</summary>
    public ushort? VodIntro { get; set; }

    /// <summary>サブタイトル表示用フォント名（フォント管理基盤整備前の暫定フィールド）。</summary>
    public string? FontSubtitle { get; set; }

    // ── 監査・論理削除 ──

    /// <summary>レコード作成者（監査用）。DB 側の created_at / updated_at は自動付与。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ（0: 有効、1: 削除済み）。</summary>
    public bool IsDeleted { get; set; }
}
