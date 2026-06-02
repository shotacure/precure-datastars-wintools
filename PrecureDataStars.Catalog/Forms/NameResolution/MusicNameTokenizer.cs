using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

/// <summary>
/// 音楽の名寄せセンターで、フリーテキストの歌手・作家表記を
/// 連名トークン列に粗く分解するヘルパ。
/// 既定区切り（"、" "・" "／" "/" "＆" "&amp;" "," "  "）で分解した各セグメントについて、
/// 「キャラ名(CV:声優)」「キャラ／キャラ(CV:声優)」形式の CV パターンを検出する。
/// 検出に失敗したものは PERSON 種別のトークンとしてそのままの文字列で返す。
/// このヘルパは DB に触らず、構造化エントリへの解決（alias_id への変換）は
/// 呼び出し側（<see cref="MusicNameResolutionForm"/>）で行う。
/// </summary>
public static class MusicNameTokenizer
{
    /// <summary>連名区切りとして認識する文字列群。長い順に試す（半角スペースは最後）。</summary>
    private static readonly string[] SeparatorCandidates = new[]
    {
        " with ", " feat. ", " feat ",
        "、", "，",
        "・", "／", "&amp;", "＆", "&", "／",
        "/", ",",
    };

    /// <summary>CV パターンの開き括弧候補（全角・半角）。</summary>
    private static readonly char[] OpenParens = new[] { '(', '（' };
    private static readonly char[] CloseParens = new[] { ')', '）' };

    /// <summary>「(CV:◯◯)」を検出する正規表現。CV / Cv / cv と全角・半角コロン・ドットを許容。</summary>
    private static readonly Regex CvPattern = new(
        @"^(?<main>.+?)\s*[\(（]\s*(?:CV|Cv|cv)\s*[:：.．]?\s*(?<voice>.+?)\s*[\)）]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>「キャラ／キャラ」のスラッシュ並列を検出する区切り候補。CV パターンの main 部分にだけ適用する。</summary>
    private static readonly char[] SlashSeparators = new[] { '/', '／' };

    /// <summary>トークン分解結果。1 行 1 トークン。</summary>
    public sealed class Token
    {
        /// <summary>このトークンが先頭でない場合、前トークンとの間に置かれていた区切り文字（"、" "・" 等）。</summary>
        public string? PrecedingSeparator { get; init; }

        /// <summary>分解前の生テキスト（このトークン分のみ）。</summary>
        public string RawText { get; init; } = "";

        /// <summary>CV パターン検出時のメイン部（キャラ名側）。検出失敗時は null。</summary>
        public string? MainPart { get; init; }

        /// <summary>CV パターン検出時のスラッシュ並列の相方キャラ名。なければ null。</summary>
        public string? SlashPart { get; init; }

        /// <summary>CV パターン検出時の声優名。検出失敗時は null。</summary>
        public string? VoicePart { get; init; }

        /// <summary>このトークンが CV パターンとして識別できたかどうか。</summary>
        public bool IsCharacterWithCv => VoicePart is not null;
    }

    /// <summary>フリーテキストを連名トークン列に分解する。</summary>
    /// <param name="freeText">対象フリーテキスト（null/空なら空配列）。</param>
    /// <returns>分解されたトークン列。1 件以上の場合、先頭の <c>PrecedingSeparator</c> は null。</returns>
    public static IReadOnlyList<Token> Tokenize(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText)) return Array.Empty<Token>();

        string trimmedInput = freeText.Trim();

        // 入力全体が単一の CV パターン「XXX(CV:ZZZ)」（XXX に '/' を含み得る）に尽きる場合は、
        // トップレベルの区切り分割をスキップして 1 トークンを直接返す。
        // これがないと「美墨なぎさ/キュアブラック(CV:本名陽子)」のような「主名義/スラッシュ相方(CV:声優)」記法で
        // '/' が行区切りとして拾われてしまい、本来 1 トークンの CHARACTER_WITH_CV が 2 行に分断される。
        //
        // ただし全体を 1 トークンと見なせるのは「括弧深度 0 のトップレベルに、スラッシュ以外の
        // 連名区切り（"、" "・" "&" 等）が存在しない」場合に限る。複数歌手を「、」で連結した
        // 「A/相方(CV:x)、B/相方(CV:y)」では CvPattern の貪欲マッチが「、」を跨いで全体を 1 つの
        // CV パターンとして飲み込み（voice が最後の ")" まで伸びる）、2 人目以降が落ちてしまう。
        // トップレベルに連名区切りがある場合はショートカットを使わず通常分割経路に回す。
        var wholeCvMatch = CvPattern.Match(trimmedInput);
        bool wholeIsSingleCvToken =
            wholeCvMatch.Success
            && !CollectSeparatorOccurrences(trimmedInput).Any(o => !IsSlashSeparator(o.Sep));
        if (wholeIsSingleCvToken)
        {
            string mainPart = wholeCvMatch.Groups["main"].Value.Trim();
            string voicePart = wholeCvMatch.Groups["voice"].Value.Trim();
            string? slashPart = null;
            int slashIdx = mainPart.IndexOfAny(SlashSeparators);
            if (slashIdx > 0 && slashIdx < mainPart.Length - 1)
            {
                slashPart = mainPart[(slashIdx + 1)..].Trim();
                mainPart  = mainPart[..slashIdx].Trim();
            }
            return new[]
            {
                new Token
                {
                    PrecedingSeparator = null,
                    RawText = trimmedInput,
                    MainPart = mainPart,
                    SlashPart = string.IsNullOrEmpty(slashPart) ? null : slashPart,
                    VoicePart = voicePart
                }
            };
        }

