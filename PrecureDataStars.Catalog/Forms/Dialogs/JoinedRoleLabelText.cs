namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 一括入力の <c>@join=文字</c>（1 行にまとめて出る役職の、画面どおりの行の文字）と、
/// 役職ごとの表記（<c>role_label_text</c>）・直前との区切り（<c>join_separator</c>）との相互変換。
/// <para>
/// 書き方は 2 通り。
/// <list type="bullet">
///   <item><description>角括弧なし（例: <c>キャラクターデザイン・作画監督</c>）：画面の文字が役職名どおりのとき。
///     文字を「役職名 + 区切り + 役職名 …」に分ける。区切りに使える文字は、文字・数字以外（「・」「／」「＆」・空白など）だけ。
///     分け方が一通りに決まらないときは分けずにエラーにする（役職名がほかの役職名に含まれる「作画監督」と
///     「作画監督補」のような組でも、取り違えて黙って保存しないため）。</description></item>
///   <item><description>角括弧あり（例: <c>[キャラクター・デザイン]・[作画監督]</c>）：役職ごとの文字の範囲を括弧で示す。
///     括弧の中が役職の表記、括弧の外が区切り。画面の表記が役職名と違う（揺れている）ときはこちらで書く。
///     括弧は半角 <c>[ ]</c> と全角 <c>［ ］</c> のどちらでもよい。</description></item>
/// </list>
/// 役職名を上下 2 段に重ねて出す画面（区切りが改行）は、改行を <c>\n</c>（バックスラッシュと n）と書く
/// （例: <c>キャラクターデザイン\n作画監督</c>）。
/// </para>
/// </summary>
public static class JoinedRoleLabelText
{
    /// <summary>一括入力で区切りの改行を表す書き方。</summary>
    public const string LineBreakToken = "\\n";

    /// <summary>一括入力の文字の <c>\n</c> を改行に戻す。</summary>
    public static string DecodeLineBreaks(string text) => text.Replace(LineBreakToken, "\n");

    /// <summary>区切りの改行を一括入力の <c>\n</c> にする。</summary>
    public static string EncodeLineBreaks(string text) => text.Replace("\n", LineBreakToken);

    private static readonly char[] OpenBrackets = { '[', '［' };
    private static readonly char[] CloseBrackets = { ']', '］' };

    /// <summary>
    /// <paramref name="text"/> を、<paramref name="names"/>（まとめる役職の役職名、表示順）の数の部品に分ける。
    /// </summary>
    /// <param name="text"><c>@join=</c> の右側の文字。</param>
    /// <param name="names">まとめる役職の役職名（表示順）。</param>
    /// <param name="parts">成功時、役職ごとの (表記, 直前との区切り)。表記は役職名と同じなら null、
    /// 区切りは先頭の役職と空文字のとき null。</param>
    /// <param name="error">失敗時の理由（利用者向けの文）。</param>
    public static bool TrySplit(
        string text, IReadOnlyList<string> names,
        out List<(string? LabelText, string? Separator)> parts, out string error)
    {
        parts = new List<(string?, string?)>();
        error = "";
        if (names.Count == 0) { error = "まとめる役職がありません。"; return false; }

        var raw = text.TrimStart().Length > 0 && Array.IndexOf(OpenBrackets, text.TrimStart()[0]) >= 0
            ? SplitBracketed(text.Trim(), names.Count, out error)
            : SplitByNames(text.Trim(), names, out error);
        if (raw is null) return false;

        for (int k = 0; k < raw.Count; k++)
        {
            var (label, sep) = raw[k];
            if (label.Length == 0) { error = $"{k + 1} つ目の役職の文字が空です。"; parts.Clear(); return false; }
            // 角括弧の中が、まとめるほかの役職の役職名そのものなら、役職の並びと文字の並びが食い違っている。
            if (!string.Equals(label, names[k], StringComparison.Ordinal)
                && names.Where((n, j) => j != k).Any(n => string.Equals(n, label, StringComparison.Ordinal)))
            {
                error = $"{k + 1} つ目の役職「{names[k]}」の文字が、ほかの役職の名前「{label}」になっています。役職の並びと文字の並びをそろえてください。";
                parts.Clear();
                return false;
            }
            parts.Add((
                string.Equals(label, names[k], StringComparison.Ordinal) ? null : label,
                k == 0 || string.IsNullOrEmpty(sep) ? null : sep));
        }
        return true;
    }

