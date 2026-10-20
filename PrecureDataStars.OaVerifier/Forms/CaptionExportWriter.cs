using System.IO;
using System.Text;
using PrecureDataStars.OaVerifier.Ts;
using PrecureDataStars.OaVerifier.Ts.Arib;

namespace PrecureDataStars.OaVerifier.Forms;

/// <summary>書き出しに使うパート 1 件（番組先頭からの秒と、現在のアンカーで求めたメディア時刻の両方を持つ）。</summary>
internal sealed record CaptionExportPart(
    byte EpisodeSeq, string PartType, string NameJa,
    int StartOffsetSec, int EndOffsetSec,
    long StartMediaMs, long EndMediaMs);

/// <summary>書き出しの前提（どの TS を、どの番組先頭アンカーとパートで並べるか）。</summary>
/// <param name="TsPath">元の TS のパス。出力ファイル名に使う。</param>
/// <param name="ProgramStartMediaMs">番組先頭（オフセット 0）のメディア時刻。</param>
/// <param name="Timebase">時刻写像（放送時刻の表示とクロックレートに使う）。</param>
/// <param name="Parts">パート（episode_seq 順）。エピソード未同定なら空。</param>
internal sealed record CaptionExportContext(
    string TsPath, long ProgramStartMediaMs, TsTimebase? Timebase,
    IReadOnlyList<CaptionExportPart> Parts);

