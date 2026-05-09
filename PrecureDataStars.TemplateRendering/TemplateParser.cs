
using System.Text;
using System.Text.RegularExpressions;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 役職テンプレ DSL のパーサ（v1.2.0 工程 H 追加）。
/// <para>
/// 入力テンプレ文字列を <see cref="TemplateNode"/> の AST にトークナイズして変換する。
/// 想定構文：
/// <list type="bullet">
///   <item><description><c>{NAME}</c> または <c>{NAME:opt1=val1,opt2=val2}</c></description></item>
///   <item><description><c>{#BLOCKS}...{/BLOCKS}</c> または <c>{#BLOCKS:first}...{/BLOCKS:first}</c></description></item>
///   <item><description><c>{?NAME}...{/?NAME}</c></description></item>
/// </list>
/// ネスト：BLOCKS / ? の内側にも他の {NAME} を入れられるが、{#BLOCKS} の中に {#BLOCKS} のような
/// 多重ネストは v1.2.0 ではサポートしない（必要になったら拡張）。
/// </para>
/// <para>
/// 実装：単純な「先頭から走査するステートマシン」。<c>{</c> を見つけたらタグ全体を切り出して解釈、
/// それ以外はリテラルとして蓄積。BLOCKS / ? は対応する閉じタグまでを子とみなす。
/// </para>
/// </summary>
public static class TemplateParser
{
    /// <summary>テンプレ文字列を AST に変換する。</summary>
    public static IReadOnlyList<TemplateNode> Parse(string template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<TemplateNode>();
        int pos = 0;
        return ParseUntil(template, ref pos, terminator: null);
    }

    /// <summary>
    /// <paramref name="template"/> 中の <paramref name="pos"/> 以降を読み進めて、
    /// <paramref name="terminator"/>（例：<c>{/BLOCKS}</c> や <c>{/?NAME}</c>）に
    /// 到達したら手前までの AST を返す。<paramref name="terminator"/> が null の場合は末尾まで。
    /// 終端タグ自体は読み飛ばさず（位置はそのまま）、呼び出し側で消費する。
    /// </summary>
    private static List<TemplateNode> ParseUntil(string template, ref int pos, string? terminator)
    {
        var result = new List<TemplateNode>();
        var literal = new StringBuilder();

        while (pos < template.Length)
        {
            // ターミネータチェック：今の位置がターミネータと一致するなら抜ける
            if (terminator is not null && MatchesAt(template, pos, terminator))
            {
                FlushLiteral(literal, result);
                return result;
            }

            char ch = template[pos];
            if (ch != '{')
            {
                literal.Append(ch);
                pos++;
                continue;
            }

            // '{' を見つけた → タグ全体を切り出す
            int closePos = template.IndexOf('}', pos);
            if (closePos < 0)
            {
                // 閉じ括弧がなければ残りはリテラル
                literal.Append(template, pos, template.Length - pos);
                pos = template.Length;
                break;
            }

            string raw = template.Substring(pos + 1, closePos - pos - 1); // {と}の中身
            FlushLiteral(literal, result);
            pos = closePos + 1;

            // 種別判定
            if (raw.StartsWith("#BLOCKS"))
            {
                // {#BLOCKS} or {#BLOCKS:first} 等
                string filter = ExtractFilter(raw, "#BLOCKS");
                string closeTag = "{/BLOCKS" + (string.IsNullOrEmpty(filter) ? "" : ":" + filter) + "}";
                var body = ParseUntil(template, ref pos, closeTag);
                ConsumeTerminator(template, ref pos, closeTag);
                result.Add(new BlockLoopNode(filter, body));
            }
            else if (raw.StartsWith("#THEME_SONGS"))
            {
                // {#THEME_SONGS} or {#THEME_SONGS:kind=OP+ED} 等（v1.2.0 工程 H-16 で追加）
                // BLOCKS と同じく対応する閉じタグ {/THEME_SONGS} までを子テンプレとして読む。
                // オプション部はコロン以降をそのまま ParseOptions で辞書化する。
                IReadOnlyDictionary<string, string>? opts = null;
                int colonIdx = raw.IndexOf(':');
                if (colonIdx >= 0)
                {
                    opts = ParseOptions(raw.Substring(colonIdx + 1));
                }
                const string closeTag = "{/THEME_SONGS}";
                var body = ParseUntil(template, ref pos, closeTag);
                ConsumeTerminator(template, ref pos, closeTag);
                result.Add(new ThemeSongsLoopNode(opts, body));
            }
            else if (raw.StartsWith("?"))
            {
                // {?NAME} ... {/?NAME}
                string name = raw.Substring(1).Trim();
                string closeTag = "{/?" + name + "}";
                var body = ParseUntil(template, ref pos, closeTag);
                ConsumeTerminator(template, ref pos, closeTag);
                result.Add(new ConditionalNode(name, body));
            }
            else if (raw.StartsWith("/"))
            {
                // 終了タグなのに ParseUntil の terminator と一致しなかった → 構文ミス、リテラル扱い
                result.Add(new LiteralNode("{" + raw + "}"));
            }
            else
            {
                // 通常のプレースホルダ {NAME} または {NAME:opt=val,opt=val}
                int colon = raw.IndexOf(':');
                if (colon < 0)
                {
                    result.Add(new PlaceholderNode(raw.Trim()));
                }
                else
                {
                    string name = raw.Substring(0, colon).Trim();
                    string opts = raw.Substring(colon + 1);
                    var dict = ParseOptions(opts);
                    result.Add(new PlaceholderNode(name, dict));
                }
            }
        }

        FlushLiteral(literal, result);
        return result;
    }

