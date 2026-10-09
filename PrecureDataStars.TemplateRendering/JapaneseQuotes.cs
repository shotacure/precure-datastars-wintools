using System.Text;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// かぎ括弧「」の入れ子の表記。「」で囲んで出す文字列（曲名・サブタイトル・トラック名など）の中に「」があれば、
/// 日本語の表記の決まりどおり内側を二重かぎ括弧『』にする（例:「笑うが勝ち!」でGO! →「『笑うが勝ち!』でGO!」）。
/// DB の文字列はそのまま持ち、表示するときだけ変える。SiteBuilder と Catalog のプレビュー、役職テンプレの描画で共有する。
/// </summary>
public static class JapaneseQuotes
{
    /// <summary>「」で囲む中に置く文字列の「」を『』にする。null は空文字にする。</summary>
    public static string InQuotes(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.IndexOf('「') < 0 && text.IndexOf('」') < 0) return text;
        return text.Replace('「', '『').Replace('」', '』');
    }

    /// <summary>
    /// 出力済みの文字列の末尾が「」の中にあるか（開きかぎ括弧が閉じかぎ括弧より多いか）を返す。
    /// HTML タグの中（&lt; から &gt; まで）の文字は数えない。役職テンプレの描画で、値を差し込む位置が
    /// テンプレの文字の「」の中かどうかを判定するのに使う。
    /// </summary>
    public static bool IsInsideQuotes(StringBuilder output)
    {
        int depth = 0;
        bool inTag = false;
        for (int i = 0; i < output.Length; i++)
        {
            char c = output[i];
            if (inTag) { if (c == '>') inTag = false; continue; }
            if (c == '<') { inTag = true; continue; }
            if (c == '「') depth++;
            else if (c == '」' && depth > 0) depth--;
        }
        return depth > 0;
    }
}
