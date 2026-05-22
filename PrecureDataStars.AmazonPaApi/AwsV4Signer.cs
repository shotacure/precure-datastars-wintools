#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>
/// AWS Signature Version 4 (SigV4) を PA-API 5.0 のリクエストに対して計算するヘルパ。
/// PA-API は AWS の汎用 SigV4 を採用しているが、サービス名は <c>ProductAdvertisingAPI</c>、
/// リージョンは marketplace に対応するもの（日本: <c>us-west-2</c>）を使う規約になっている。
/// <para>
/// SigV4 の手順は以下の通り：
///   1. Canonical Request の組み立て（HTTPMethod / URI / QueryString / Headers / Signed Headers / Payload Hash）
///   2. String to Sign の組み立て（algorithm / amzDate / credentialScope / canonicalRequestHash）
///   3. Signing Key の派生（dateKey → regionKey → serviceKey → signingKey）
///   4. Signature の計算（HmacSHA256(signingKey, stringToSign)）
///   5. Authorization ヘッダの組み立て
/// </para>
/// 本クラスはステートレスで、メソッド呼び出しごとに必要な情報を引数で受け取って署名済みヘッダ集合を返す。
/// </summary>
public static class AwsV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "ProductAdvertisingAPI";

    /// <summary>
    /// PA-API POST リクエスト用の SigV4 ヘッダ群を計算して返す。
    /// 戻り値の辞書には <c>x-amz-date</c> / <c>host</c> / <c>x-amz-target</c> / <c>content-type</c> /
    /// <c>content-encoding</c> / <c>Authorization</c> が含まれる。HttpRequestMessage に詰めて送出すること。
    /// </summary>
    /// <param name="host">PA-API ホスト名（例: <c>webservices.amazon.co.jp</c>）。</param>
    /// <param name="region">AWS リージョン（日本: <c>us-west-2</c>）。</param>
    /// <param name="path">リクエストパス（例: <c>/paapi5/getitems</c>）。</param>
    /// <param name="target">x-amz-target（例: <c>com.amazon.paapi5.v1.ProductAdvertisingAPIv1.GetItems</c>）。</param>
    /// <param name="payload">リクエストボディ（UTF-8 の JSON）。</param>
    /// <param name="accessKey">PA-API のアクセスキー。</param>
    /// <param name="secretKey">PA-API のシークレットキー。</param>
    /// <param name="utcNow">署名タイムスタンプ（テスト用。本番は <c>DateTime.UtcNow</c>）。</param>
    public static IDictionary<string, string> SignPostRequest(
        string host,
        string region,
        string path,
        string target,
        string payload,
        string accessKey,
        string secretKey,
        DateTime utcNow)
    {
        // ISO 8601 basic format（YYYYMMDDTHHMMSSZ）と日付部分のみ（YYYYMMDD）の 2 表記を用意する。
        string amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // 署名対象ヘッダ（小文字・辞書順）。PA-API では下記 5 つを Signed Headers に含めるのが定石。
        // content-encoding=amz-1.0 は PA-API 5.0 のドキュメントどおり固定値で送る。
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-encoding"] = "amz-1.0",
            ["content-type"] = "application/json; charset=utf-8",
            ["host"] = host,
            ["x-amz-date"] = amzDate,
            ["x-amz-target"] = target,
        };

        // Canonical Headers と Signed Headers を組み立てる。
        var canonicalHeadersSb = new StringBuilder();
        var signedHeadersSb = new StringBuilder();
        bool first = true;
        foreach (var kv in headers)
        {
            canonicalHeadersSb.Append(kv.Key).Append(':').Append(kv.Value.Trim()).Append('\n');
            if (!first) signedHeadersSb.Append(';');
            signedHeadersSb.Append(kv.Key);
            first = false;
        }
        string canonicalHeaders = canonicalHeadersSb.ToString();
        string signedHeaders = signedHeadersSb.ToString();

        // Payload Hash（本文の SHA-256 を hex で）。
        string payloadHash = ToHex(Sha256(Encoding.UTF8.GetBytes(payload)));

        // Canonical Request: POST の場合 QueryString は空文字。
        string canonicalRequest = string.Join("\n", new[]
        {
            "POST",
            path,
            "",                  // CanonicalQueryString（POST + JSON body のため空）
            canonicalHeaders,    // 末尾改行込み
            signedHeaders,
            payloadHash,
        });

        // Credential Scope と String to Sign。
        string credentialScope = $"{dateStamp}/{region}/{Service}/aws4_request";
        string stringToSign = string.Join("\n", new[]
        {
            Algorithm,
            amzDate,
            credentialScope,
            ToHex(Sha256(Encoding.UTF8.GetBytes(canonicalRequest))),
        });

        // Signing Key の派生（4 段階の HMAC）。
        byte[] kDate    = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), Encoding.UTF8.GetBytes(dateStamp));
        byte[] kRegion  = HmacSha256(kDate, Encoding.UTF8.GetBytes(region));
        byte[] kService = HmacSha256(kRegion, Encoding.UTF8.GetBytes(Service));
        byte[] kSigning = HmacSha256(kService, Encoding.UTF8.GetBytes("aws4_request"));

        // Signature 計算と Authorization ヘッダ組み立て。
        string signature = ToHex(HmacSha256(kSigning, Encoding.UTF8.GetBytes(stringToSign)));
        string authorization =
            $"{Algorithm} Credential={accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        // 呼び出し側は HttpRequestMessage.Headers と Content.Headers に振り分けて入れる。
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-date"] = amzDate,
            ["x-amz-target"] = target,
            ["content-encoding"] = "amz-1.0",
            ["content-type"] = "application/json; charset=utf-8",
            ["Authorization"] = authorization,
        };
    }

    /// <summary>SHA-256 ハッシュを計算する。</summary>
    private static byte[] Sha256(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>HMAC-SHA256 を計算する。</summary>
    private static byte[] HmacSha256(byte[] key, byte[] data)
    {
        return HMACSHA256.HashData(key, data);
    }

    /// <summary>バイト配列を小文字 16 進文字列に変換する。</summary>
    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
