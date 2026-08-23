#nullable enable
using PrecureDataStars.AmazonPaApi;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// <see cref="AmazonProductSearchDialog"/> の検索対象と表示文言を決める設定。
/// ダイアログは「左右 2 系統を並列検索して片方ずつ選ばせる」骨格だけを持ち、
/// その 2 系統が何なのか（音楽の CD / デジタルなのか、書籍の紙 / Kindle なのか）は
/// 本クラスが与える。
/// <para>
/// <see cref="PreferRightForCover"/> は代表画像をどちら側から採るかの既定。音楽・書籍とも
/// 電子側（デジタル音源 / Kindle）を優先する。事業者アップの正規画像が確実に得られる一方、
/// 物理商品（特に廃盤・絶版）は出品者の撮影画像が混ざるリスクがあるため。
/// </para>
/// </summary>
public sealed class AmazonSearchMode
{
    /// <summary>左ペインの見出し。</summary>
    public required string LeftHeader { get; init; }

    /// <summary>右ペインの見出し。</summary>
    public required string RightHeader { get; init; }

    /// <summary>ステータス行や失敗メッセージで使う左系統の短い呼び名。</summary>
    public required string LeftShortLabel { get; init; }

    /// <summary>ステータス行や失敗メッセージで使う右系統の短い呼び名。</summary>
    public required string RightShortLabel { get; init; }

    /// <summary>左ペインの検索カテゴリ。</summary>
    public required PaSearchIndex LeftIndex { get; init; }

    /// <summary>右ペインの検索カテゴリ。</summary>
    public required PaSearchIndex RightIndex { get; init; }

    /// <summary>左系統を代表画像に採ったときの取得元コード（<c>amazon_cd</c> / <c>amazon_print</c>）。</summary>
    public required string LeftSourceCode { get; init; }

    /// <summary>右系統を代表画像に採ったときの取得元コード（<c>amazon_digital</c> / <c>amazon_kindle</c>）。</summary>
    public required string RightSourceCode { get; init; }

    /// <summary>代表画像を右系統から優先して採るか。false なら左優先。</summary>
    public required bool PreferRightForCover { get; init; }

    /// <summary>要求するリソース集合。書籍はページ数・ISBN 等が要るため拡張集合を使う。</summary>
    public PaResourceSet ResourceSet { get; init; } = PaResourceSet.Standard;

    /// <summary>音楽商品向け（CD / デジタル音源）。代表画像はデジタル優先。</summary>
    public static AmazonSearchMode Music() => new()
    {
        LeftHeader = "CD (物理パッケージ)",
        RightHeader = "デジタル (Amazon Music)",
        LeftShortLabel = "CD",
        RightShortLabel = "デジタル",
        LeftIndex = PaSearchIndex.Music,
        RightIndex = PaSearchIndex.DigitalMusic,
        LeftSourceCode = "amazon_cd",
        RightSourceCode = "amazon_digital",
        PreferRightForCover = true,
    };

    /// <summary>書籍向け（紙 / Kindle）。代表画像は Kindle 優先。</summary>
    public static AmazonSearchMode Book() => new()
    {
        LeftHeader = "紙 (単行本・ムック)",
        RightHeader = "Kindle 版",
        LeftShortLabel = "紙",
        RightShortLabel = "Kindle",
        LeftIndex = PaSearchIndex.Books,
        RightIndex = PaSearchIndex.KindleStore,
        LeftSourceCode = "amazon_print",
        RightSourceCode = "amazon_kindle",
        PreferRightForCover = true,
        ResourceSet = PaResourceSet.Extended,
    };
}
