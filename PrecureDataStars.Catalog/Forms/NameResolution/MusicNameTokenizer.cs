using System;
using System.Collections.Generic;
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

        // 1. 既定区切りで分割。区切り文字自体も Token.PrecedingSeparator として保持する。
        var segments = SplitWithSeparators(freeText.Trim());

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
    /// </summary>
    private static List<Segment> SplitWithSeparators(string input)
    {
        var segments = new List<Segment>();
        var current = new System.Text.StringBuilder();
        string? pendingSep = null;
        int parenDepth = 0;
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];

            // 括弧深度の追跡。深度 0 以外（=CV 括弧内）では区切りを検出しない。
            if (Array.IndexOf(OpenParens, c) >= 0) { parenDepth++; current.Append(c); i++; continue; }
            if (Array.IndexOf(CloseParens, c) >= 0) { if (parenDepth > 0) parenDepth--; current.Append(c); i++; continue; }

            if (parenDepth == 0)
            {
                string? hit = TryMatchSeparator(input, i);
                if (hit is not null)
                {
                    // 現在の蓄積を 1 セグメントとして確定し、区切りは次セグメントの PrecedingSeparator として保持。
                    segments.Add(new Segment(current.ToString(), pendingSep));
                    current.Clear();
                    pendingSep = hit;
                    i += hit.Length;
                    continue;
                }
            }

            current.Append(c);
            i++;
        }
        // 末尾のセグメント。
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
