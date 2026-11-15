using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 販売中の商品のファイル（<c>/shop/{作品 ID}.json</c> と、トップページ用の <c>/shop/home.json</c>）の中身を組み立てる。
/// 作品詳細・各話の詳細・トップページの枠は、ページを開いたときに自分のファイルを読んで描く（<c>shop-box.js</c>）。
/// 売り切れ・絶版になった商品をすぐ下げ、また売られ始めたらすぐ戻すため、枠の中身は HTML に焼き込まず、
/// 変わったファイルだけを上げ直せば済むようにしてある（<see cref="ShopItemsPublisher"/>）。
/// 全体のビルドも同じ中身を出力に書くので、どちらが書いても同じファイルになる。
/// <para>
/// 候補は、手で入れた行（<c>series_shop_items</c>）と、作品に結び付く音楽商品（<see cref="ShopProductCandidate"/>）のうち、
/// Amazon で売られていると確かめたもの。1 つの枠に出すのは <see cref="MaxItems"/> 件までで、次の順に選ぶ：
/// 発売前（予約受付中）の手で入れた行 → 発売前の音楽商品（発売日の近い順）→ 発売済みの手で入れた行 →
/// 発売済みのボーカルベストとサントラ（新しい順）→ 発売済みのそれ以外の音楽商品（新しい順）。
/// </para>
/// </summary>
internal static class ShopItemsFile
{
    /// <summary>出力のルートからのフォルダ名（S3 のキーの接頭辞も同じ）。</summary>
    public const string DirectoryName = "shop";

    /// <summary>トップページの枠のファイル名（拡張子なし）。</summary>
    public const string HomeFileStem = "home";

    /// <summary>1 つの枠に出す商品の上限。</summary>
    public const int MaxItems = 4;

    /// <summary>発売済みの商品の中で先に出す種別（ボーカルベストとサントラ）。</summary>
    private static readonly HashSet<string> PreferredKindCodes = new(StringComparer.Ordinal)
    {
        "VOCAL_BEST", "VOCAL_BEST_BOX", "OST", "OST_MOVIE",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>出力のルートからの相対パス（S3 のキー）。</summary>
    public static string RelativePath(string fileStem) => $"{DirectoryName}/{fileStem}.json";

    /// <summary>
    /// 枠の置き場を出す作品の ID（手で入れた行か、候補の音楽商品がある作品）。
    /// 売られているかどうかにかかわらず入る（置き場の中身は閲覧時に描くので、売られていない間は何も出ない）。
    /// </summary>
    public static IReadOnlySet<int> SeriesIds(IReadOnlyList<SeriesShopItem> rows, IReadOnlyList<ShopProductCandidate> products)
        => rows.Where(r => r.IsEnabled).Select(r => r.SeriesId)
            .Concat(products.Select(p => p.SeriesId))
            .ToHashSet();

    /// <summary>
    /// 全ファイルの中身を「相対パス → JSON」で返す（パスの順）。置き場を出す作品ごとに 1 つと、トップページ用に 1 つ。
    /// 出す商品が無い作品のファイルも、空の一覧で必ず作る（ページがファイルを読めずに終わらないようにする）。
    /// 中身は行の内容だけで決まる（時刻を入れない）ので、状態が変わらなければ同じバイト列になる。
    /// </summary>
    /// <param name="rows">手で入れた行のうち、出す設定の行。</param>
    /// <param name="products">候補の音楽商品（作品 × 商品）。</param>
    /// <param name="amazonAssociateTag">Amazon アソシエイトのトラッキング ID。空ならタグなしのリンクにする。</param>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildFiles(
        IReadOnlyList<SeriesShopItem> rows, IReadOnlyList<ShopProductCandidate> products, string amazonAssociateTag)
    {
        var entries = new List<Entry>();
        foreach (var r in rows.Where(r => r.IsEnabled && r.IsBuyable == true))
        {
            entries.Add(new Entry
            {
                SeriesId = r.SeriesId,
                IsManual = true,
                ManualOrder = r.DisplayOrder,
                ManualId = r.ShopItemId,
                Asin = r.Asin,
                Title = r.Title,
                Media = r.MediaLabel,
                ImageUrl = r.ImageUrl ?? "",
                Status = r.AvailabilityType ?? "",
                ReleaseDate = r.ReleaseDate,
                DetailUrl = string.IsNullOrEmpty(r.ProductCatalogNo) ? "" : PathUtil.ProductUrl(r.ProductCatalogNo),
            });
        }
        foreach (var p in products.Where(p => p.IsBuyable == true))
        {
            entries.Add(new Entry
            {
                SeriesId = p.SeriesId,
                Asin = p.Asin,
                Title = p.Title,
                Media = "CD",
                ImageUrl = p.ImageUrl ?? "",
                Status = p.AvailabilityType ?? "",
                ReleaseDate = p.ReleaseDate,
                DetailUrl = PathUtil.ProductUrl(p.ProductCatalogNo),
                IsPreferredKind = PreferredKindCodes.Contains(p.KindCode),
            });
        }

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var bySeries = entries.ToLookup(e => e.SeriesId);
        foreach (int seriesId in SeriesIds(rows, products))
            files[RelativePath(seriesId.ToString(System.Globalization.CultureInfo.InvariantCulture))] =
                ToJson(Pick(bySeries[seriesId]), amazonAssociateTag);
        // トップページは全作品を通して同じ決まりで選ぶ。
        files[RelativePath(HomeFileStem)] = ToJson(Pick(entries), amazonAssociateTag);
        return files.ToList();
    }

