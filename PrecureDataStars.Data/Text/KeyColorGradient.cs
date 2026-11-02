using System.Globalization;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Data.Text;

/// <summary>
/// プリキュアのキーカラーのグラデーション（<c>precures.key_color_gradient</c>）の読み書き。
/// 形式は「<c>#RRGGBB 位置%</c>」をカンマと空白でつないだ左から右への色の並び（2 色以上、位置は 0〜100）。
/// 例: <c>#F1346E 1%, #FF65F8 19%, #8474FF 29%</c>。DB の CHECK 制約 <c>ck_precures_key_color_gradient</c> と同じ形式。
/// サイトは CSS の <c>linear-gradient</c> にそのまま入れ、OGP カードと入力用のデスクトップアプリ（Catalog）の見本は
/// <see cref="TryParse"/> で色と位置に分けて塗る。
/// </summary>
public static partial class KeyColorGradient
{
    /// <summary>グラデーションの 1 つの色の位置。<see cref="Position"/> は 0〜1（左端 0、右端 1）。</summary>
    public readonly record struct Stop(byte R, byte G, byte B, float Position)
    {
        /// <summary>「#RRGGBB」形式の色。</summary>
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    }

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6} [0-9]{1,3}%(, #[0-9A-Fa-f]{6} [0-9]{1,3}%)+$")]
    private static partial Regex FormatRegex();

    /// <summary>DB に入れられる形式か（空は不可）。位置が 100 を超えるもの・左から右へ並んでいないものも不可。</summary>
    public static bool IsValid(string? value) => TryParse(value, out _);

    /// <summary>
    /// 色の並びを読む。形式が違う・位置が 0〜100 に収まらない・位置が左から右へ並んでいないときは false。
    /// </summary>
    public static bool TryParse(string? value, out IReadOnlyList<Stop> stops)
    {
        stops = Array.Empty<Stop>();
        if (string.IsNullOrEmpty(value) || !FormatRegex().IsMatch(value)) return false;

        var list = new List<Stop>();
        int last = -1;
        foreach (var part in value.Split(", "))
        {
            int percent = int.Parse(part.AsSpan(8, part.Length - 9), CultureInfo.InvariantCulture);
            if (percent > 100 || percent < last) return false;
            last = percent;
            list.Add(new Stop(
                byte.Parse(part.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(part.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(part.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                percent / 100f));
        }
        stops = list;
        return true;
    }

    /// <summary>
    /// CSS の左から右への <c>linear-gradient</c>。読めない値なら空文字（呼び出し側は 1 色のキーカラーに戻す）。
    /// 形式を <see cref="TryParse"/> で確かめてから値を入れるので、CSS に任意の文字列が混じることは無い。
    /// </summary>
    public static string ToCssLinearGradient(string? value)
        => TryParse(value, out _) ? $"linear-gradient(to right, {value})" : "";
}
