#nullable enable
using System;
using System.Configuration;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>
/// App.config の <c>appSettings</c> から PA-API キーを読み出して <see cref="PaApiClient"/> を構築するヘルパ。
/// 利用側プロジェクト（Catalog / AmazonSync）の App.config には下記キーを定義しておく：
/// <list type="bullet">
///   <item><c>PaApi.AccessKey</c>: PA-API のアクセスキー</item>
///   <item><c>PaApi.SecretKey</c>: PA-API のシークレットキー</item>
///   <item><c>PaApi.PartnerTag</c>: アソシエイトタグ（例 <c>yourtag-22</c>）</item>
///   <item><c>PaApi.Marketplace</c>: マーケットプレイスドメイン（既定 <c>www.amazon.co.jp</c>）</item>
///   <item><c>PaApi.Host</c>: PA-API ホスト名（既定 <c>webservices.amazon.co.jp</c>）</item>
///   <item><c>PaApi.Region</c>: AWS リージョン（既定 <c>us-west-2</c>）</item>
/// </list>
/// </summary>
public static class PaApiClientFactory
{
    /// <summary>
    /// App.config から設定を読み出して <see cref="PaApiClient"/> を構築する。
    /// 必須キー（AccessKey / SecretKey / PartnerTag）のうち 1 つでも空なら null を返し、
    /// 呼び出し側で「PA-API キー未設定」として扱えるようにする。
    /// </summary>
    public static PaApiClient? TryCreateFromAppConfig()
    {
        string access = (ConfigurationManager.AppSettings["PaApi.AccessKey"] ?? "").Trim();
        string secret = (ConfigurationManager.AppSettings["PaApi.SecretKey"] ?? "").Trim();
        string tag = (ConfigurationManager.AppSettings["PaApi.PartnerTag"] ?? "").Trim();
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(tag))
            return null;

        string marketplace = (ConfigurationManager.AppSettings["PaApi.Marketplace"] ?? "www.amazon.co.jp").Trim();
        string host = (ConfigurationManager.AppSettings["PaApi.Host"] ?? "webservices.amazon.co.jp").Trim();
        string region = (ConfigurationManager.AppSettings["PaApi.Region"] ?? "us-west-2").Trim();

        return new PaApiClient(access, secret, tag, marketplace, host, region);
    }
}
