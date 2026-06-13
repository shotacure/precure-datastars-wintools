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

    /// <summary>フレーズ区切り（前後に空白を伴う語）。名義内には出現しないため、記号区切りの
    /// ドミナント投票とは独立に常時分割の対象とする。文字列は <see cref="SeparatorCandidates"/> と一致させる。</summary>
    private static readonly string[] PhraseSeparators = new[]
    {
        " with ", " feat. ", " feat ",
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
    /// 採用する区切りは 2 系統。(1) フレーズ区切り（" with " / " feat. "）は名義内に出現しない
    /// 明白な連名区切りなので常に分割する。(2) 記号区切り（"、" "・" "&amp;" "," 等）は
    /// 「名義内に紛れ込み得る」ため、検出種が複数あるときは最頻出 1 種だけを採用し、
    /// それ以外の種別はトークン内テキスト（=名義の一部）として温存する（同回数のタイは先出が勝つ）。
    /// スラッシュ "/" "／" は「主名義/スラッシュ相方」の下位区切りなので、他に区切りが一切無いとき
    /// （PERSON の「A/B」連名）に限って採用する。詳細は <see cref="ChooseDominantSeparator"/>。
    /// </para>
    /// <para>例 1：<c>美墨なぎさ(CV:本名陽子)&amp;雪城ほのか(CV:ゆかな)&amp;ヤング・フレッシュ</c> は
    /// "&amp;" 2 回 / "・" 1 回。最頻出 "&amp;" だけを採用するため "ヤング・フレッシュ" が
    /// 1 トークンとして温存される（ユニット名を勝手に分割しない）。</para>
    /// <para>例 2：<c>五條真由美 with 美墨なぎさ(CV:本名陽子)&amp;雪城ほのか(CV:ゆかな)</c> は
    /// フレーズ " with "（常時）＋記号 "&amp;"（ドミナント）の両方で分割され、3 トークンになる。</para>
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

    /// <summary>記号区切り（"、" "・" "&amp;" "," 等）の中から「ドミナント 1 種」を選ぶ。該当無しなら null。
    /// フレーズ区切り（" with " " feat. "）は <see cref="IsPhraseSeparator"/> で別枠の常時分割扱いに
    /// するため本投票からは除外する。スラッシュ（"/" "／"）も除外し、記号区切りが 1 つも無く
    /// スラッシュだけが在るとき（PERSON の「A/B」連名）に限って採用する。
    /// 記号区切り同士は「最頻出 1 種、同点なら先出」で決める（名義内に紛れる "・" "&amp;" を
    /// 巻き込んで割らないための経験則）。</summary>
    private static string? ChooseDominantSeparator(List<(int Pos, string Sep)> occurrences)
    {
        if (occurrences.Count == 0) return null;

        // フレーズ区切り・スラッシュを除いた「記号区切り」候補。
        var symbolOccurrences = occurrences
            .Where(o => !IsPhraseSeparator(o.Sep) && !IsSlashSeparator(o.Sep))
            .ToList();
        if (symbolOccurrences.Count > 0)
        {
            return symbolOccurrences
                .GroupBy(o => o.Sep, StringComparer.Ordinal)
                .Select(g => (Sep: g.Key, Count: g.Count(), FirstPos: g.Min(o => o.Pos)))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.FirstPos)
                .First()
                .Sep;
        }

        // 記号区切りもフレーズ区切りも無く、スラッシュだけが在る場合に限りスラッシュを採用する。
        // フレーズ区切りが在るときのスラッシュは「主名義/スラッシュ相方」の下位区切りなので採用しない
        // （例「五條真由美 with 美墨なぎさ/キュアブラック(CV:…)」の '/' は相方であって連名区切りではない）。
        bool hasPhrase = occurrences.Any(o => IsPhraseSeparator(o.Sep));
        if (!hasPhrase && occurrences.Any(o => IsSlashSeparator(o.Sep)))
        {
            return occurrences.First(o => IsSlashSeparator(o.Sep)).Sep;
        }
        return null;
    }

    /// <summary>区切り文字列がスラッシュ（"/" / "／"）かどうか。区切り採用の優先度判定に使う。</summary>
    private static bool IsSlashSeparator(string sep) =>
        sep.Length == 1 && Array.IndexOf(SlashSeparators, sep[0]) >= 0;

    /// <summary>区切り文字列がフレーズ区切り（" with " / " feat. " / " feat "）かどうか。
    /// 前後に空白を伴う語であり名義内に出現しないため、ドミナント投票と無関係に常時分割する。</summary>
    private static bool IsPhraseSeparator(string sep) => Array.IndexOf(PhraseSeparators, sep) >= 0;

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

    /// <summary>採用区切りで実際のセグメント列を生成する。採用されない区切り出現は文字列に埋め戻す。
    /// 採用される区切りは「フレーズ区切り（常時）」または「<paramref name="activeSeparator"/>（ドミナント記号 1 種）」。
    /// フレーズ区切りはドミナント記号と独立に常に分割するため、「 with 」と「&amp;」のように
    /// 種類の違う連名区切りが同居しても両方で分割される。</summary>
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
                bool isActive = IsPhraseSeparator(sep)
                    || (activeSeparator is not null && string.Equals(sep, activeSeparator, StringComparison.Ordinal));
                if (isActive)
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
