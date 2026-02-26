using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Data.TitleCharStatsJson;

/// <summary>
/// サブタイトル文字列 (<c>title_text</c>) から文字統計 JSON (<c>title_char_stats</c>) を生成する共通ビルダー。
/// <para>
/// 以下の処理を行う:
/// <list type="number">
///   <item>NFKC 正規化 + 日本語固有の文字揺れ補正（波ダッシュ・三点リーダ等）</item>
///   <item>書記素（grapheme cluster）単位で文字をカウント</item>
///   <item>各書記素をカテゴリ（ひらがな/カタカナ/漢字/ラテン/数字/絵文字/記号/句読点/その他）に分類</item>
///   <item>JSON として直列化し、DB の <c>title_char_stats</c> 列に格納可能な文字列を返す</item>
/// </list>
/// </para>
/// <remarks>
/// EpisodesEditorForm およびコンソール一括ツール (PrecureDataStars.TitleCharStatsJson) の
/// 両方から参照される。カテゴリ判定ロジックはフォーム側の実装を移植・統一したもの。
/// </remarks>
/// </summary>
public static class TitleCharStatsBuilder
{
    // ── 書記素先頭でのカテゴリ判定（EpisodesEditorForm の実装を移植） ──
    private static readonly Regex RxSpace = new(@"\p{Zs}+", RegexOptions.Compiled);
    private static readonly Regex RxHira = new(@"^\p{IsHiragana}", RegexOptions.Compiled);
    private static readonly Regex RxKata = new(@"^[\p{IsKatakana}ｦ-ﾟー]", RegexOptions.Compiled); // 半角ｶﾅ/長音含む
    private static readonly Regex RxKanji = new(@"^\p{IsCJKUnifiedIdeographs}", RegexOptions.Compiled);
    private static readonly Regex RxLatin = new(@"^[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}Ａ-Ｚａ-ｚ]", RegexOptions.Compiled);
    private static readonly Regex RxDigit = new(@"^\p{Nd}", RegexOptions.Compiled);

    // 絵文字レンジ（サロゲートペア範囲の主要絵文字ブロック。U+2600–U+27BF は Symbols 扱い）
    private static readonly Regex RxEmoji = new(@"^(\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDEFF]|\uD83E[\uDD00-\uDDFF])", RegexOptions.Compiled);
    private static readonly Regex RxPunct = new(@"^\p{P}", RegexOptions.Compiled);
    private static readonly Regex RxSymbol = new(@"^\p{S}", RegexOptions.Compiled);

    /// <summary>
    /// 指定されたサブタイトル文字列を解析し、文字統計の JSON 文字列を生成する。
    /// </summary>
    /// <param name="titleText">サブタイトル文字列（NFKC 正規化前の生テキスト）。NULL の場合は空文字扱い。</param>
    /// <returns>
    /// <c>title_char_stats</c> 列に格納する JSON 文字列。
    /// スキーマ: <c>{ version, norm, length: { codepoints, graphemes, unique_graphemes }, spaces, categories: {...}, chars: {...} }</c>
    /// </returns>
    public static string BuildJson(string titleText)
    {
        // Step 1: NFKC 正規化 + 日本語固有の文字修正
        //   - NBSP / 全角スペース → 半角スペース
        //   - 波ダッシュ (U+301C, U+FF5E) → 〜 に統一
        //   - NFKC で崩れた三点リーダ "..." → "…" に復元
        var s = (titleText ?? string.Empty).Normalize(NormalizationForm.FormKC)
            .Replace('\u00A0', ' ') // NBSP → 半角スペース
            .Replace('\u3000', ' ') // 全角スペース → 半角スペース
            .Replace('\u301C', '〜').Replace('\uFF5E', '〜') // 波ダッシュ統一
            .Replace("...", "…"); // NFKCで崩れた三点リーダを復元

        // Step 2: カウンタ初期化（9 カテゴリ + 文字頻度辞書）
        var chars = new Dictionary<string, int>();
        var categories = new Dictionary<string, int>
        {
            ["Hiragana"] = 0,
            ["Katakana"] = 0,
            ["Kanji"] = 0,
            ["Latin"] = 0,
            ["Digits"] = 0,
            ["Emoji"] = 0,
            ["Punct"] = 0,
            ["Symbols"] = 0,
            ["Other"] = 0
        };

        int spaces = 0, graphemes = 0;
        var uniq = new HashSet<string>();

        // Step 3: 書記素クラスタ単位で列挙し、カテゴリ分類とカウント
        //   - StringInfo.GetTextElementEnumerator: 合字・結合文字・サロゲートペアを正しく 1 単位に
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var g = e.GetTextElement();
            if (string.IsNullOrEmpty(g)) continue;

            if (RxSpace.IsMatch(g)) { spaces++; continue; }

            graphemes++;
            uniq.Add(g);

            string cat =
                RxEmoji.IsMatch(g) ? "Emoji" :
                RxPunct.IsMatch(g) ? "Punct" :
                RxSymbol.IsMatch(g) ? "Symbols" :
                RxHira.IsMatch(g) ? "Hiragana" :
                RxKata.IsMatch(g) ? "Katakana" :
                RxKanji.IsMatch(g) ? "Kanji" :
                RxDigit.IsMatch(g) ? "Digits" :
                RxLatin.IsMatch(g) ? "Latin" : "Other";

            categories[cat]++;

            // 文字頻度（空白は含めない）
            chars[g] = chars.TryGetValue(g, out var n) ? n + 1 : 1;
        }

        // Unicode コードポイント数のカウント（Rune 単位で列挙）
        int codepoints = 0;
        foreach (var _ in s.EnumerateRunes()) codepoints++;

        // Step 4: JSON ペイロードの組み立て
        var payload = new
        {
            version = 1,
            norm = "NFKC+jpn-fix+ellipsis",
            length = new { codepoints, graphemes, unique_graphemes = uniq.Count },
            spaces,
            categories,
            chars
        };

        // UnsafeRelaxedJsonEscaping: 日本語文字をエスケープせずそのまま出力（可読性重視）
        var opts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        return JsonSerializer.Serialize(payload, opts);
    }
}
