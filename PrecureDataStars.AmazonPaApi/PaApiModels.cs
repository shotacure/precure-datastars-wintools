#nullable enable
using System;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>PA-API SearchItems の SearchIndex 引数（検索対象カテゴリ）。</summary>
public enum PaSearchIndex
{
    /// <summary>物理音楽（CD/レコード）。Amazon ジャパンでは「ミュージック」相当。</summary>
    Music,
    /// <summary>デジタル音源（Amazon Music の MP3 アルバム）。「デジタルミュージック」相当。</summary>
    DigitalMusic,
}

/// <summary>
/// PA-API GetItems / SearchItems のレスポンスから抽出した 1 商品分のビュー。
/// 取得できる情報は ASIN・タイトル・著者/アーティスト・価格・発売日・画像 URL（複数サイズ）など。
/// 画像 URL は <c>m.media-amazon.com</c> 系で、本データ層では文字列として保持し実体保存はしない。
/// </summary>
public sealed class PaItem
{
    /// <summary>商品 ASIN（10 桁の英数字）。</summary>
    public string Asin { get; set; } = "";

    /// <summary>商品タイトル。</summary>
    public string Title { get; set; } = "";

    /// <summary>著者・アーティスト名（先頭 1 件）。</summary>
    public string? ByLine { get; set; }

    /// <summary>商品詳細ページの URL（アフィリエイトタグは PA-API レスポンスに含まれていれば自動付与）。</summary>
    public string? DetailPageUrl { get; set; }

    /// <summary>大サイズ画像（500x500 程度）の URL。<c>m.media-amazon.com</c> 系。</summary>
    public string? LargeImageUrl { get; set; }

    /// <summary>中サイズ画像（160x160 程度）の URL。検索ダイアログのサムネ用。</summary>
    public string? MediumImageUrl { get; set; }

    /// <summary>表示用の価格（例 "¥3,300"）。Offers が無いときは null。</summary>
    public string? PriceDisplay { get; set; }

    /// <summary>発売日（PA-API では文字列のまま返る）。例 "2008-07-02"。</summary>
    public string? ReleaseDate { get; set; }
}
