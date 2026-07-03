namespace PrecureDataStars.Data.Text;

/// <summary>
/// 名義文字列の類似判定（誤字候補検知）ヘルパ。クレジット一括入力の「1 字違いの既存名義」警告が使う。
/// 依存のない純粋なテキスト処理のため、UI 層（CreditBulkApplyService）から本クラスへ切り出した。
/// プロジェクト方針：ひらがなとカタカナは別物として扱う（カナ統一の正規化は禁忌）。
/// 文字種構成の比較（<see cref="SameScriptComposition"/>）でカナ違い・カナ漢字違いを
/// 「タイポではなく別物」として誤字候補から除外する。
/// </summary>
public static class NameSimilarity
{
    /// <summary>誤字候補と判定する最小文字数。これ未満の名前同士の 1 字違いは
    /// 「タイポ」と「別人」の区別が困難（極端な場合 1 字違いで完全別人）なので警告対象外とする。</summary>
    private const int TypoMinNameLength = 3;

    /// <summary>誤字候補と判定する最大編集距離。1 = 1 字の差し替え / 挿入 / 削除のみ警告。
    /// 2 以上は「複数字違い = 別名義」とみなして警告しない。</summary>
    private const int TypoMaxEditDistance = 1;

    /// <summary>文字種カテゴリ別の構成数の最大許容差。<see cref="SameScriptComposition"/> で使う。
    /// 0 = 構成完全一致、1 = いずれかのカテゴリで 1 つだけ違ってよい（差し替え系の編集距離 1 と整合）。</summary>
    private const int ScriptCompositionMaxDelta = 1;

    /// <summary>比較用の文字列正規化（空白除去）。 半角スペース・全角スペース・タブ・各種空白文字をすべて除去する。 「五條 真由美」と「五条真由美」のように空白の有無による表記揺れを吸収するための前処理。</summary>
    public static string NormalizeForCompare(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            // char.IsWhiteSpace は半角・全角スペース両方を拾う（U+3000 全角スペースも対象）。
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>文字種カテゴリ。ひらがな⇔カタカナ・カナ⇔漢字をまたぐ違いを
    /// 「タイポではなく別物」と判定するために、各文字を 6 カテゴリに分類する。
    /// プロジェクト方針：ひらがなとカタカナは別物として扱う（カナ統一の正規化は禁忌）。</summary>
    private enum ScriptCategory { Hiragana, Katakana, CjkIdeograph, Latin, Digit, Other }

    /// <summary>文字を <see cref="ScriptCategory"/> に分類する。</summary>
    private static ScriptCategory ClassifyChar(char c)
    {
        // ひらがな: U+3040..U+309F
        if (c >= '぀' && c <= 'ゟ') return ScriptCategory.Hiragana;
        // 全角カタカナ: U+30A0..U+30FF / カタカナ拡張 (片仮名拡張): U+31F0..U+31FF / 半角カタカナ: U+FF65..U+FF9F
        if ((c >= '゠' && c <= 'ヿ')
            || (c >= 'ㇰ' && c <= 'ㇿ')
            || (c >= '･' && c <= 'ﾟ')) return ScriptCategory.Katakana;
        // CJK 統合漢字 + 拡張 A + 互換漢字: U+3400..U+4DBF, U+4E00..U+9FFF, U+F900..U+FAFF
        if ((c >= '㐀' && c <= '䶿')
            || (c >= '一' && c <= '鿿')
            || (c >= '豈' && c <= '﫿')) return ScriptCategory.CjkIdeograph;
        // ラテン英字（半角 A-Z / a-z、全角 Ａ-Ｚ / ａ-ｚ）
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
            || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ')) return ScriptCategory.Latin;
        // 数字（半角 0-9、全角 ０-９）
        if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９')) return ScriptCategory.Digit;
        return ScriptCategory.Other;
    }

    /// <summary>2 つの文字列の文字種構成（各カテゴリ何文字か）がほぼ一致するか判定する。
    /// 各カテゴリの個数差が <see cref="ScriptCompositionMaxDelta"/> 以内ならば「同じ文字種構成」とみなす。
    /// これによりひらがな⇔カタカナ・カナ⇔漢字をまたぐ違いは「別物」として誤字検知から弾く。
    /// 例：「アスカ」(カタカナ3) vs 「あすか」(ひらがな3) は 各カテゴリ差 3 で不一致 → 警告しない。
    /// 例：「田中花子」(漢字4) vs 「田中華子」(漢字4) は 完全一致 → 編集距離 1 と合わせて誤字候補。</summary>
    public static bool SameScriptComposition(string a, string b)
    {
        Span<int> ca = stackalloc int[6];
        Span<int> cb = stackalloc int[6];
        foreach (char ch in a) ca[(int)ClassifyChar(ch)]++;
        foreach (char ch in b) cb[(int)ClassifyChar(ch)]++;
        for (int i = 0; i < 6; i++)
        {
            if (Math.Abs(ca[i] - cb[i]) > ScriptCompositionMaxDelta) return false;
        }
        return true;
    }

    /// <summary>2 文字列の編集距離（レーベンシュタイン距離）を返す。挿入・削除・置換のコストを 1 で計算。
    /// 動的計画法 O(|A|×|B|) 実装。日本語名義は最大数十文字なので性能的に問題なし。
    /// 早期打ち切り最適化：<paramref name="cutoff"/> を超えた距離は計算途中で諦めて
    /// <c>cutoff + 1</c> を返す（誤字検知は距離 1 以下しか興味がないため、無駄計算を省く）。</summary>
    public static int LevenshteinDistance(string a, string b, int cutoff)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;
        // 文字数差が cutoff を超えていれば距離も必ず超える（早期判定）
        if (Math.Abs(a.Length - b.Length) > cutoff) return cutoff + 1;

        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            // 行最小値が cutoff を既に超えていれば、以降の行で距離が縮むことはないので打ち切り
            if (rowMin > cutoff) return cutoff + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }

