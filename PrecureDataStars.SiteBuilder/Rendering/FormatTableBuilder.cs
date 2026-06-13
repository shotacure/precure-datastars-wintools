using System.Globalization;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// <see cref="EpisodePart"/> の列から <see cref="FormatTableModel"/> を組み立てるヘルパー。
/// 本放送列は放送開始時刻（<c>episode.on_air_at</c> の時刻部分）を起点に、
/// 各パートの累積「開始時刻〜終了時刻」を <c>HH:mm:ss</c> 形式で出す。
/// この計算は エピソードエディタ <c>EpisodesEditorForm.LoadPartsForEpisodeAsync</c> と同じ。
/// 配信列は時刻ではなく「累積開始秒（<c>m:ss</c>）」と「尺（<c>m:ss</c>）」を出す。
/// 累積開始秒の起点は <c>series.vod_intro</c>（配信版に挿入される先頭イントロ尺）。
/// この扱いは エピソードエディタの <c>RecalcTotals</c> が配信総尺の計算に
/// <c>series.vod_intro</c> を加算しているのと同じ理屈で、配信版では「先頭にイントロが
/// 入る分だけ各パートの開始位置がずれる」という事実を反映する。
/// 円盤列は累積時刻を持たず、尺のみ表示する。
/// </summary>
public static class FormatTableBuilder
{
    /// <summary>パート行（episode_seq 昇順）を受け取って、フォーマット表モデルを組み立てる。</summary>
    /// <param name="parts">対象エピソードのパート行。</param>
    /// <param name="onAirAt">エピソードの放送開始日時（<c>episodes.on_air_at</c>）。本放送列の起点。</param>
    /// <param name="vodIntroSeconds">配信版の先頭イントロ尺（<c>series.vod_intro</c>）。null のときは 0 として扱う。</param>
    /// <param name="ctx">パート種別マスタ参照用。</param>
    public static FormatTableModel Build(
        IReadOnlyList<EpisodePart> parts,
        DateTime onAirAt,
        ushort? vodIntroSeconds,
        BuildContext ctx)
    {
        var partTypeMap = ctx.PartTypeByCode;

        // 媒体ごとの累積秒。配信は vod_intro を起点に積む（最初のパートの開始位置が intro 秒数になる）。
        int oaCum = 0;
        int vodCum = vodIntroSeconds ?? 0;
        int discCum = 0;

        var rows = new List<FormatTableRow>(parts.Count);
        foreach (var p in parts.OrderBy(x => x.EpisodeSeq))
        {
            var row = new FormatTableRow
            {
                PartName = partTypeMap.TryGetValue(p.PartType, out var pt) ? pt.NameJa : p.PartType,
                Notes = p.Notes ?? ""
            };

            if (p.OaLength is ushort oa)
            {
                var start = onAirAt.AddSeconds(oaCum);
                var end = start.AddSeconds(oa);
                row.OaStart = start.ToString(@"HH\:mm\:ss");
                row.OaEnd = end.ToString(@"HH\:mm\:ss");
                row.OaDuration = HtmlUtil.FormatSeconds(oa);
                oaCum += oa;
            }

            if (p.VodLength is ushort vod)
            {
                // 累積開始秒はパート尺加算前の値（=このパートが何秒目から始まるか）
                row.VodStart = HtmlUtil.FormatSeconds(vodCum);
                row.VodDuration = HtmlUtil.FormatSeconds(vod);
                vodCum += vod;
            }

            if (p.DiscLength is ushort disc)
            {
                row.DiscDuration = HtmlUtil.FormatSeconds(disc);
                discCum += disc;
            }

            rows.Add(row);
        }

        var (bars, legend) = BuildBars(parts, vodIntroSeconds, ctx);

        // 本放送フォーマットの調査中フラグ。Catalog 側の運用で、本放送で実測できていないパートには
        // 備考に「【本放送未確認】」マーカーを入れている。1 つでも含まれていれば調査中扱い。
        bool oaUnderInvestigation = parts.Any(p => (p.Notes ?? "").Contains("【本放送未確認】", StringComparison.Ordinal));

        return new FormatTableModel
        {
            Rows = rows,
            OaTotal = HtmlUtil.FormatSeconds(oaCum),
            VodTotal = HtmlUtil.FormatSeconds(vodCum),
            DiscTotal = HtmlUtil.FormatSeconds(discCum),
            OaUnderInvestigation = oaUnderInvestigation,
            Bars = bars,
            Legend = legend
        };
    }

    /// <summary>パート種別コードを帯グラフの色パレットキーへ写像する。
    /// CM① 〜 CM④ は「CM」、前後の提供クレジットは「提供」に束ね、
    /// 構成上の主要パート以外（エンドカード・各種告知など）は misc（無彩色）にまとめる。</summary>
    private static string PaletteKey(string partType) => partType switch
    {
        "AVANT" => "avant",
        "OPENING" => "op",
        "ENDING" => "ed",
        "PART_A" => "a",
        "PART_B" => "b",
        "PART_C" => "c",
        "TRAILER" => "trailer",
        _ when partType.StartsWith("CM", StringComparison.Ordinal) => "cm",
        _ when partType.StartsWith("SPONSOR_CREDIT", StringComparison.Ordinal) => "sponsor",
        _ => "misc"
    };

    /// <summary>パート種別コードに対応する帯グラフ・凡例・統計カード共用の CSS クラス名（fmt-p-*）。</summary>
    public static string PaletteCss(string partType) => "fmt-p-" + PaletteKey(partType);

