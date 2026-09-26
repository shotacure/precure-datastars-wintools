namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 書籍の流通コードのうち、DB に持たず他の値から導けるものを組み立てるヘルパー。
/// <list type="bullet">
///   <item><description>ISBN-10：978 始まりの ISBN-13 から導く（979 始まりには ISBN-10 が存在しない）。</description></item>
///   <item><description>書籍 JAN の 2 段目：<c>192</c> + Cコードの数字 4 桁 + 本体価格（税抜）5 桁 + チェックデジット。</description></item>
/// </list>
/// </summary>
public static class BookCodes
{
    /// <summary>ISBN-13 から ISBN-10 を導く。978 始まりの 13 桁数字でなければ空文字。</summary>
    public static string ToIsbn10(string? isbn13)
    {
        if (isbn13 is null || isbn13.Length != 13 || !isbn13.All(char.IsAsciiDigit) || !isbn13.StartsWith("978", StringComparison.Ordinal))
            return "";
        var body = isbn13.Substring(3, 9);
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (10 - i) * (body[i] - '0');
        int check = (11 - sum % 11) % 11;
        return body + (check == 10 ? "X" : check.ToString());
    }

    /// <summary>
    /// 書籍 JAN コードの 2 段目（分類・価格コード）を導く。Cコード（"C" + 4 桁）と税抜価格（0〜99999 円）が
    /// 揃わなければ空文字。
    /// </summary>
    public static string ToBookJanSecondRow(string? cCode, int? priceExTax)
    {
        if (cCode is null || cCode.Length != 5 || cCode[0] != 'C' || !cCode.Skip(1).All(char.IsAsciiDigit)) return "";
        if (priceExTax is not int price || price < 0 || price > 99999) return "";
        var body = "192" + cCode.Substring(1) + price.ToString("D5");
        return body + Ean13CheckDigit(body);
    }

    /// <summary>EAN-13 の先頭 12 桁からチェックデジットを計算する。</summary>
    private static char Ean13CheckDigit(string first12)
    {
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (char)('0' + (10 - sum % 10) % 10);
    }
}
