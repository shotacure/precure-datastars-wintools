using System.Text.Encodings.Web;
using System.Text.Json;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// Schema.org の JSON-LD 構造化データを構築するための共通ユーティリティ。
/// <para>
/// 各 Generator がページ種別に応じた JSON-LD を組み立てる際の共通の直列化設定を提供する。
/// 日本語をエスケープしないために <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> を使い、
/// インデント無し（HTML 内に埋め込むため、コンパクトな 1 行 JSON で十分）で出力する。
/// </para>
/// <para>
/// 入力は匿名型または <see cref="Dictionary{TKey, TValue}"/> を想定。プロパティ名は Schema.org 仕様に
/// 合わせて <c>"@context"</c> や <c>"@type"</c> など <c>@</c> 始まりのキーを Dictionary キーとして扱う。
/// </para>
/// </summary>
public static class JsonLdBuilder
{
    /// <summary>JSON-LD 直列化用の JsonSerializerOptions（共通設定）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        // null プロパティは出力しない（任意プロパティが NULL のときに不要な "key": null を出さない）。
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 指定オブジェクトを Schema.org 仕様の JSON 文字列に直列化する。
    /// </summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}