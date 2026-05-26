#nullable enable
using System;
using System.Configuration;
using System.Net.Http;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>
/// App.config の <c>appSettings</c> から Creators API のクレデンシャルを読み出して
/// <see cref="PaApiClient"/> を構築するヘルパ。
/// 利用側プロジェクト（Catalog / AmazonSync）の App.config には下記キーを定義する：
/// <list type="bullet">
///   <item><c>PaApi.CredentialId</c>: Creators API の Credential ID</item>
///   <item><c>PaApi.CredentialSecret</c>: 同 Credential Secret</item>
///   <item><c>PaApi.CredentialVersion</c>: 同 Version 文字列（例 <c>"2.3"</c> や <c>"3.3"</c>）。
///     Associates Central で発行された Application の Version 列をそのまま入れる。
///     2.x は Cognito 経由、3.x は Login with Amazon 経由でトークンを取得し、API 呼び出し時の
///     Authorization ヘッダにも Version 値（2.x のみ併記）を反映する。</item>
///   <item><c>PaApi.PartnerTag</c>: アソシエイトタグ（例 <c>yourtag-22</c>）</item>
///   <item><c>PaApi.Marketplace</c>: マーケットプレイスドメイン（既定 <c>www.amazon.co.jp</c>）</item>
/// </list>
/// </summary>
public static class PaApiClientFactory
{
    /// <summary>
    /// <see cref="HttpClient"/> は本プロセス内で 1 個共有する。Creators API は OAuth 2.0 のトークン取得 +
    /// API 呼び出しで 2 種類の HTTP 通信先があるが、ホスト切替は <see cref="HttpRequestMessage"/> 側の
    /// 絶対 URL で扱うため単一 <see cref="HttpClient"/> で問題ない。
    /// </summary>
    private static readonly HttpClient SharedHttp = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    /// <summary>
    /// App.config から設定を読み出して <see cref="PaApiClient"/> を構築する。
    /// 必須キー（CredentialId / CredentialSecret / CredentialVersion / PartnerTag）のうち
    /// 1 つでも空なら null を返し、呼び出し側で「Creators API キー未設定」として扱えるようにする。
    /// </summary>
    public static PaApiClient? TryCreateFromAppConfig()
    {
        string credId = (ConfigurationManager.AppSettings["PaApi.CredentialId"] ?? "").Trim();
        string credSecret = (ConfigurationManager.AppSettings["PaApi.CredentialSecret"] ?? "").Trim();
        string credVersion = (ConfigurationManager.AppSettings["PaApi.CredentialVersion"] ?? "").Trim();
        string partnerTag = (ConfigurationManager.AppSettings["PaApi.PartnerTag"] ?? "").Trim();
        if (string.IsNullOrEmpty(credId)
            || string.IsNullOrEmpty(credSecret)
            || string.IsNullOrEmpty(credVersion)
            || string.IsNullOrEmpty(partnerTag))
        {
            return null;
        }

        string marketplace = (ConfigurationManager.AppSettings["PaApi.Marketplace"] ?? "www.amazon.co.jp").Trim();
        var tokenProvider = new OAuth2TokenProvider(SharedHttp, credId, credSecret, credVersion);
        return new PaApiClient(SharedHttp, tokenProvider, partnerTag, marketplace);
    }
}
