namespace PrecureDataStars.Data.Models;

/// <summary>
/// books テーブルに対応するエンティティモデル（PK: book_id）。
/// 書籍（紙 / Kindle）を表す。音楽商品（<see cref="Product"/> / <see cref="Disc"/>）とは独立した系統で、
/// 書籍には品番に相当する自然キーが無い（ISBN は紙のみで Kindle 版には無い）ため代理キーを主キーとする。
/// <para>
/// 同一書籍の紙版と Kindle 版は別 ASIN が振られるため、ASIN・表紙 URL・価格を 2 系統で保持し、
/// 詳細ページでは「Amazon で買う (紙)」「Kindle で読む」を並列表示する。
/// </para>
/// <para>
/// シリーズ所属は本モデルではなく <c>book_series</c>（多対多）側に持つ。合同本・オールスターズ本のため。
/// ジャンルも <c>book_genre_links</c>（多対多）側で、1 冊が「ムック かつ 設定資料集」を取り得る。
/// </para>
/// </summary>
public sealed class Book
{
    // ── 主キー ──

    /// <summary>書籍の主キー（代理キー）。</summary>
    public int BookId { get; set; }

    // ── 表題 ──

    /// <summary>書名。</summary>
    public string Title { get; set; } = "";

    /// <summary>書名の読み（任意）。五十音順の並びに使う。</summary>
    public string? TitleKana { get; set; }

    /// <summary>英語書名（任意）。</summary>
    public string? TitleEn { get; set; }

    // ── 関連 ──

    /// <summary>出版社として紐付ける社名マスタ（→ product_companies）の ID。 音楽商品の発売元／販売元と同じマスタを流用する。</summary>
    public int? PublisherProductCompanyId { get; set; }

    // ── 販売情報 ──

    /// <summary>代表発売日。紙があれば紙の発売日、電子のみの書籍は配信日。</summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>Kindle 版の配信日（紙と同日なら NULL）。後日配信のときだけ入れる。</summary>
    public DateTime? ReleaseDateKindle { get; set; }

    /// <summary>ISBN-13（紙のみ）。Amazon からは <c>externalIds.eans</c> の 13 桁を採る。</summary>
    public string? Isbn13 { get; set; }

    /// <summary>Cコード（分類コード。"C" + 数字 4 桁、例: <c>C8776</c>）。書籍 JAN の 2 段目はこれと税抜価格から導く。</summary>
    public string? CCode { get; set; }

    /// <summary>雑誌コード（5 桁 + "-" + 月号 2 桁、例: <c>66557-17</c>）。ムック・増刊・別冊向け。月号に年を含まないため一意ではない。</summary>
    public string? MagazineCode { get; set; }

    /// <summary>定期刊行物コード（雑誌 JAN）。"491" で始まる 13 桁 + 価格アドオン 5 桁の 18 桁（区切り無し）。</summary>
    public string? PeriodicalCode { get; set; }

    /// <summary>ページ数（任意）。</summary>
    public ushort? PageCount { get; set; }

    /// <summary>装丁の生表記（"ムック" / "大型本" / "単行本（ソフトカバー）" 等）。Amazon 取り込みの受け皿。</summary>
    public string? BindingText { get; set; }

    /// <summary>判型（A4 / B5 / 新書判 等）。人手で整えた値を入れる。</summary>
    public string? TrimSize { get; set; }

    /// <summary>紙版の税抜価格（円）。</summary>
    public int? PriceExTax { get; set; }

    /// <summary>紙版の税込価格（円）。</summary>
    public int? PriceIncTax { get; set; }

    /// <summary>Kindle 版の税込価格（円）。</summary>
    public int? PriceKindleIncTax { get; set; }

    /// <summary>紙版が存在するか。ASIN 未取得でも版の有無は事実として持てるので独立したフラグにする。</summary>
    public bool HasPrint { get; set; } = true;

    /// <summary>Kindle 版が存在するか。</summary>
    public bool HasKindle { get; set; }

    // ── 外部プラットフォーム ID ──

    /// <summary>紙版の Amazon ASIN。</summary>
    public string? AmazonAsinPrint { get; set; }

    /// <summary>Kindle 版の Amazon ASIN。</summary>
    public string? AmazonAsinKindle { get; set; }

    // ── 表紙画像キャッシュ ──

    /// <summary>紙版 ASIN から取得した表紙画像 URL（Amazon CDN ホットリンク、画像実体は保存しない）。未取得は NULL。</summary>
    public string? CoverImageUrlPrint { get; set; }

    /// <summary>Kindle 版 ASIN から取得した表紙画像 URL。未取得は NULL。</summary>
    public string? CoverImageUrlKindle { get; set; }

    /// <summary>表紙画像の取得元コード＝表示に採用するソース（代表）の明示選択。 取り得る値は <c>amazon_print</c> ／ <c>amazon_kindle</c>。未選択は NULL。</summary>
    public string? CoverImageSource { get; set; }

    /// <summary>詳細ページで紙・Kindle 両方の表紙を並べて表示するか（true=両方 / false=代表 1 枚）。 両 URL が揃っていて互いに異なる場合のみ実際に効く。索引・ホームは常に代表 1 枚。</summary>
    public bool CoverImageShowBoth { get; set; }

    /// <summary>表示に使う実効表紙画像 URL。<see cref="CoverImageSource"/> が指すソースの URL を返す計算プロパティ（DB 列ではない）。 採用ソースが未選択／該当 URL が無い場合は、選択を尊重しつつ Kindle→紙 の順でフォールバックする。 音楽商品のデジタル優先と同じ理由で、電子側は事業者アップの正規画像が確実に得られる一方、 紙（特に絶版書）は出品者の撮影画像が混ざるリスクがあるため。</summary>
    public string? CoverImageUrl =>
        CoverImageSource switch
        {
            "amazon_kindle" => CoverImageUrlKindle ?? CoverImageUrlPrint,
            "amazon_print" => CoverImageUrlPrint ?? CoverImageUrlKindle,
            _ => CoverImageUrlKindle ?? CoverImageUrlPrint,
        };

    /// <summary>表紙画像 URL の取得日時。再取得（鮮度判定）に使う。未取得は NULL。</summary>
    public DateTime? CoverImageFetchedAt { get; set; }

    // ── 備考 ──

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 外部リンク（詳細ページの末尾「外部リンク」セクションに出る） ──

    /// <summary>書籍の公式ページ URL（任意）。</summary>
    public string? OfficialUrl { get; set; }

    // ── 監査 ──

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザー。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザー。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
