#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>
/// Amazon Creators API の OAuth 2.0 アクセストークン取得・キャッシュ管理を担う。
/// <para>
/// 旧 PA-API 5.0 の AWS Signature V4 では各リクエストごとに署名する方式だったが、Creators API は
/// 事前に取得した Bearer トークン（有効期限 3600 秒）をヘッダに乗せる方式に変わっている。本クラスは
/// トークンを <b>1 度取得したらメモリにキャッシュ</b>し、有効期限が安全マージン以内に近づいたら
/// 自動的に再取得する。
/// </para>
/// <para>
/// クレデンシャルバージョンに応じて 2 系統の token endpoint を使い分ける：
/// </para>
/// <list type="bullet">
///   <item><b>v2.x</b>: AWS Cognito（リージョン別の <c>creatorsapi.auth.*.amazoncognito.com</c>）。
///     リクエストは <c>application/x-www-form-urlencoded</c>、Basic 認証ヘッダで client_id:client_secret。
///     スコープは <c>creatorsapi/default</c>。</item>
///   <item><b>v3.x</b>: Login with Amazon（リージョン別の <c>api.amazon.*</c>）。
///     リクエストは <c>application/json</c> ボディに client_id / client_secret を埋め込む。
///     スコープは <c>creatorsapi::default</c>（v2 の <c>/</c> ではなく <c>::</c> 区切り）。</item>
/// </list>
/// <para>
/// API 呼び出し側（<see cref="PaApiClient"/>）はクレデンシャルバージョンを参照して
/// <c>Authorization: Bearer &lt;token&gt;[, Version &lt;ver&gt;]</c> ヘッダを組み立てる
/// （v2.x のみ Version 値を併記、v3.x は Bearer のみ）。
/// </para>
/// </summary>
public sealed class OAuth2TokenProvider
{
    private readonly HttpClient _http;
    private readonly string _credentialId;
    private readonly string _credentialSecret;
    private readonly string _credentialVersion;
    private readonly string _tokenEndpoint;
    private readonly bool _isV3;

    /// <summary>有効期限切れの直前に再取得するためのマージン。本マージン以内に迫ったらキャッシュを捨てて再取得する。</summary>
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(60);