    private static void FlushLiteral(StringBuilder sb, List<TemplateNode> result)
    {
        if (sb.Length > 0)
        {
            result.Add(new LiteralNode(sb.ToString()));
            sb.Clear();
        }
    }

    private static bool MatchesAt(string s, int pos, string what)
    {
        if (pos + what.Length > s.Length) return false;
        return s.AsSpan(pos, what.Length).SequenceEqual(what.AsSpan());
    }

    private static void ConsumeTerminator(string s, ref int pos, string terminator)
    {
        if (MatchesAt(s, pos, terminator))
        {
            pos += terminator.Length;
        }
    }

    /// <summary>
    /// "#BLOCKS" や "#BLOCKS:first" の後ろ部分（フィルタ名）を返す。
    /// 例: <paramref name="raw"/>="#BLOCKS:first", <paramref name="prefix"/>="#BLOCKS" → "first"。
    /// 例: <paramref name="raw"/>="#BLOCKS", <paramref name="prefix"/>="#BLOCKS" → ""。
    /// </summary>
    private static string ExtractFilter(string raw, string prefix)
    {
        if (raw.Length <= prefix.Length) return "";
        if (raw[prefix.Length] != ':') return "";
        return raw.Substring(prefix.Length + 1).Trim();
    }

    /// <summary>
    /// "opt1=val1,opt2=val2" 形式の文字列を辞書に変換する。値はクォート（<c>"..."</c>）を許容。
    /// </summary>
    private static Dictionary<string, string> ParseOptions(string opts)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(opts)) return dict;
        // 雑にトップレベルカンマで分割（クォート内のカンマは保護）
        // 簡単化：ダブルクォートで囲まれた範囲のカンマだけ保護する正規表現で代替
        var pairs = SplitTopLevelCommas(opts);
        foreach (var p in pairs)
        {
            int eq = p.IndexOf('=');
            if (eq < 0) continue;
            string k = p.Substring(0, eq).Trim();
            string v = p.Substring(eq + 1).Trim();
            // クォート除去
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                v = v.Substring(1, v.Length - 2);
            dict[k] = v;
        }
        return dict;
    }

    private static List<string> SplitTopLevelCommas(string s)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char c in s)
        {
            if (c == '"') { inQuote = !inQuote; sb.Append(c); continue; }
            if (c == ',' && !inQuote) { list.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
}
