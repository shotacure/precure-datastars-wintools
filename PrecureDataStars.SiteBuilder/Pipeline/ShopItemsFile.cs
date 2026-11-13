using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 販売中の商品のファイル（<c>/shop-items.json</c>）の中身を組み立てる。
/// 作品詳細・各話の詳細・トップページの枠は、ページを開いたときにこのファイルを読んで描く（<c>shop-box.js</c>）。
/// 売り切れ・絶版になった商品をすぐ下げ、また売られ始めたらすぐ戻すため、枠の中身は HTML に焼き込まず、
/// このファイルだけを上げ直せば済むようにしてある（<see cref="ShopItemsPublisher"/>）。
/// 全体のビルドも同じ中身を出力に書くので、どちらが書いても同じファイルになる。
/// </summary>
internal static class ShopItemsFile
{
    /// <summary>出力のルートからのファイル名（S3 のキーも同じ）。</summary>
    public const string FileName = "shop-items.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>
    /// ファイルの中身（JSON）を返す。載せるのは、出す設定で、Amazon で売られていると確かめた行だけ。
    /// 中身は行の内容だけで決まる（時刻を入れない）ので、状態が変わらなければ同じバイト列になる。
    /// </summary>
    /// <param name="rows">出す設定の行（並び順の順）。</param>
    /// <param name="amazonAssociateTag">Amazon アソシエイトのトラッキング ID。空ならタグなしのリンクにする。</param>
    public static string BuildJson(IReadOnlyList<SeriesShopItem> rows, string amazonAssociateTag)
    {
        var items = rows
            .Where(r => r.IsEnabled && r.IsBuyable == true)
            .OrderBy(r => r.DisplayOrder).ThenBy(r => r.ShopItemId)
            .Select(r => new
            {
                series = r.SeriesId,
                title = r.Title,
                media = r.MediaLabel,
                url = AmazonUrl(r.Asin, amazonAssociateTag),
                image = r.ImageUrl ?? "",
                // PREORDER（予約受付中）か IN_STOCK（在庫あり）。枠の札の出し分けに使う。
                status = r.AvailabilityType ?? "",
                release = r.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
                detail = string.IsNullOrEmpty(r.ProductCatalogNo) ? "" : PathUtil.ProductUrl(r.ProductCatalogNo),
            })
            .ToList();
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    /// <summary>ファイルの中身を UTF-8（BOM なし）のバイト列で返す。</summary>
    public static byte[] BuildBytes(IReadOnlyList<SeriesShopItem> rows, string amazonAssociateTag)
        => new UTF8Encoding(false).GetBytes(BuildJson(rows, amazonAssociateTag));

    /// <summary>商品ページの URL。トラッキング ID があれば付ける。</summary>
    private static string AmazonUrl(string asin, string tag)
    {
        string url = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(asin);
        if (!string.IsNullOrEmpty(tag)) url += "?tag=" + Uri.EscapeDataString(tag);
        return url;
    }
}