    /// <summary>決まりの順に並べ、同じ ASIN の重なりを除いて、上限の件数までを返す。</summary>
    private static List<Entry> Pick(IEnumerable<Entry> candidates)
    {
        var ordered = candidates
            .OrderBy(e => e.Rank)
            // 手で入れた行は並び順。音楽商品は、発売前なら発売日の近い順、発売済みなら新しい順。
            .ThenBy(e => e.IsManual ? e.ManualOrder : 0)
            .ThenBy(e => e.IsManual ? e.ManualId : 0)
            .ThenBy(e => e.IsPreorder ? (e.ReleaseDate ?? DateTime.MaxValue).Ticks : -(e.ReleaseDate ?? DateTime.MinValue).Ticks)
            .ThenBy(e => e.Asin, StringComparer.Ordinal);

        var picked = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in ordered)
        {
            if (!seen.Add(e.Asin)) continue;
            picked.Add(e);
            if (picked.Count == MaxItems) break;
        }
        return picked;
    }

    private static string ToJson(List<Entry> picked, string tag)
    {
        var items = picked.Select(e => new
        {
            title = e.Title,
            media = e.Media,
            url = AmazonUrl(e.Asin, tag),
            image = e.ImageUrl,
            // PREORDER（予約受付中）か、IN_STOCK・IN_STOCK_SCARCE（在庫あり・残りわずか）。枠の札の出し分けに使う。
            status = e.Status,
            release = e.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
            detail = e.DetailUrl,
        });
        return JsonSerializer.Serialize(new { items }, JsonOptions);
    }

    /// <summary>ファイルの中身を UTF-8（BOM なし）のバイト列にする。</summary>
    public static byte[] ToBytes(string json) => new UTF8Encoding(false).GetBytes(json);

    /// <summary>商品ページの URL。トラッキング ID があれば付ける。</summary>
    private static string AmazonUrl(string asin, string tag)
    {
        string url = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(asin);
        if (!string.IsNullOrEmpty(tag)) url += "?tag=" + Uri.EscapeDataString(tag);
        return url;
    }

    /// <summary>枠の候補 1 件（手で入れた行か、音楽商品）。</summary>
    private sealed class Entry
    {
        public int SeriesId { get; init; }
        public bool IsManual { get; init; }
        public int ManualOrder { get; init; }
        public int ManualId { get; init; }
        public string Asin { get; init; } = "";
        public string Title { get; init; } = "";
        public string Media { get; init; } = "";
        public string ImageUrl { get; init; } = "";
        public string Status { get; init; } = "";
        public DateTime? ReleaseDate { get; init; }
        public string DetailUrl { get; init; } = "";
        public bool IsPreferredKind { get; init; }

        /// <summary>発売前か（Amazon が予約受付中と返した）。</summary>
        public bool IsPreorder => string.Equals(Status, "PREORDER", StringComparison.OrdinalIgnoreCase);

        /// <summary>選ぶ順の段（小さいほど先）。</summary>
        public int Rank => (IsPreorder, IsManual, IsPreferredKind) switch
        {
            (true, true, _) => 0,
            (true, false, _) => 1,
            (false, true, _) => 2,
            (false, false, true) => 3,
            _ => 4,
        };
    }
}
