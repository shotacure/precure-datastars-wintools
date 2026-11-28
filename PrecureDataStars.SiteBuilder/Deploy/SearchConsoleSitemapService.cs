using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// 本番デプロイで更新日の進んだページがあった回に、Search Console の API でサイトマップ（<c>{BaseUrl}/sitemap.xml</c>）を送り直す。
/// <list type="bullet">
///   <item>鍵はサービスアカウントの JSON（<see cref="BuildConfig.SearchConsoleKeyFile"/>）。スコープ <c>webmasters</c>（送る用）でトークンを取る。
///   署名は .NET の標準の RSA（RS256）で行い、外の部品は使わない。</item>
///   <item>送り先は <c>PUT https://www.googleapis.com/webmasters/v3/sites/{プロパティ}/sitemaps/{サイトマップの URL}</c>（<see cref="BuildConfig.SearchConsoleSite"/>）。</item>
///   <item>結果は「Sitemap 送信     : 成功／失敗（理由）」の 1 行で出す（deploy.ps1 がデプロイの記録に写す）。失敗してもデプロイの結果は変えない。</item>
///   <item>鍵かプロパティが未設定なら何もしない。</item>
/// </list>
/// </summary>
public sealed class SearchConsoleSitemapService
{
    private const string Scope = "https://www.googleapis.com/auth/webmasters";
    private const string ApiBase = "https://www.googleapis.com/webmasters/v3/sites/";

    private readonly BuildConfig _config;
    private readonly BuildLogger _logger;

    public SearchConsoleSitemapService(BuildConfig config, BuildLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 更新日が進んだページがあれば（<paramref name="pagesChanged"/> が 1 以上）、サイトマップを送り直す。
    /// 送らなかった・送れたなら true、送れなかったら false（警告にとどめる）。
    /// </summary>
    public async Task<bool> RunAsync(int pagesChanged, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.SearchConsoleKeyFile) || string.IsNullOrEmpty(_config.SearchConsoleSite)
            || string.IsNullOrEmpty(_config.BaseUrl)) return true;
        if (pagesChanged <= 0)
        {
            _logger.Info("Sitemap 送信     : 更新日の進んだページが無いので送らない");
            return true;
        }

        string sitemapUrl = _config.BaseUrl + "/sitemap.xml";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string token = await FetchAccessTokenAsync(http, _config.SearchConsoleKeyFile, ct).ConfigureAwait(false);
            var url = ApiBase + Uri.EscapeDataString(_config.SearchConsoleSite) + "/sitemaps/" + Uri.EscapeDataString(sitemapUrl);
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _logger.Info($"Sitemap 送信     : 成功（HTTP {(int)res.StatusCode}。{sitemapUrl}）");
                return true;
            }
            string body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string reason = (int)res.StatusCode == 403 ? "権限なし" : $"HTTP {(int)res.StatusCode}";
            _logger.Warn($"Sitemap 送信     : 失敗（{reason}）{Shorten(body)}");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Warn($"Sitemap 送信     : 失敗（{ex.GetType().Name}: {Shorten(ex.Message)}）");
            return false;
        }
    }

    /// <summary>
    /// サービスアカウントの鍵で JWT（RS256）を作り、トークンの窓口（鍵の <c>token_uri</c>）でアクセストークンに替える。
    /// 鍵の中身（秘密鍵）は出力に出さない。
    /// </summary>
    private static async Task<string> FetchAccessTokenAsync(HttpClient http, string keyFile, CancellationToken ct)
    {
        using var key = JsonDocument.Parse(await File.ReadAllTextAsync(keyFile, ct).ConfigureAwait(false));
        string email = key.RootElement.GetProperty("client_email").GetString() ?? "";
        string privateKey = key.RootElement.GetProperty("private_key").GetString() ?? "";
        string tokenUri = key.RootElement.TryGetProperty("token_uri", out var tu) ? tu.GetString() ?? "" : "";
        if (tokenUri.Length == 0) tokenUri = "https://oauth2.googleapis.com/token";

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" }));
        string claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = email,
            ["scope"] = Scope,
            ["aud"] = tokenUri,
            ["iat"] = now,
            ["exp"] = now + 3600
        }));
        string unsigned = header + "." + claims;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        string signature = Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = unsigned + "." + signature
        });
        using var res = await http.PostAsync(tokenUri, form, ct).ConfigureAwait(false);
        string body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"トークンを取れません（HTTP {(int)res.StatusCode}）{Shorten(body)}");
        using var tokenDoc = JsonDocument.Parse(body);
        return tokenDoc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("トークンが空です");
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>応答の本文を 1 行・200 字までに縮める（ログに長い JSON を流さない）。</summary>
    private static string Shorten(string text)
    {
        string oneLine = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }
}