    /// <summary>
    /// 役職ごとの表記・区切りから <c>@join=</c> の右側の文字を作る（逆翻訳用）。
    /// 表記がすべて役職名どおりで、角括弧なしの形から同じ部品に一通りに戻せるときは角括弧なし、
    /// それ以外は角括弧ありの形で書く。
    /// </summary>
    /// <param name="names">まとめる役職の役職名（表示順）。</param>
    /// <param name="parts">役職ごとの (表記, 直前との区切り)。表記が null なら役職名を使う。</param>
    public static string Format(IReadOnlyList<string> names, IReadOnlyList<(string? LabelText, string? Separator)> parts)
    {
        var plain = new System.Text.StringBuilder();
        var bracketed = new System.Text.StringBuilder();
        for (int k = 0; k < parts.Count; k++)
        {
            string label = string.IsNullOrEmpty(parts[k].LabelText) ? names[k] : parts[k].LabelText!;
            string sep = k == 0 ? "" : parts[k].Separator ?? "";
            plain.Append(sep).Append(label);
            bracketed.Append(sep).Append('[').Append(label).Append(']');
        }

        bool allNames = parts.All(p => string.IsNullOrEmpty(p.LabelText));
        if (allNames
            && TrySplit(plain.ToString(), names, out var back, out _)
            && back.Select(b => b.Separator ?? "").SequenceEqual(parts.Select((p, k) => k == 0 ? "" : p.Separator ?? "")))
        {
            return EncodeLineBreaks(plain.ToString());
        }
        return EncodeLineBreaks(bracketed.ToString());
    }

    /// <summary>角括弧ありの形を分ける。括弧の外の先頭・末尾に文字があるとき、括弧の数が役職の数と違うときは失敗。</summary>
    private static List<(string Label, string Separator)>? SplitBracketed(string text, int count, out string error)
    {
        error = "";
        var result = new List<(string, string)>();
        int pos = 0;
        var sep = new System.Text.StringBuilder();
        while (pos < text.Length)
        {
            char c = text[pos];
            if (Array.IndexOf(OpenBrackets, c) >= 0)
            {
                int close = text.IndexOfAny(CloseBrackets, pos + 1);
                int nextOpen = text.IndexOfAny(OpenBrackets, pos + 1);
                if (close < 0 || (nextOpen >= 0 && nextOpen < close))
                {
                    error = "角括弧が閉じていません。";
                    return null;
                }
                if (result.Count == 0 && sep.Length > 0)
                {
                    error = "最初の角括弧より前に文字があります。";
                    return null;
                }
                result.Add((text.Substring(pos + 1, close - pos - 1), sep.ToString()));
                sep.Clear();
                pos = close + 1;
            }
            else if (Array.IndexOf(CloseBrackets, c) >= 0)
            {
                error = "対応する開き角括弧のない閉じ角括弧があります。";
                return null;
            }
            else
            {
                sep.Append(c);
                pos++;
            }
        }
        if (sep.Length > 0)
        {
            error = "最後の角括弧より後ろに文字があります。";
            return null;
        }
        if (result.Count != count)
        {
            error = $"角括弧の数（{result.Count}）が、まとめる役職の数（{count}）と合いません。";
            return null;
        }
        return result;
    }

    /// <summary>
    /// 角括弧なしの形を、役職名を順に当てはめて分ける。役職名どうしのあいだ（区切り）は、文字・数字以外の文字だけ。
    /// 分け方が無いとき・二通り以上あるときは失敗。
    /// </summary>
    private static List<(string Label, string Separator)>? SplitByNames(string text, IReadOnlyList<string> names, out string error)
    {
        error = "";
        var found = new List<List<(string, string)>>();
        Search(text, names, 0, 0, new List<(string, string)>(), found);
        if (found.Count == 0)
        {
            error = $"「{text}」を役職名（{string.Join("、", names)}）と区切りの文字に分けられません。"
                  + "画面の表記が役職名と違うときは、[キャラクター・デザイン]・[作画監督] のように角括弧で役職の部分を示してください。";
            return null;
        }
        if (found.Count > 1)
        {
            error = $"「{text}」を役職名（{string.Join("、", names)}）に分ける方法が一通りに決まりません。角括弧で役職の部分を示してください。";
            return null;
        }
        return found[0];
    }

    /// <summary>
    /// <paramref name="k"/> 番目の役職名を <paramref name="pos"/> 以降に当てはめる分け方を探す。
    /// 二通り見つかった時点で打ち切る（一通りかどうかだけ分かればよいため）。
    /// </summary>
    private static void Search(
        string text, IReadOnlyList<string> names, int k, int pos,
        List<(string, string)> current, List<List<(string, string)>> found)
    {
        if (found.Count > 1) return;
        if (k == names.Count)
        {
            if (pos == text.Length) found.Add(new List<(string, string)>(current));
            return;
        }
        string name = names[k];
        if (name.Length == 0) return;

        // 先頭の役職は文字の頭に置く。2 つ目以降は、区切り（文字・数字以外）を挟んだ位置を順に試す。
        int limit = k == 0 ? pos : text.Length - name.Length;
        for (int idx = pos; idx <= limit; idx++)
        {
            if (idx > pos && char.IsLetterOrDigit(text[idx - 1])) break;
            if (string.CompareOrdinal(text, idx, name, 0, name.Length) != 0) continue;
            current.Add((name, text.Substring(pos, idx - pos)));
            Search(text, names, k + 1, idx + name.Length, current, found);
            current.RemoveAt(current.Count - 1);
            if (found.Count > 1) return;
        }
    }
}
