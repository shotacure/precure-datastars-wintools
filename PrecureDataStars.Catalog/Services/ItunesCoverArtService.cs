#nullable enable
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.Catalog.Services;

/// <summary>
/// iTunes Lookup API からアルバムのジャケット画像 URL を取得するサービス（フェーズ 1）。
/// <para>
/// 認証不要・無料の公開 API（<c>https://itunes.apple.com/lookup</c>）を用い、
/// Apple Music のアルバム ID（<c>collectionId</c>）からアートワーク URL を引く。
/// API が返す <c>artworkUrl100</c>（100x100）の寸法表記を高解像度へ置換して使う。
/// 画像実体は保存せず、URL のみをキャッシュするホットリンク運用とする。
/// </para>
/// <para>
/// PA-API は Amazon のサイト審査通過＋初回適格売上が前提のため、立ち上げ前の現段階では
/// 使用できない。本サービスはその前段（審査素材としてのコンテンツ整備）を担う。
/// PA-API 開通後は取得元 <c>amazon</c> のサービスを別途追加し、本サービスと併存させる想定。
/// </para>
/// </summary>
public sealed class ItunesCoverArtService
{
    // プロセス内で 1 つの HttpClient を共有する（ソケット枯渇回避の定石）。
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        // 既定 UA を明示（一部 CDN/プロキシが UA 無しを弾くため）。
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PrecureDataStars-Catalog/1.0");
        return c;
    }

    /// <summary>
    /// 取得結果。<see cref="ImageUrl"/> が null の場合は「該当なし／取得失敗」を意味する。
    /// </summary>
    /// <param name="ImageUrl">高解像度ジャケット画像 URL（取得できなければ null）。</param>
    /// <param name="Source">取得元識別子（常に <c>apple</c>）。</param>
    public readonly record struct Result(string? ImageUrl, string Source);

    /// <summary>
    /// 指定の Apple Music アルバム ID からジャケット画像 URL を取得する。
    /// </summary>
    /// <param name="appleAlbumId">Apple Music のアルバム ID（collectionId）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>
    /// 取得できれば <see cref="Result.ImageUrl"/> に高解像度 URL を格納した結果。
    /// 該当なし・通信失敗・レスポンス不正のときは <see cref="Result.ImageUrl"/> が null。
    /// 例外はスローせず、呼び出し側がスキップ判定できるよう Result で表現する。
    /// </returns>
    public async Task<Result> FetchAsync(string appleAlbumId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appleAlbumId))
            return new Result(null, "apple");

        // country=JP で日本ストアのメタデータを優先。entity=album でアルバムに限定。
        string url =
            "https://itunes.apple.com/lookup?id=" + Uri.EscapeDataString(appleAlbumId.Trim()) +
            "&country=JP&entity=album";

        try
        {
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new Result(null, "apple");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() == 0)
            {
                return new Result(null, "apple");
            }

            // 先頭の結果から artworkUrl100 を取り出し、寸法表記を高解像度へ置換する。
            // 例: ".../source/100x100bb.jpg" → ".../source/600x600bb.jpg"
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("artworkUrl100", out var art)
                    && art.ValueKind == JsonValueKind.String)
                {
                    string? art100 = art.GetString();
                    if (!string.IsNullOrEmpty(art100))
                    {
                        string hi = art100.Replace("100x100bb", "600x600bb");
                        return new Result(hi, "apple");
                    }
                }
            }

            return new Result(null, "apple");
        }
        catch (OperationCanceledException)
        {
            // キャンセルは呼び出し側へ伝搬させる。
            throw;
        }
        catch (Exception)
        {
            // 通信エラー・JSON 不正などは「取得失敗」として扱い、処理全体は止めない。
            return new Result(null, "apple");
        }
    }
}
