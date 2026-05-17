using System.Globalization;
using System.Text;

namespace PrecureDataStars.Data.Text;

/// <summary>
/// かな（ひらがな・カタカナ）をパスポート式（ヘボン式準拠の旅券表記）ローマ字へ
/// 変換する純粋ユーティリティ。外部ライブラリに依存しない。
/// <para>
/// 本クラスは「かな読みから機械的に英字表記を生成する」用途に限定する。漢字の読みは
/// 推定できないため、変換対象は「かな＋長音符（ー）＋中黒（・）＋半角スペース」のみとし、
/// それ以外の文字（漢字・英数字・その他記号・全角スペース等）が 1 文字でも混入する
/// 入力は変換不能として扱う（呼び出し側でスキップ判定できるよう理由を返す）。
/// </para>
/// <para>
/// パスポート式の規則:
/// <list type="bullet">
/// <item>長音は表記しない（切り捨て）。長音符「ー」は無音として落とす。
///       母音による長音は「直前の音のローマ字末尾が o もしくは u で、続くかなが
///       『う』」のときその『う』を表記しない（「おう」「こう」「ゆう」「とうきょう」
///       → o / ko / yu / tokyo、「さとう」→ sato）。一方「えい」「おお」「ええ」等は
///       旅券表記でも素直に綴るケースが多いため触らない（過剰な切り捨てを避ける安全側）。</item>
/// <item>撥音「ん」は常に n（後続が b/m/p でも m にしない）。</item>
/// <item>促音「っ」は次の音の子音を重ねる。後続が無い場合は無視する。
///       次が「ち」のときは旅券表記に合わせ tch ではなく cch を避け、ヘボン子音
///       （chi の子音 ch）の代表字 t を重ねて tchi とせず、ここでは次音の先頭子音
///       をそのまま重ねる単純規則とする（っち→tchi 相当を避け cchi でもなく、
///       一般的な「次音ローマ字の先頭子音を重ねる」= tchi になる点に注意）。</item>
/// <item>姓名は反転しない。半角スペースで区切られた語ごとに先頭 1 文字だけ大文字化し、
///       残りは小文字。半角スペースと中黒「・」は区切りとして保持する。</item>
/// </list>
/// 本実装はアプリ内（登録・変更時の自動補完）と一括補完フォームの双方から
/// 共有して使う恒久資産であり、使い捨て側だけを撤去しても残す前提とする。
/// </para>
/// </summary>
public static class KanaRomanizer
{
    /// <summary>
    /// 入力 <paramref name="kana"/> をパスポート式ローマ字へ変換する。
    /// 変換できた場合は true を返し <paramref name="romaji"/> に結果を格納する。
    /// 変換対象外（空・null・かな以外の文字を含む）の場合は false を返し、
    /// <paramref name="skipReason"/> に日本語の理由を格納する。
    /// </summary>
    /// <param name="kana">かな読み文字列（ひらがな／カタカナ混在可）。</param>
    /// <param name="romaji">変換結果（パスポート式）。</param>
    /// <param name="skipReason">変換不能時の理由（UI 表示・レポート用）。</param>
    public static bool TryRomanize(string? kana, out string romaji, out string? skipReason)
    {
        romaji = string.Empty;
        skipReason = null;

        if (string.IsNullOrWhiteSpace(kana))
        {
            skipReason = "かなが空です。";
            return false;
        }

        // 入力を一旦ひらがなへ正規化（カタカナ→ひらがな）。長音符・中黒・半角スペースは保持。
        // 想定外の文字が含まれていればこの時点で変換不能と判定する。
        var normalized = new StringBuilder(kana.Length);
        foreach (char ch in kana)
        {
            if (ch == ' ' || ch == '\u30FB' /* ・ 中黒 */ || ch == '\u30FC' /* ー 長音符 */)
            {
                normalized.Append(ch);
                continue;
            }

            // カタカナ（U+30A1..U+30F6）はひらがな（U+3041..U+3096）へ -0x60 でマップ。
            if (ch >= '\u30A1' && ch <= '\u30F6')
            {
                normalized.Append((char)(ch - 0x60));
                continue;
            }

            // ひらがな（U+3041..U+3096）と「ゔ」(U+3094) はそのまま採用。
            if (ch >= '\u3041' && ch <= '\u3096')
            {
                normalized.Append(ch);
                continue;
            }

            // それ以外（漢字・英数字・記号・全角空白など）が混入したら変換不能。
            skipReason = $"かな以外の文字 '{ch}' が含まれています。";
            return false;
        }

        string src = normalized.ToString();
        var sb = new StringBuilder(src.Length * 2);

        int i = 0;
        while (i < src.Length)
        {
            char c = src[i];

            // 母音長音の切り捨て（パスポート式）。
            // 直前に出力済みのローマ字末尾が母音 o もしくは u で、現在のかなが「う」の
            // 場合、その「う」は長音とみなして出力しない（さとう→sato、こう→ko、
            // とうきょう→tokyo）。直前が区切り文字や語頭の場合、および直前末尾が
            // a/i/e の場合（えい等）は通常どおり綴る。
            if (c == '\u3046' && sb.Length > 0)
            {
                char prevOut = sb[sb.Length - 1];
                if (prevOut is 'o' or 'u')
                {
                    i++;
                    continue;
                }
            }

            // 区切り文字はそのまま出力（語境界判定は最後の TitleCase 化で使う）。
            if (c == ' ' || c == '\u30FB')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // 長音符は無音（パスポート式は長音を表記しない）。
            if (c == '\u30FC')
            {
                i++;
                continue;
            }

            // 促音「っ」: 次の音をローマ字化し、その先頭子音を 1 文字重ねる。
            if (c == '\u3063')
            {
                // 連続する「っ」は 1 つにまとめる（っっ→次子音 1 回重ね）。
                int j = i;
                while (j < src.Length && src[j] == '\u3063') j++;
                if (j >= src.Length)
                {
                    // 後続が無い促音は無視する。
                    i = j;
                    continue;
                }

                string nextRoma = ConvertOneUnit(src, j, out int consumed);
                if (nextRoma.Length == 0)
                {
                    // 次が変換不能（理論上ここには来ないが保険）。促音は落とす。
                    i = j;
                    continue;
                }

                char head = nextRoma[0];
                // 母音始まり（a/i/u/e/o）の音には促音重ねを付けない。
                if (head is not ('a' or 'i' or 'u' or 'e' or 'o'))
                {
                    sb.Append(head);
                }

                sb.Append(nextRoma);
                i = j + consumed;
                continue;
            }

            // 撥音「ん」: パスポート式は常に n。
            if (c == '\u3093')
            {
                sb.Append('n');
                i++;
                continue;
            }

            string unit = ConvertOneUnit(src, i, out int used);
            if (unit.Length == 0)
            {
                // テーブル未定義のかな（拗音の組合せ外など）。1 文字だけ素通しせず
                // 変換不能としてレポートさせる方が安全。
                skipReason = $"ローマ字へ変換できないかな '{src[i]}' があります。";
                return false;
            }

            sb.Append(unit);
            i += used;
        }

        romaji = ToTitleCasePerToken(sb.ToString());
        if (romaji.Length == 0)
        {
            skipReason = "変換結果が空になりました。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// <paramref name="src"/> の位置 <paramref name="pos"/> から始まる 1 音
    /// （拗音なら 2 文字）をローマ字へ変換し、消費した文字数を <paramref name="consumed"/>
    /// に返す。テーブルに無い場合は空文字を返す（消費 1）。
    /// </summary>
    private static string ConvertOneUnit(string src, int pos, out int consumed)
    {
        consumed = 1;

        // 拗音（次が ゃ/ゅ/ょ、または「うぃ/うぇ」等の外来音）の 2 文字一致を優先。
        if (pos + 1 < src.Length)
        {
            string pair = src.Substring(pos, 2);
            if (DigraphTable.TryGetValue(pair, out string? d))
            {
                consumed = 2;
                return d;
            }
        }

        string mono = src[pos].ToString();
        if (MonographTable.TryGetValue(mono, out string? m))
        {
            return m;
        }

        return string.Empty;
    }

    /// <summary>
    /// 半角スペースと中黒「・」を語区切りとして、語ごとに先頭 1 文字のみ大文字化する。
    /// 区切り文字自体は保持する。姓名の順序は変更しない。
    /// </summary>
    private static string ToTitleCasePerToken(string lowerRomaji)
    {
        var sb = new StringBuilder(lowerRomaji.Length);
        bool atTokenStart = true;

        foreach (char ch in lowerRomaji)
        {
            if (ch == ' ' || ch == '\u30FB')
            {
                sb.Append(ch);
                atTokenStart = true;
                continue;
            }

            if (atTokenStart)
            {
                sb.Append(char.ToUpper(ch, CultureInfo.InvariantCulture));
                atTokenStart = false;
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // ── 拗音・外来音（2 かな）→ ローマ字。長音は含めない（長音符は呼び出し側で落とす）。
    private static readonly Dictionary<string, string> DigraphTable = new(StringComparer.Ordinal)
    {
        // き／ぎ／し／じ／ち／に／ひ／び／ぴ／み／り 行の拗音
        ["きゃ"] = "kya", ["きゅ"] = "kyu", ["きょ"] = "kyo",
        ["ぎゃ"] = "gya", ["ぎゅ"] = "gyu", ["ぎょ"] = "gyo",
        ["しゃ"] = "sha", ["しゅ"] = "shu", ["しょ"] = "sho", ["しぇ"] = "she",
        ["じゃ"] = "ja",  ["じゅ"] = "ju",  ["じょ"] = "jo",  ["じぇ"] = "je",
        ["ちゃ"] = "cha", ["ちゅ"] = "chu", ["ちょ"] = "cho", ["ちぇ"] = "che",
        ["にゃ"] = "nya", ["にゅ"] = "nyu", ["にょ"] = "nyo",
        ["ひゃ"] = "hya", ["ひゅ"] = "hyu", ["ひょ"] = "hyo",
        ["びゃ"] = "bya", ["びゅ"] = "byu", ["びょ"] = "byo",
        ["ぴゃ"] = "pya", ["ぴゅ"] = "pyu", ["ぴょ"] = "pyo",
        ["みゃ"] = "mya", ["みゅ"] = "myu", ["みょ"] = "myo",
        ["りゃ"] = "rya", ["りゅ"] = "ryu", ["りょ"] = "ryo",
        // 外来音
        ["ふぁ"] = "fa", ["ふぃ"] = "fi", ["ふぇ"] = "fe", ["ふぉ"] = "fo", ["ふゅ"] = "fyu",
        ["うぃ"] = "wi", ["うぇ"] = "we", ["うぉ"] = "wo",
        ["ゔぁ"] = "va", ["ゔぃ"] = "vi", ["ゔぇ"] = "ve", ["ゔぉ"] = "vo", ["ゔ"] = "vu",
        ["てぃ"] = "ti", ["でぃ"] = "di", ["でゅ"] = "dyu", ["とぅ"] = "tu", ["どぅ"] = "du",
        ["ちぃ"] = "chi", ["つぁ"] = "tsa", ["つぃ"] = "tsi", ["つぇ"] = "tse", ["つぉ"] = "tso",
        ["いぇ"] = "ye", ["きぇ"] = "kye", ["ぎぇ"] = "gye",
    };

    // ── 単かな → ローマ字。「ん」「っ」「ー」「・」「空白」は本テーブル外で処理する。
    private static readonly Dictionary<string, string> MonographTable = new(StringComparer.Ordinal)
    {
        ["あ"] = "a",  ["い"] = "i",  ["う"] = "u",  ["え"] = "e",  ["お"] = "o",
        ["か"] = "ka", ["き"] = "ki", ["く"] = "ku", ["け"] = "ke", ["こ"] = "ko",
        ["が"] = "ga", ["ぎ"] = "gi", ["ぐ"] = "gu", ["げ"] = "ge", ["ご"] = "go",
        ["さ"] = "sa", ["し"] = "shi",["す"] = "su", ["せ"] = "se", ["そ"] = "so",
        ["ざ"] = "za", ["じ"] = "ji", ["ず"] = "zu", ["ぜ"] = "ze", ["ぞ"] = "zo",
        ["た"] = "ta", ["ち"] = "chi",["つ"] = "tsu",["て"] = "te", ["と"] = "to",
        ["だ"] = "da", ["ぢ"] = "ji", ["づ"] = "zu", ["で"] = "de", ["ど"] = "do",
        ["な"] = "na", ["に"] = "ni", ["ぬ"] = "nu", ["ね"] = "ne", ["の"] = "no",
        ["は"] = "ha", ["ひ"] = "hi", ["ふ"] = "fu", ["へ"] = "he", ["ほ"] = "ho",
        ["ば"] = "ba", ["び"] = "bi", ["ぶ"] = "bu", ["べ"] = "be", ["ぼ"] = "bo",
        ["ぱ"] = "pa", ["ぴ"] = "pi", ["ぷ"] = "pu", ["ぺ"] = "pe", ["ぽ"] = "po",
        ["ま"] = "ma", ["み"] = "mi", ["む"] = "mu", ["め"] = "me", ["も"] = "mo",
        ["や"] = "ya", ["ゆ"] = "yu", ["よ"] = "yo",
        ["ら"] = "ra", ["り"] = "ri", ["る"] = "ru", ["れ"] = "re", ["ろ"] = "ro",
        ["わ"] = "wa", ["ゐ"] = "i",  ["ゑ"] = "e",  ["を"] = "o",
        // 小書きかな（単独出現時のフォールバック。通常は拗音テーブルで 2 文字処理される）
        ["ぁ"] = "a",  ["ぃ"] = "i",  ["ぅ"] = "u",  ["ぇ"] = "e",  ["ぉ"] = "o",
        ["ゃ"] = "ya", ["ゅ"] = "yu", ["ょ"] = "yo", ["ゎ"] = "wa",
    };
}