    private string? _cachedToken;
    private DateTime _cachedTokenExpiresAtUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// 新しい <see cref="OAuth2TokenProvider"/> を構築する。
    /// </summary>
    /// <param name="http">HTTP クライアント（呼び出し側で共有する想定）。</param>
    /// <param name="credentialId">Associates Central で発行された Credential ID。</param>
    /// <param name="credentialSecret">同 Credential Secret。</param>
    /// <param name="credentialVersion">同 Version 文字列（例 <c>"2.3"</c> / <c>"3.3"</c>）。
    /// バージョン番号からトークン取得エンドポイントと認証方式（Cognito か LwA か）を自動決定する。</param>
    public OAuth2TokenProvider(HttpClient http, string credentialId, string credentialSecret, string credentialVersion)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentialId = credentialId ?? throw new ArgumentNullException(nameof(credentialId));
        _credentialSecret = credentialSecret ?? throw new ArgumentNullException(nameof(credentialSecret));
        _credentialVersion = credentialVersion ?? throw new ArgumentNullException(nameof(credentialVersion));
        (_tokenEndpoint, _isV3) = ResolveTokenEndpoint(credentialVersion);
    }

    /// <summary>クレデンシャルバージョン文字列。API ヘッダの Version 値として呼び出し側が利用する（v2.x のみ）。</summary>
    public string CredentialVersion => _credentialVersion;

    /// <summary>v3.x クレデンシャルかどうか。v3.x は Authorization ヘッダに Version を併記しない。</summary>
    public bool IsV3Credential => _isV3;

    /// <summary>
    /// アクセストークンを取得する。キャッシュが生きていればそれを返し、期限切れまたは安全マージン以内なら
    /// 再取得する。多スレッドからの同時呼び出しは <see cref="SemaphoreSlim"/> で直列化する
    /// （並行発射した複数リクエストが二重にトークンを取りに行かないようにするため。Cognito v2.x の
    /// token endpoint は 5 分間 300 リクエストのレート制限があるので、キャッシュは特に重要）。
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        // ロック外で早期判定（多くの呼び出しはここで return できる）。
        if (_cachedToken is not null && DateTime.UtcNow + SafetyMargin < _cachedTokenExpiresAtUtc)
            return _cachedToken;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // ロック取得後の二重チェック（他スレッドが先に再取得済みの可能性）。
            if (_cachedToken is not null && DateTime.UtcNow + SafetyMargin < _cachedTokenExpiresAtUtc)
                return _cachedToken;

            var (token, expiresInSec) = _isV3
                ? await FetchTokenV3Async(ct).ConfigureAwait(false)
                : await FetchTokenV2Async(ct).ConfigureAwait(false);

            _cachedToken = token;
            _cachedTokenExpiresAtUtc = DateTime.UtcNow + TimeSpan.FromSeconds(expiresInSec);
            return token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>v2.x クレデンシャル用：Cognito、form-urlencoded、Basic 認証ヘッダ、スコープ <c>creatorsapi/default</c>。</summary>
    private async Task<(string Token, int ExpiresInSec)> FetchTokenV2Async(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint);
        string basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_credentialId}:{_credentialSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        req.Content = new StringContent(
            "grant_type=client_credentials&scope=creatorsapi/default",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");
        return await ExecuteTokenRequestAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>v3.x クレデンシャル用：Login with Amazon、JSON、ボディに client_id / client_secret を埋め込み、スコープ <c>creatorsapi::default</c>。</summary>
    private async Task<(string Token, int ExpiresInSec)> FetchTokenV3Async(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint);
        var bodyObj = new
        {
            grant_type = "client_credentials",
            client_id = _credentialId,
            client_secret = _credentialSecret,
            scope = "creatorsapi::default"
        };
        req.Content = new StringContent(
            JsonSerializer.Serialize(bodyObj),
            Encoding.UTF8,
            "application/json");
        return await ExecuteTokenRequestAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>共通実行＋レスポンスパース。HTTP エラーは詳細を含む例外で投げ直す。</summary>
    private async Task<(string Token, int ExpiresInSec)> ExecuteTokenRequestAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        string respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Creators API token request failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{respBody}");
        }

        using var doc = JsonDocument.Parse(respBody);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl)
            || tokenEl.ValueKind != JsonValueKind.String
            || tokenEl.GetString() is not string token
            || string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"Creators API token response missing or invalid access_token.\nBody: {respBody}");
        }

        int expiresIn = 3600;
        if (doc.RootElement.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var s))
            expiresIn = s;

        return (token, expiresIn);
    }

    /// <summary>
    /// クレデンシャルバージョンからトークン取得エンドポイントと認証方式を判定する。
    /// 公式の Regional Endpoints 表（Using cURL）に従う：
    /// <list type="bullet">
    ///   <item>v2.1 NA = Cognito us-east-1（US/CA/MX/BR）</item>
    ///   <item>v2.2 EU = Cognito eu-south-2（UK/DE/FR/IT/ES/NL/BE/EG/IN/IE/PL/SA/SE/TR/AE）</item>
    ///   <item>v2.3 FE = Cognito us-west-2（JP/SG/AU）</item>
    ///   <item>v3.1 NA = LwA api.amazon.com</item>
    ///   <item>v3.2 EU = LwA api.amazon.co.uk</item>
    ///   <item>v3.3 FE = LwA api.amazon.co.jp</item>
    /// </list>
    /// クレデンシャルはグローバルに使えるため、ここで決まるのは <b>トークン発行先のみ</b>。実際の
    /// API 呼び出し時のマーケットプレイスは別途 <c>x-marketplace</c> ヘッダで指定する。
    /// </summary>
    private static (string Endpoint, bool IsV3) ResolveTokenEndpoint(string version)
    {
        return version switch
        {
            "2.1" => ("https://creatorsapi.auth.us-east-1.amazoncognito.com/oauth2/token", false),
            "2.2" => ("https://creatorsapi.auth.eu-south-2.amazoncognito.com/oauth2/token", false),
            "2.3" => ("https://creatorsapi.auth.us-west-2.amazoncognito.com/oauth2/token", false),
            "3.1" => ("https://api.amazon.com/auth/o2/token", true),
            "3.2" => ("https://api.amazon.co.uk/auth/o2/token", true),
            "3.3" => ("https://api.amazon.co.jp/auth/o2/token", true),
            _ => throw new InvalidOperationException(
                $"Unknown Creators API credential version '{version}'. "
                + "Expected one of 2.1, 2.2, 2.3, 3.1, 3.2, 3.3 (refer to Associates Central > CreatorsAPI > Application Version column).")
        };
    }
}
