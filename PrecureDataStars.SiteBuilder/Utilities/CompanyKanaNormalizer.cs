using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 企業・団体一覧の 50 音ソート時に、法人格表記（「かぶしきがいしゃ」等）を
/// スキップして読みを比較するためのヘルパー。
/// <para>
/// たとえば「株式会社サンライズ」を kana 順で並べる場合、読みが
/// 「かぶしきがいしゃさんらいず」のままだと「か」のセクションに分類されてしまうが、
/// 利用者の感覚としては「さ」のセクションに来るべき。本ヘルパーは比較用キーから
/// 「かぶしきがいしゃ」「カブシキガイシャ」「(株)」「（株）」「株式会社」など
/// の典型表記を取り除いて、その後ろの実体名で並ぶようにする。
/// </para>
/// <para>
/// 現状（v1.3.0 時点）は「株式会社」系のみ対応。他の法人格（有限会社・合同会社等）が
/// 出てきたタイミングで <see cref="StripPatterns"/> を拡張する想定。
/// </para>
/// </summary>
public static class CompanyKanaNormalizer
{
    /// <summary>
    /// 取り除くべき法人格表記のパターン（先頭・中間・末尾、いずれの位置に出ても除去）。
    /// 文字列比較は順次行うため、長いパターンを先に書いて部分一致の食い合いを避ける。
    /// </summary>
    private static readonly string[] StripPatterns = new[]
    {
        // 漢字表記（先頭または末尾に付く慣行が多い）
        "株式会社",
        // ひらがな読み
        "かぶしきがいしゃ",
        // カタカナ読み（kana 列に同表記が混在する場合の保険）
        "カブシキガイシャ",
        // 略記
        "(株)", "（株）", "㈱"
    };

    /// <summary>
    /// kana 比較用キーを返す。法人格表記をすべて取り除いた残り文字列が比較対象になる。
    /// 元文字列が NULL/空の場合は空文字を返す。
    /// </summary>
    /// <param name="kana">企業の読み（companies.name_kana）。</param>
    public static string NormalizeForSort(string? kana)
    {
        if (string.IsNullOrEmpty(kana)) return string.Empty;
        string result = kana;
        foreach (var pat in StripPatterns)
        {
            // 単純な文字列置換で OK（正規表現エスケープを必要とする特殊文字を含まない）
            result = result.Replace(pat, string.Empty);
        }
        // 連続空白の畳み込みは不要だが、先頭末尾の空白だけは消す（「株式会社 サンライズ」→「サンライズ」）。
        return result.Trim();
    }

    /// <summary>
    /// <see cref="NormalizeForSort"/> 結果を比較する <c>StringComparer</c> 互換の比較関数。
    /// LINQ の <c>OrderBy</c> に渡せる。
    /// </summary>
    public static IComparer<string?> Comparer { get; } = new KanaSkipComparer();

    private sealed class KanaSkipComparer : IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            string nx = NormalizeForSort(x);
            string ny = NormalizeForSort(y);
            // 通常の Ordinal 比較。kana 列はひらがな統一を期待しているので
            // CurrentCulture でなくても期待通り並ぶはず。
            return string.CompareOrdinal(nx, ny);
        }
    }
}