        // 1. 既定区切りで分割。区切り文字自体も Token.PrecedingSeparator として保持する。
        var segments = SplitWithSeparators(trimmedInput);

        // 2. 各セグメントを CV パターン検出にかけて Token を生成。
        var result = new List<Token>(segments.Count);
        foreach (var seg in segments)
        {
            string raw = seg.Text.Trim();
            if (raw.Length == 0) continue;

            var cvMatch = CvPattern.Match(raw);
            if (cvMatch.Success)
            {
                string mainPart = cvMatch.Groups["main"].Value.Trim();
                string voicePart = cvMatch.Groups["voice"].Value.Trim();

                // メイン側にスラッシュ並列が含まれる場合は分割する（最大 1 段）。
                string? slashPart = null;
                int slashIdx = mainPart.IndexOfAny(SlashSeparators);
                if (slashIdx > 0 && slashIdx < mainPart.Length - 1)
                {
                    slashPart = mainPart[(slashIdx + 1)..].Trim();
                    mainPart  = mainPart[..slashIdx].Trim();
                }

                result.Add(new Token
                {
                    PrecedingSeparator = seg.Separator,
                    RawText = raw,
                    MainPart = mainPart,
                    SlashPart = string.IsNullOrEmpty(slashPart) ? null : slashPart,
                    VoicePart = voicePart
                });
            }
            else
            {
                result.Add(new Token
                {
                    PrecedingSeparator = seg.Separator,
                    RawText = raw
                });
            }
        }
        return result;
    }

    /// <summary>分割中の中間表現（テキスト + 直前の区切り文字）。</summary>
    private sealed record Segment(string Text, string? Separator);

    /// <summary>
    /// 区切り候補リストを「長い順」に走査して文字列を分割する。
    /// CV パターンの括弧の内側に出現する区切り（例: "(CV:本名陽子&amp;ゆかな)" の "&amp;"）を
    /// 誤って区切りにしないよう、括弧深度を見ながら走査する。
    /// <para>
    /// さらに「1 つのフリーテキストの中で複数種の区切りが混在することはない」という運用ルールに従い、
    /// 検出した区切り文字種が複数あるときは最頻出 1 種だけを実際の区切りとして採用する。
    /// それ以外の種別はトークン内テキスト（=名義の一部）として温存する。
    /// 同回数のタイは「最初に出現したもの」が勝つ（先頭側に構造的な区切りがある可能性が高い経験則）。
    /// </para>
    /// <para>例：<c>美墨なぎさ(CV:本名陽子)&amp;雪城ほのか(CV:ゆかな)&amp;ヤング・フレッシュ</c> は
    /// "&amp;" 2 回 / "・" 1 回。最頻出 "&amp;" だけを区切りとして採用するため、
    /// "ヤング・フレッシュ" が 1 トークンとして温存される（ユニット名等を勝手に分割しない）。</para>
    /// </summary>
    private static List<Segment> SplitWithSeparators(string input)
    {
        // パス 1：括弧深度を見ながら全区切り出現を回収（type と pos の組）。
        var occurrences = CollectSeparatorOccurrences(input);

        // パス 2：最頻出 1 種を採用区切りに決める（候補が無ければ null = 全部 1 トークン化）。
        string? activeSeparator = ChooseDominantSeparator(occurrences);

        // パス 3：active な区切りのみで実際にセグメントを切る。非 active な区切り出現は文字列にそのまま埋め戻す。
        return EmitSegments(input, occurrences, activeSeparator);
    }

    /// <summary>括弧深度を見ながら全区切り出現を 1 回スキャンで集める。</summary>
    private static List<(int Pos, string Sep)> CollectSeparatorOccurrences(string input)
    {
        var occurrences = new List<(int, string)>();
        int parenDepth = 0;
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (Array.IndexOf(OpenParens, c) >= 0) { parenDepth++; i++; continue; }
            if (Array.IndexOf(CloseParens, c) >= 0) { if (parenDepth > 0) parenDepth--; i++; continue; }
            if (parenDepth == 0)
            {
                string? hit = TryMatchSeparator(input, i);
                if (hit is not null)
                {
                    occurrences.Add((i, hit));
                    i += hit.Length;
                    continue;
                }
            }
            i++;
        }
        return occurrences;
    }

    /// <summary>出現リストから採用区切りを選ぶ。出現ゼロなら null。
    /// 連名区切り（"、" "・" "&amp;" 等）はスラッシュ（"/" "／"）より常に優先する。
    /// スラッシュは「主名義/スラッシュ相方」の下位区切りであり、同一フリーテキストに上位の
    /// 連名区切りが在るときに '/' で割ると各歌手の主名義と相方が分断されるため。
    /// 非スラッシュ同士・スラッシュ同士の中では「最頻出 1 種、同点なら先出」で決める。</summary>
    private static string? ChooseDominantSeparator(List<(int Pos, string Sep)> occurrences)
    {
        if (occurrences.Count == 0) return null;
        return occurrences
            .GroupBy(o => o.Sep, StringComparer.Ordinal)
            .Select(g => (Sep: g.Key, Count: g.Count(), FirstPos: g.Min(o => o.Pos)))
            .OrderBy(x => IsSlashSeparator(x.Sep) ? 1 : 0)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.FirstPos)
            .First()
            .Sep;
    }

    /// <summary>区切り文字列がスラッシュ（"/" / "／"）かどうか。連名区切りとの優先度付けに使う。</summary>
    private static bool IsSlashSeparator(string sep) =>
        sep.Length == 1 && Array.IndexOf(SlashSeparators, sep[0]) >= 0;

    /// <summary>トークンに保持・出力する区切り文字を半角へ正規化する。
    /// 全角スラッシュ「／」→「/」、全角アンパサンド「＆」→「&」。それ以外はそのまま返す。
    /// 検出（<see cref="SeparatorCandidates"/>）は全角も受けるが、保持する区切りは半角に統一して
    /// 全角「／」「＆」が再エンコードや UI 表示に残らないようにする。</summary>
    private static string NormalizeOutputSeparator(string sep) => sep switch
    {
        "／" => "/",
        "＆" => "&",
        _ => sep,
    };

    /// <summary>active な区切りのみで実際のセグメント列を生成する。非 active な区切り出現は文字列に埋め戻す。</summary>
    private static List<Segment> EmitSegments(
        string input,
        List<(int Pos, string Sep)> occurrences,
        string? activeSeparator)
    {
        var segments = new List<Segment>();
        var current = new System.Text.StringBuilder();
        string? pendingSep = null;
        int occIdx = 0;
        int i = 0;
        while (i < input.Length)
        {
            // i 以降の最初の出現に occIdx を揃える。
            while (occIdx < occurrences.Count && occurrences[occIdx].Pos < i) occIdx++;
            if (occIdx < occurrences.Count && occurrences[occIdx].Pos == i)
            {
                var (_, sep) = occurrences[occIdx];
                if (activeSeparator is not null && string.Equals(sep, activeSeparator, StringComparison.Ordinal))
                {
                    // 採用区切り：セグメント確定。
                    segments.Add(new Segment(current.ToString(), pendingSep));
                    current.Clear();
                    // 検出は全角スラッシュ・アンパサンドも拾うが、トークンに保持する区切りは
                    // 半角へ正規化する（全角「／」「＆」を出力・表示に残さない方針。
                    // 区切り列ドロップダウンの選択肢も半角のみ）。
                    pendingSep = NormalizeOutputSeparator(sep);
                    i += sep.Length;
                    occIdx++;
                    continue;
                }
                // 非採用区切り：名義の一部としてテキストに編入。
                current.Append(input, i, sep.Length);
                i += sep.Length;
                occIdx++;
                continue;
            }
            current.Append(input[i]);
            i++;
        }
        segments.Add(new Segment(current.ToString(), pendingSep));
        return segments;
    }

    /// <summary>指定位置から始まる区切り候補があれば、ヒットしたものを返す。なければ null。</summary>
    private static string? TryMatchSeparator(string s, int pos)
    {
        // 長い候補から順に試す（"  with " 等の複数文字パターンを先に当てるため）。
        foreach (var sep in SeparatorCandidates)
        {
            if (pos + sep.Length > s.Length) continue;
            if (string.CompareOrdinal(s, pos, sep, 0, sep.Length) == 0) return sep;
        }
        return null;
    }
}
