using System;
using System.Collections.Generic;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>Creators API SearchItems の SearchIndex 引数（検索対象カテゴリ）。</summary>
public enum PaSearchIndex
{
    /// <summary>物理音楽（CD/レコード）。Amazon ジャパンでは「ミュージック」相当。</summary>
    Music,
    /// <summary>デジタル音源（Amazon Music の MP3 アルバム）。「デジタルミュージック」相当。</summary>
    DigitalMusic,
    /// <summary>紙の書籍（単行本・ムック・絵本・雑誌増刊）。「本」相当。</summary>
    Books,
    /// <summary>電子書籍（Kindle 版）。「Kindle ストア」相当。</summary>
    KindleStore,
}

/// <summary>
/// リクエストに載せる Resources の組み合わせ。音楽商品は従来どおりの最小集合で足りるが、
/// 書籍は著者ロール・ISBN・ページ数・判型・カテゴリまで欲しいため拡張集合を使う。
/// </summary>
public enum PaResourceSet
{
    /// <summary>従来の音楽商品向け最小集合（画像・タイトル・著者名・発売日・価格）。</summary>
    Standard,
    /// <summary>書籍向け拡張集合。Standard に contentInfo / classifications / externalIds / browseNodeInfo を足したもの。</summary>
    Extended,
}

/// <summary>
/// 商品への寄与者 1 名分（<c>itemInfo.byLineInfo.contributors[]</c> の 1 要素）。
/// 書籍では著者・イラスト・監修・編集などがロール付きで並ぶため、名前とロールを対にして保持する。
/// </summary>
public sealed class PaContributor
{
    /// <summary>寄与者名（表記そのまま）。</summary>
    public string Name { get; set; } = "";

    /// <summary>ロールの表示名（日本語で返る。例 "著" / "編集" / "監修" / "イラスト"）。Amazon 側が返さないときは null。</summary>
    public string? Role { get; set; }

    /// <summary>
    /// ロールの機械可読コード（例 "author" / "editor" / "illustrator"）。
    /// 表示名 <see cref="Role"/> より安定しているため、書籍役職コードへのマッピングはこちらを主に使う。
    /// </summary>
    public string? RoleType { get; set; }

    /// <summary>ログ・診断用の "名前 (ロール)" 表記。</summary>
    public override string ToString() => string.IsNullOrEmpty(Role) ? Name : $"{Name} ({Role})";
}

/// <summary>
/// Creators API GetItems / SearchItems のレスポンスから抽出した 1 商品分のビュー。
/// 取得できる情報は ASIN・タイトル・著者/アーティスト・価格・発売日・画像 URL（複数サイズ）など。
/// <see cref="PaResourceSet.Extended"/> で取得した場合は、書籍向けの寄与者一覧・出版社・ページ数・
/// ISBN・判型・カテゴリも埋まる（Standard 取得時はすべて null / 空のまま）。
/// 画像 URL は <c>m.media-amazon.com</c> 系で、本データ層では文字列として保持し実体保存はしない。
/// </summary>
public sealed class PaItem
{
    /// <summary>商品 ASIN（10 桁の英数字）。</summary>
    public string Asin { get; set; } = "";

    /// <summary>商品タイトル。</summary>
    public string Title { get; set; } = "";

    /// <summary>著者・アーティスト名（先頭 1 件）。全件とロールが要るときは <see cref="Contributors"/> を見る。</summary>
    public string? ByLine { get; set; }

    /// <summary>寄与者の全件（名前 + ロール）。Extended 取得時のみ 1 件以上入る。</summary>
    public IReadOnlyList<PaContributor> Contributors { get; set; } = Array.Empty<PaContributor>();

    /// <summary>製造元／出版社（<c>byLineInfo.manufacturer</c>）。書籍では出版社名が入る。</summary>
    public string? Manufacturer { get; set; }

    /// <summary>ブランド（<c>byLineInfo.brand</c>）。書籍ではレーベル名が入ることがある。</summary>
    public string? Brand { get; set; }

    /// <summary>商品詳細ページの URL（アフィリエイトタグは Creators API レスポンスに含まれていれば自動付与）。</summary>
    public string? DetailPageUrl { get; set; }

    /// <summary>大サイズ画像（500x500 程度）の URL。<c>m.media-amazon.com</c> 系。</summary>
    public string? LargeImageUrl { get; set; }

    /// <summary>中サイズ画像（160x160 程度）の URL。検索ダイアログのサムネ用。</summary>
    public string? MediumImageUrl { get; set; }

    /// <summary>表示用の価格（例 "¥3,300"）。Offers が無いときは null。</summary>
    public string? PriceDisplay { get; set; }

    /// <summary>価格の数値（円）。<c>offersV2.listings[0].price.money.amount</c> 由来で、DB への取り込みはこちらを使う。</summary>
    public int? PriceAmount { get; set; }

    /// <summary>発売日（Creators API では文字列のまま返る）。例 "2008-07-02"。</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>出版日（<c>contentInfo.publicationDate</c>）。書籍では <see cref="ReleaseDate"/> と別値になることがある。</summary>
    public string? PublicationDate { get; set; }

    /// <summary>ページ数（<c>contentInfo.pagesCount</c>）。</summary>
    public int? PagesCount { get; set; }

    /// <summary>版次（<c>contentInfo.edition</c>）。</summary>
    public string? Edition { get; set; }

    /// <summary>装丁・判型（<c>classifications.binding</c>）。例 "単行本" / "ムック" / "Kindle版"。</summary>
    public string? Binding { get; set; }

    /// <summary>商品グループ（<c>classifications.productGroup</c>）。例 "Book" / "eBooks"。</summary>
    public string? ProductGroup { get; set; }

    /// <summary>ISBN（<c>externalIds.isbNs</c> の先頭）。紙の書籍のみ。ハイフン無しで返ることが多い。</summary>
    public string? Isbn { get; set; }

    /// <summary>EAN（<c>externalIds.eaNs</c> の先頭）。</summary>
    public string? Ean { get; set; }

    /// <summary>ブラウズノード（カテゴリ）名の列。ジャンル推定の手がかりに使う。</summary>
    public IReadOnlyList<string> BrowseNodes { get; set; } = Array.Empty<string>();
}