    /// <summary>セグメント内に出す短縮ラベル。misc はパートの正式名をそのまま使う
    /// （幅が許せば「コーナー」等が出る。収まらなければ ShowLabel 側で隠れる）。</summary>
    private static string SegmentShortLabel(string paletteKey, string fullName) => paletteKey switch
    {
        "avant" => "アバン",
        "op" => "OP",
        "ed" => "ED",
        "a" => "Aパート",
        "b" => "Bパート",
        "c" => "Cパート",
        "cm" => "CM",
        "sponsor" => "提供",
        "trailer" => "予告",
        "intro" => "イントロ",
        _ => fullName
    };

    /// <summary>凡例のラベル。束ねたパレットキーには束ねた後の総称を与える。</summary>
    private static string LegendLabel(string paletteKey) => paletteKey switch
    {
        "avant" => "アバンタイトル",
        "op" => "オープニング",
        "ed" => "エンディング",
        "a" => "Aパート",
        "b" => "Bパート",
        "c" => "Cパート",
        "cm" => "CM",
        "sponsor" => "提供クレジット",
        "trailer" => "予告",
        "intro" => "配信イントロ",
        _ => "その他"
    };

    /// <summary>ラベルの概算表示幅（半角 1・全角 2 の単位数）。セグメントに収まるかの判定に使う。</summary>
    private static double LabelUnits(string s)
    {
        double units = 0;
        foreach (var ch in s) units += ch <= 0x7F ? 1 : 2;
        return units;
    }

    /// <summary>帯グラフのメディア別バーと色凡例を組み立てる。
    /// 3 本のバーは「最長メディア＝100%」の同一スケールで幅を割り当て、メディア間の総尺差が
    /// そのまま見た目の長さの差になるようにする。配信バーは先頭に vod_intro のイントロ
    /// セグメントを挿入し、配信版で各パートの開始位置がずれる事実を可視化する。</summary>
    private static (IReadOnlyList<FormatBar> Bars, IReadOnlyList<FormatLegendItem> Legend) BuildBars(
        IReadOnlyList<EpisodePart> parts,
        ushort? vodIntroSeconds,
        BuildContext ctx)
    {
        var partTypeMap = ctx.PartTypeByCode;
        var ordered = parts.OrderBy(x => x.EpisodeSeq).ToList();

        // (パレットキー, フル名, 秒) の素朴なタプル列をメディアごとに作る。
        List<(string Key, string FullName, int Seconds)> Collect(Func<EpisodePart, ushort?> pick)
        {
            var list = new List<(string, string, int)>();
            foreach (var p in ordered)
            {
                if (pick(p) is not ushort sec) continue;
                var fullName = partTypeMap.TryGetValue(p.PartType, out var pt) ? pt.NameJa : p.PartType;
                list.Add((PaletteKey(p.PartType), fullName, sec));
            }
            return list;
        }

        var oaSegs = Collect(p => p.OaLength);
        var vodSegs = Collect(p => p.VodLength);
        var discSegs = Collect(p => p.DiscLength);

        // 配信はパートデータがあるときだけ vod_intro を先頭に積む（イントロだけのバーは作らない）。
        if (vodSegs.Count > 0 && (vodIntroSeconds ?? 0) > 0)
        {
            vodSegs.Insert(0, ("intro", "配信イントロ", vodIntroSeconds!.Value));
        }

        var sources = new List<(string Medium, List<(string Key, string FullName, int Seconds)> Segs)>();
        if (oaSegs.Count > 0) sources.Add(("本放送", oaSegs));
        if (vodSegs.Count > 0) sources.Add(("配信", vodSegs));
        if (discSegs.Count > 0) sources.Add(("円盤", discSegs));
        if (sources.Count == 0)
        {
            return (Array.Empty<FormatBar>(), Array.Empty<FormatLegendItem>());
        }

        int maxTotal = sources.Max(s => s.Segs.Sum(x => x.Seconds));

        var bars = new List<FormatBar>(sources.Count);
        var legend = new List<FormatLegendItem>();
        var seenKeys = new HashSet<string>();

        foreach (var (medium, segs) in sources)
        {
            int total = segs.Sum(x => x.Seconds);
            var segments = new List<FormatBarSegment>(segs.Count);
            foreach (var (key, fullName, seconds) in segs)
            {
                // ラベル収まり判定は「最長メディア＝100%」スケールでの実効幅比率で行う。
                double sharePct = maxTotal > 0 ? seconds * 100.0 / maxTotal : 0;
                var shortLabel = SegmentShortLabel(key, fullName);
                segments.Add(new FormatBarSegment
                {
                    PaletteCss = "fmt-p-" + key,
                    Seconds = seconds,
                    ShortLabel = shortLabel,
                    ShowLabel = shortLabel != "" && sharePct >= LabelUnits(shortLabel) * 0.75 + 1.2,
                    SmallLabel = sharePct < 14,
                    Title = $"{fullName} {HtmlUtil.FormatSeconds(seconds)}"
                });

                if (seenKeys.Add(key))
                {
                    legend.Add(new FormatLegendItem { PaletteCss = "fmt-p-" + key, Label = LegendLabel(key) });
                }
            }

            bars.Add(new FormatBar
            {
                MediumLabel = medium,
                TotalLabel = HtmlUtil.FormatSeconds(total),
                WidthPercent = maxTotal > 0
                    ? (total * 100.0 / maxTotal).ToString("0.##", CultureInfo.InvariantCulture)
                    : "100",
                Segments = segments
            });
        }

        return (bars, legend);
    }
}