/// <summary>
/// 抽出した字幕を、ドキュメントフォルダの <c>{TS名}.captions.tsv</c>（UTF-8 BOM 付き）に書き出す。
///
/// <para>行は時刻順に「境目」と「字幕」の 2 種類。「境目」はパートの始まり（と番組の終わり）ごとに 1 行で、
/// その時刻（番組内・ファイル・放送）とパートの尺を持つ。「字幕」は 1 行 1 字幕で、所属パート（表示開始時刻で決める）・
/// パート内・番組内・ファイル・放送の各時刻、表示秒、本文、外字の印を持つ。</para>
///
/// <para>本文は行を「 / 」で連結し、白以外の文字色（話者の区別に使われる）は〈黄〉のような印で残す。</para>
/// </summary>
internal static class CaptionExportWriter
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private const string BeforeProgram = "番組開始前";
    private const string AfterProgram = "番組終了後";
    private const string KindBoundary = "境目";
    private const string KindCaption = "字幕";

    /// <summary>TSV を書き出し、書いたファイルのパスを返す。</summary>
    public static string Write(CaptionExportContext ctx, IReadOnlyList<CaptionCue> cues)
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(ctx.TsPath) + ".captions.tsv");
        File.WriteAllText(path, BuildTsv(ctx, cues), Utf8Bom);
        return path;
    }

    private static string BuildTsv(CaptionExportContext ctx, IReadOnlyList<CaptionCue> cues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("種別\tパート\tパート内\t番組内\tファイル\t放送\t表示秒\t本文\t外字");

        // 境目の行を、その時刻以降の最初の字幕の前に差し込みながら字幕を並べる。
        int nextBoundary = 0;
        int boundaryCount = ctx.Parts.Count == 0 ? 0 : ctx.Parts.Count + 1; // 各パートの始まり＋番組の終わり
        foreach (var c in cues)
        {
            while (nextBoundary < boundaryCount && BoundaryMediaMs(ctx, nextBoundary) <= c.StartMs)
                AppendBoundary(sb, ctx, nextBoundary++);
            AppendCaption(sb, ctx, c);
        }
        while (nextBoundary < boundaryCount) AppendBoundary(sb, ctx, nextBoundary++);
        return sb.ToString();
    }

    /// <summary>境目 i のメディア時刻。i &lt; Parts.Count はパート i の始まり、i == Parts.Count は番組の終わり（最後のパートの終わり）。</summary>
    private static long BoundaryMediaMs(CaptionExportContext ctx, int i)
        => i < ctx.Parts.Count ? ctx.Parts[i].StartMediaMs : ctx.Parts[^1].EndMediaMs;

    private static void AppendBoundary(StringBuilder sb, CaptionExportContext ctx, int i)
    {
        long ms = BoundaryMediaMs(ctx, i);
        string part, seconds, text;
        if (i < ctx.Parts.Count)
        {
            var p = ctx.Parts[i];
            part = PartLabel(p);
            seconds = (p.EndOffsetSec - p.StartOffsetSec).ToString();
            text = $"ここから {PartLabel(p)}［{p.PartType}］";
        }
        else
        {
            part = AfterProgram;
            seconds = "";
            text = "ここで番組終了";
        }
        AppendRow(sb, KindBoundary, part, i < ctx.Parts.Count ? FmtMs(0) : "",
                  FmtMs(ProgramOffsetMs(ctx, ms)), FmtMs(ms), FmtWall(ctx, ms), seconds, text, "");
    }

    private static void AppendCaption(StringBuilder sb, CaptionExportContext ctx, CaptionCue c)
    {
        string part = "", inPart = "";
        if (ctx.Parts.Count > 0)
        {
            int k = PartIndexOf(ctx, c.StartMs);
            if (k < 0) part = BeforeProgram;
            else if (k >= ctx.Parts.Count) part = AfterProgram;
            else
            {
                var p = ctx.Parts[k];
                part = PartLabel(p);
                inPart = FmtMs(ProgramOffsetMs(ctx, c.StartMs) - p.StartOffsetSec * 1000L);
            }
        }
        AppendRow(sb, KindCaption, part, inPart,
                  FmtMs(ProgramOffsetMs(ctx, c.StartMs)), FmtMs(c.StartMs), FmtWall(ctx, c.StartMs),
                  ((c.EndMs - c.StartMs) / 1000.0).ToString("0.0"), MarkedText(c), c.HasGaiji ? "あり" : "");
    }

    private static void AppendRow(StringBuilder sb, params string[] cells)
        => sb.AppendLine(string.Join('\t', cells.Select(s => s.Replace('\t', ' '))));

    private static string PartLabel(CaptionExportPart p) => $"#{p.EpisodeSeq} {p.NameJa}";

    /// <summary>メディア時刻がどのパートに入るか。-1 = 番組開始前、Parts.Count = 番組終了後。</summary>
    private static int PartIndexOf(CaptionExportContext ctx, long mediaMs)
    {
        if (mediaMs < ctx.Parts[0].StartMediaMs) return -1;
        for (int i = 0; i < ctx.Parts.Count; i++)
            if (mediaMs < ctx.Parts[i].EndMediaMs) return i;
        return ctx.Parts.Count;
    }

    /// <summary>メディア時刻を番組内の経過（ms、番組先頭=0）に直す。クロックレートが 1 でない場合も壁時計の経過に合わせる。</summary>
    private static long ProgramOffsetMs(CaptionExportContext ctx, long mediaMs)
    {
        double rate = ctx.Timebase is { HasMapping: true } tb ? tb.ClockRate : 1.0;
        return (long)Math.Round((mediaMs - ctx.ProgramStartMediaMs) * rate);
    }

    /// <summary>行を「 / 」で連結した本文。色が変わる所に〈色〉の印を付ける（行頭の白は付けない）。</summary>
    private static string MarkedText(CaptionCue c)
    {
        var lines = new List<string>();
        foreach (var line in c.Lines)
        {
            var sb = new StringBuilder();
            int prev = CaptionSpan.DefaultColor;
            foreach (var s in line.Spans)
            {
                if ((s.ColorIndex & 7) != (prev & 7)) sb.Append('〈').Append(CaptionSpan.ColorName(s.ColorIndex)).Append('〉');
                sb.Append(s.Text);
                prev = s.ColorIndex;
            }
            lines.Add(sb.ToString());
        }
        return string.Join(" / ", lines);
    }

    private static string FmtWall(CaptionExportContext ctx, long mediaMs)
        => ctx.Timebase is { HasMapping: true } tb ? tb.WallClockForMediaMs(mediaMs).ToString("HH:mm:ss.f") : "--:--:--";

    private static string FmtMs(long ms)
    {
        string sign = ms < 0 ? "−" : "";
        ms = Math.Abs(ms);
        long totalSec = ms / 1000;
        return $"{sign}{totalSec / 60:00}:{totalSec % 60:00}.{ms % 1000 / 100}";
    }
}
