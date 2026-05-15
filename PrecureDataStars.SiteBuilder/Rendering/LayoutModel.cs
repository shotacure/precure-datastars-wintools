namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 共通レイアウト <c>_layout.sbn</c> に渡すモデル。
/// <para>
/// 各 Generator は本文を「コンテンツテンプレート」でレンダリングして HTML 文字列を作り、
/// それを <see cref="Content"/> に詰めて <c>_layout.sbn</c> をレンダリングする 2 段構え。
/// Scriban には Razor の <c>@RenderBody()</c> 相当の仕組みがないため、
/// このパターンが最もシンプル。
/// </para>
/// </summary>
public sealed class LayoutModel
{
    /// <summary>サイト名（ヘッダおよび <c>&lt;title&gt;</c> サフィックス）。</summary>
    public string SiteName { get; set; } = "";

    /// <summary>絶対 URL 構築用ベース URL。空文字なら canonical 出力をスキップ。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>当ページの canonical パス（例 "/series/precure/"）。</summary>
    public string CanonicalPath { get; set; } = "";

    /// <summary><c>&lt;title&gt;</c> のプレフィックス。空ならサイト名のみ。</summary>
    public string PageTitle { get; set; } = "";

    /// <summary>meta description。空なら出力しない。</summary>
    public string MetaDescription { get; set; } = "";

    /// <summary>パンくずリスト。空なら出力しない。</summary>
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; set; } = Array.Empty<BreadcrumbItem>();

    /// <summary>本文 HTML（Generator がコンテンツテンプレで先にレンダリングした結果）。</summary>
    public string Content { get; set; } = "";

    // ── SEO / OGP / アナリティクス系（v1.3.0 追加） ──

    /// <summary>
    /// OGP の <c>og:type</c> 値。空文字なら <see cref="Rendering.PageRenderer"/> が自動で
    /// "website" を補う。ページ種別に応じて Generator 側で個別指定する：
    /// シリーズ・エピソードは "video.tv_show" / "video.episode"、人物は "profile"、
    /// 楽曲は "music.song" 等、Schema.org / OGP 仕様に準拠した値を使う。
    /// </summary>
    public string OgType { get; set; } = "";

    /// <summary>
    /// OGP の <c>og:image</c> 値（絶対 URL）。空文字なら出力しない。
    /// 個別ページ専用画像が無い場合は <see cref="PageRenderer"/> が
    /// <see cref="Configuration.BuildConfig.DefaultOgImage"/> から自動補完する
    /// （v1.3.1 で自動補完経路を追加）。Generator 側で明示指定すればそちらが優先される。
    /// </summary>
    public string OgImage { get; set; } = "";

    /// <summary>
    /// JSON-LD（Schema.org 構造化データ）の本体 JSON 文字列。空文字なら
    /// <c>&lt;script type="application/ld+json"&gt;</c> 自体を出力しない。
    /// Generator 側で <see cref="System.Text.Json.JsonSerializer.Serialize{T}(T, System.Text.Json.JsonSerializerOptions)"/>
    /// 等を使って構築済みの JSON 文字列を入れる。
    /// </summary>
    public string JsonLd { get; set; } = "";

    /// <summary>
    /// パンくず由来の Schema.org <c>BreadcrumbList</c> 構造化データ（v1.3.1 追加）。
    /// <see cref="Breadcrumbs"/> が 1 件以上あるとき <see cref="PageRenderer"/> が自動生成して詰める。
    /// <c>_layout.sbn</c> 側では本値が空でないときに専用の
    /// <c>&lt;script type="application/ld+json"&gt;</c> を 1 つ追加で出力する。
    /// </summary>
    public string BreadcrumbJsonLd { get; set; } = "";

    /// <summary>
    /// Google Analytics 4 メジャメント ID（例: <c>G-XXXXXXXXXX</c>）。
    /// PageRenderer が BuildConfig から自動補完する。空文字なら GA4 タグを出力しない。
    /// </summary>
    public string Ga4MeasurementId { get; set; } = "";

    /// <summary>
    /// Google Search Console の所有権確認用トークン。
    /// PageRenderer が BuildConfig から自動補完する。空文字なら確認用メタタグを出力しない。
    /// </summary>
    public string GoogleSiteVerification { get; set; } = "";

    /// <summary>
    /// Google AdSense のパブリッシャー ID（例: <c>ca-pub-1234567890123456</c>）。
    /// PageRenderer が BuildConfig から自動補完する。空文字なら AdSense スクリプトを出力しない。
    /// 設定値があるときは自動広告モードのローダスクリプトのみが head に出力され、
    /// 個別広告ユニットの配置は Google 側の自動配置に任せる。
    /// </summary>
    public string GoogleAdSenseClientId { get; set; } = "";

    /// <summary>
    /// フッタの著作権表記に使う「年」の文字列（v1.3.0 続編 追加）。
    /// 例: 公開年と現在年が同じなら <c>"2026"</c>、異なれば <c>"2026-2027"</c>。
    /// PageRenderer が BuildConfig の <see cref="Configuration.BuildConfig.PublishedYear"/> と
    /// 現在年から自動算出して埋める（Generator から直接指定する必要は無い）。
    /// </summary>
    public string CopyrightYears { get; set; } = "";

    /// <summary>
    /// SNS シェアボタンに渡すシェア用本文テキスト（v1.3.1 追加）。
    /// 「<c>{PageTitle} | {SiteName}</c>」+ 改行 + 規定ハッシュタグ列の組み立てを
    /// <see cref="PageRenderer"/> が自動生成して詰める。
    /// <c>_layout.sbn</c> 側で <c>_share-buttons.sbn</c> をインクルードする際にシェア用 URL の
    /// クエリパラメータとして使う。Generator 側で個別指定が必要なケースは原則無い。
    /// </summary>
    public string ShareText { get; set; } = "";

    /// <summary>
    /// SNS シェアボタンに渡すシェア対象 URL（v1.3.1 追加、絶対 URL）。
    /// <see cref="PageRenderer"/> が <c>BaseUrl + CanonicalPath</c> から組み立てる。
    /// <c>BaseUrl</c> が空のときは空文字となり、シェアボタンは本値が空のため非表示にする。
    /// </summary>
    public string ShareUrl { get; set; } = "";

    /// <summary>
    /// SNS シェア用のハッシュタグ列（v1.3.1 追加、カンマ区切り。例: <c>"プリキュア,プリキュアデータベース"</c>）。
    /// X / Twitter のシェア URL はハッシュタグ用クエリ <c>hashtags=</c> をカンマ区切りで受けるため、
    /// 本値はそのまま渡せる形式で保持する。
    /// 既定は <see cref="PageRenderer"/> が固定値で詰める運用。
    /// </summary>
    public string ShareHashtags { get; set; } = "";
}

/// <summary>パンくずの 1 項目。</summary>
public sealed class BreadcrumbItem
{
    /// <summary>表示文字列。</summary>
    public string Label { get; set; } = "";

    /// <summary>リンク先 URL。最終項目（自分自身）では空文字にしておく。</summary>
    public string Url { get; set; } = "";
}
