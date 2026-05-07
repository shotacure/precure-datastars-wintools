using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// <see cref="EpisodePart"/> の列から <see cref="FormatTableModel"/> を組み立てるヘルパー。
/// <para>
/// 本放送列は放送開始時刻（<c>episode.on_air_at</c> の時刻部分）を起点に、
/// 各パートの累積「開始時刻〜終了時刻」を <c>HH:mm:ss</c> 形式で出す。
/// この計算は エピソードエディタ <c>EpisodesEditorForm.LoadPartsForEpisodeAsync</c> と同じ。
/// </para>
/// <para>
/// 配信列は時刻ではなく「累積開始秒（<c>m:ss</c>）」と「尺（<c>m:ss</c>）」を出す。
/// 累積開始秒の起点は <c>series.vod_intro</c>（配信版に挿入される先頭イントロ尺）。
/// この扱いは エピソードエディタの <c>RecalcTotals</c> が配信総尺の計算に
/// <c>series.vod_intro</c> を加算しているのと同じ理屈で、配信版では「先頭にイントロが
/// 入る分だけ各パートの開始位置がずれる」という事実を反映する。
/// </para>
/// <para>
/// 円盤列は累積時刻を持たず、尺のみ表示する。
/// </para>
/// </summary>
public static class FormatTableBuilder
{
    /// <summary>
    /// パート行（episode_seq 昇順）を受け取って、フォーマット表モデルを組み立てる。
    /// </summary>
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

        return new FormatTableModel
        {
            Rows = rows,
            OaTotal = HtmlUtil.FormatSeconds(oaCum),
            VodTotal = HtmlUtil.FormatSeconds(vodCum),
            DiscTotal = HtmlUtil.FormatSeconds(discCum)
        };
    }
}