    /// <summary>「正規化後 完全一致ではない」かつ「タイポ候補（距離 1 + 文字種構成一致 + 名前長 3 以上）」を判定する。
    /// <paramref name="normalizedRaw"/> は <see cref="NormalizeForCompare"/> で正規化済みの入力名を渡す前提。
    /// 完全一致は呼び出し側で既に除外されている前提だが、空白違いだけの「実質完全一致」は
    /// 警告対象から除外したいので、ここで正規化後完全一致もスキップする。
    /// 文字種構成が違う（例：ひらがな⇔カタカナ・漢字⇔カナ）場合は別物として警告しない（プロジェクト方針）。
    /// 編集距離 2 以上は「複数字違い = 別人」と判断して警告しない（誤字検知に焦点を絞る方針）。</summary>
    public static bool IsLikelyTypo(string normalizedRaw, string targetName)
    {
        if (string.IsNullOrEmpty(normalizedRaw)) return false;
        var targetNorm = NormalizeForCompare(targetName);
        if (targetNorm.Length == 0) return false;
        // 空白違いだけで本質同名 → 警告対象から除外
        if (string.Equals(normalizedRaw, targetNorm, StringComparison.Ordinal)) return false;

        // 短い名前同士は 1 字違いでもタイポか別人か区別できないので警告しない
        if (normalizedRaw.Length < TypoMinNameLength || targetNorm.Length < TypoMinNameLength) return false;

        // 文字種構成が違うなら別物（カナ違い・カナ漢字違いは誤字ではない）
        if (!SameScriptComposition(normalizedRaw, targetNorm)) return false;

        // 編集距離が許容範囲内ならタイポ候補
        int dist = LevenshteinDistance(normalizedRaw, targetNorm, TypoMaxEditDistance);
        return dist > 0 && dist <= TypoMaxEditDistance;
    }
}
