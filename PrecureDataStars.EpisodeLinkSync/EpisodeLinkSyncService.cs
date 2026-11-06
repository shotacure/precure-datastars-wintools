using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.EpisodeLinkSync;

/// <summary>
/// TV の各話の、放送後に公式から出るリンクを見つけて episodes の空の列にだけ書く。
/// <list type="bullet">
///   <item>あらすじ（<c>toei_anim_summary_url</c>）・ラインナップ（<c>toei_anim_lineup_url</c>）：同じ作品で URL の入っている
///     いちばん近い回の URL の話数の部分（<c>/{話数}/</c>）を差し替えて組み、ページが 200 で返り本文にその回の「第N話」があれば書く。
///     まだ無い回のページは東映アニメーションのトップへ転送されるので、転送は「まだ」とみなす。</item>
///   <item>予告（<c>youtube_trailer_url</c>）：プリキュア公式 YouTube チャンネルの新着一覧から、題が
///     「{作品名}　第N話 予告「{サブタイトル}」」のものを拾う。予告の題のサブタイトルが DB の <c>title_text</c> と違うときは書かずに知らせる。
///     その回が DB に無いときは回を作らずに知らせる（休止の週があり、放送日を機械では決めないため）。</item>
/// </list>
/// 対象は本線の TV（kind_code='TV'）だけ。入っている値は上書きしない。知らせることは <see cref="Notices"/> にためる
/// （同じことは 1 回の実行で 1 度だけ）。
/// </summary>
internal sealed class EpisodeLinkSyncService
{
    /// <summary>updated_by に入れる名前。</summary>
    public const string UpdatedBy = "EpisodeLinkSync";

    /// <summary>プリキュア公式 YouTube チャンネル（@precure）。</summary>
    public const string PrecureChannelId = "UCSsjL41NsyqSNNbanuI0htg";

    /// <summary>まとめて見る回の範囲（放送日時がいまの前後この幅に入る回）。古い回のページを毎日見に行かないよう狭くとる。</summary>
    private static readonly TimeSpan PastWindow = TimeSpan.FromDays(28);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromDays(21);

    /// <summary>予告の題。NFKC で正規化した後の形（全角の数字・空白は半角になる）。</summary>
    private static readonly Regex TrailerTitle = new(@"^(?<series>.+?)\s*第\s*(?<no>\d+)\s*話\s*予告\s*「(?<sub>.*)」\s*$", RegexOptions.Compiled);

    private readonly EpisodesRepository _episodes;
    private readonly SeriesRepository _series;
    private readonly WebProbe _web;
    private readonly bool _dryRun;
    private readonly Action<string> _log;
    private readonly HashSet<string> _noticed = new(StringComparer.Ordinal);

    public EpisodeLinkSyncService(EpisodesRepository episodes, SeriesRepository series, WebProbe web, bool dryRun, Action<string> log)
    {
        _episodes = episodes;
        _series = series;
        _web = web;
        _dryRun = dryRun;
        _log = log;
    }

    /// <summary>知らせること（祥太さんが見て手で直すもの）。</summary>
    public List<string> Notices { get; } = [];

    /// <summary>書いた件数。</summary>
    public int WrittenCount { get; private set; }

    /// <summary>DB の回と作品をその時点の内容で持つ。</summary>
    private sealed record Snapshot(IReadOnlyList<Episode> Episodes, IReadOnlyDictionary<int, Series> SeriesById)
    {
        public bool IsTv(Episode e) => SeriesById.TryGetValue(e.SeriesId, out var s) && s.KindCode == "TV";

        public Episode? Find(int seriesId, int seriesEpNo)
            => Episodes.FirstOrDefault(e => e.SeriesId == seriesId && e.SeriesEpNo == seriesEpNo);
    }

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        var episodes = await _episodes.GetAllAsync(ct).ConfigureAwait(false);
        var series = await _series.GetAllAsync(ct).ConfigureAwait(false);
        return new Snapshot(episodes, series.ToDictionary(s => s.SeriesId));
    }

    /// <summary>
    /// 1 回だけ見て回る。放送日時がいまの前後（過去 28 日・先 21 日）の TV の回のうち、放送済みの回のあらすじ・ラインナップと、
    /// 新着一覧に出ている予告を拾う。
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var snap = await LoadAsync(ct).ConfigureAwait(false);
        var window = snap.Episodes
            .Where(e => snap.IsTv(e) && e.OnAirAt >= now - PastWindow && e.OnAirAt <= now + FutureWindow)
            .ToList();
        _log($"見る回：{window.Count} 話（{now - PastWindow:M/d}〜{now + FutureWindow:M/d} に放送の TV）");

        foreach (var ep in window.Where(e => e.OnAirAt <= now))
        {
            if (string.IsNullOrEmpty(ep.ToeiAnimSummaryUrl))
                await TryPageAsync(snap, ep, EpisodeLinkColumn.ToeiAnimSummary, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(ep.ToeiAnimLineupUrl))
                await TryPageAsync(snap, ep, EpisodeLinkColumn.ToeiAnimLineup, ct).ConfigureAwait(false);
        }

        var feed = await _web.ReadChannelFeedAsync(PrecureChannelId, ct).ConfigureAwait(false);
        if (feed is null)
        {
            Notice("feed", "YouTube の新着一覧を読めませんでした（予告は見ていません）。");
            return;
        }
        var ids = window.Select(e => e.EpisodeId).ToHashSet();
        foreach (var video in feed)
        {
            if (ParseTrailer(snap, video) is not { } t) continue;
            // 範囲の外の回（古い回の予告の再掲など）は見ない。DB に無い回は、範囲の先にあるはずの回として扱う。
            if (t.Episode is { } e && !ids.Contains(e.EpisodeId)) continue;
            await ApplyTrailerAsync(t, video, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 今日放送の TV の回について、あらすじのページと次の回の予告が出るまで <paramref name="interval"/> おきに見続ける。
    /// どちらもそろったら true で終わる。<paramref name="until"/> を過ぎたら、そろわなかったものを知らせて false で終わる。
    /// 今日放送の回が無ければ（休止の週）何もせずに true。ラインナップは数日遅れで出るので、ここでは見ない。
    /// </summary>
    public async Task<bool> RunWatchAsync(DateTime until, TimeSpan interval, CancellationToken ct)
    {
        var today = DateTime.Today;
        var first = await LoadAsync(ct).ConfigureAwait(false);
        var target = first.Episodes.Where(e => first.IsTv(e) && e.OnAirAt.Date == today).OrderBy(e => e.OnAirAt).LastOrDefault();
        if (target is null)
        {
            _log("今日放送の TV の回はありません。");
            return true;
        }
        string seriesTitle = first.SeriesById[target.SeriesId].Title;
        int nextNo = target.SeriesEpNo + 1;
        _log($"{seriesTitle} 第{target.SeriesEpNo}話（episode {target.EpisodeId}）のあらすじのページと、第{nextNo}話の予告を {interval.TotalSeconds:0} 秒おきに見ます（{until:HH:mm} まで）。");

        bool summaryDone = false, trailerDone = false;
        while (true)
        {
            // 手で入れた分も拾えるよう、毎回 DB を読み直す。
            var snap = await LoadAsync(ct).ConfigureAwait(false);
            var ep = snap.Episodes.FirstOrDefault(e => e.EpisodeId == target.EpisodeId);
            if (ep is null) { Notice("gone", $"episode {target.EpisodeId} が DB から無くなりました。"); return false; }

            if (!summaryDone)
            {
                summaryDone = !string.IsNullOrEmpty(ep.ToeiAnimSummaryUrl)
                    || await TryPageAsync(snap, ep, EpisodeLinkColumn.ToeiAnimSummary, ct).ConfigureAwait(false);
                if (summaryDone) _log($"第{ep.SeriesEpNo}話のあらすじのページ：そろいました。");
            }
            if (!trailerDone)
            {
                var next = snap.Find(ep.SeriesId, nextNo);
                if (next is not null && !string.IsNullOrEmpty(next.YoutubeTrailerUrl))
                    trailerDone = true;
                else if (await _web.ReadChannelFeedAsync(PrecureChannelId, ct).ConfigureAwait(false) is { } feed)
                {
                    foreach (var video in feed)
                    {
                        if (ParseTrailer(snap, video) is not { } t || t.SeriesId != ep.SeriesId || t.EpisodeNo != nextNo) continue;
                        trailerDone = await ApplyTrailerAsync(t, video, ct).ConfigureAwait(false);
                        break;
                    }
                }
                if (trailerDone) _log($"第{nextNo}話の予告：そろいました。");
            }

            if (summaryDone && trailerDone) return true;
            if (DateTime.Now >= until)
            {
                if (!summaryDone) Notice("watch-summary", $"{seriesTitle} 第{ep.SeriesEpNo}話のあらすじのページが {until:HH:mm} までに見つかりませんでした。");
                if (!trailerDone) Notice("watch-trailer", $"{seriesTitle} 第{nextNo}話の予告が {until:HH:mm} までに見つかりませんでした。");
                return false;
            }
            await Task.Delay(interval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>あらすじ・ラインナップのページを組んで確かめ、あれば書く。書いた（またはもう入っていた）ら true。</summary>
    private async Task<bool> TryPageAsync(Snapshot snap, Episode ep, EpisodeLinkColumn column, CancellationToken ct)
    {
        string label = column == EpisodeLinkColumn.ToeiAnimSummary ? "あらすじ" : "ラインナップ";
        string seriesTitle = snap.SeriesById[ep.SeriesId].Title;
        string? url = DeriveUrl(snap, ep, column);
        if (url is null)
        {
            Notice($"derive:{column}:{ep.SeriesId}",
                $"{seriesTitle} は{label}の URL の入った回が無いので、第{ep.SeriesEpNo}話の URL を組めません（最初の 1 話は手で入れてください）。");
            return false;
        }
        if (await _web.ProbeEpisodePageAsync(url, ep.SeriesEpNo, ct).ConfigureAwait(false) != ProbeResult.Found) return false;
        return await WriteAsync(ep, column, url, $"{seriesTitle} 第{ep.SeriesEpNo}話の{label}", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 同じ作品で同じ列の URL が入っている、話数のいちばん近い回の URL から、その回の話数の部分（<c>/{話数}/</c>、最後に出てくるもの）を
    /// 差し替えて組む。組めなければ null。
    /// </summary>
    private static string? DeriveUrl(Snapshot snap, Episode ep, EpisodeLinkColumn column)
    {
        foreach (var donor in snap.Episodes
                     .Where(e => e.SeriesId == ep.SeriesId && e.EpisodeId != ep.EpisodeId && !string.IsNullOrEmpty(LinkOf(e, column)))
                     .OrderBy(e => Math.Abs(e.SeriesEpNo - ep.SeriesEpNo)))
        {
            string url = LinkOf(donor, column)!;
            string seg = $"/{donor.SeriesEpNo}/";
            int i = url.LastIndexOf(seg, StringComparison.Ordinal);
            if (i >= 0) return string.Concat(url.AsSpan(0, i), $"/{ep.SeriesEpNo}/", url.AsSpan(i + seg.Length));
        }
        return null;
    }

    private static string? LinkOf(Episode e, EpisodeLinkColumn column) => column switch
    {
        EpisodeLinkColumn.ToeiAnimSummary => e.ToeiAnimSummaryUrl,
        EpisodeLinkColumn.ToeiAnimLineup => e.ToeiAnimLineupUrl,
        _ => e.YoutubeTrailerUrl
    };

    /// <summary>新着一覧の 1 本を予告として読んだ結果。<see cref="Episode"/> はその回が DB に無ければ null。</summary>
    private sealed record Trailer(int SeriesId, string SeriesTitle, int EpisodeNo, string Subtitle, Episode? Episode);

    /// <summary>題が「{TV の作品名} 第N話 予告「…」」の形なら読む。作品名は NFKC で正規化して比べる。</summary>
    private static Trailer? ParseTrailer(Snapshot snap, FeedVideo video)
    {
        var m = TrailerTitle.Match(Normalize(video.Title));
        if (!m.Success) return null;
        string seriesPart = m.Groups["series"].Value.Trim();
        var series = snap.SeriesById.Values.FirstOrDefault(s => s.KindCode == "TV" && Normalize(s.Title) == seriesPart);
        if (series is null || !int.TryParse(m.Groups["no"].Value, out int no)) return null;
        return new Trailer(series.SeriesId, series.Title, no, m.Groups["sub"].Value, snap.Find(series.SeriesId, no));
    }

    /// <summary>
    /// 予告を書く。もう入っている・書いた・知らせた（DB に回が無い・サブタイトルが違う）ときは true（それ以上待たなくてよい）。
    /// </summary>
    private async Task<bool> ApplyTrailerAsync(Trailer t, FeedVideo video, CancellationToken ct)
    {
        string what = $"{t.SeriesTitle} 第{t.EpisodeNo}話の予告";
        if (t.Episode is not { } ep)
        {
            Notice($"trailer-norow:{t.SeriesId}:{t.EpisodeNo}",
                $"{what}が出ましたが、DB にその回がありません。サブタイトル「{t.Subtitle}」 {video.WatchUrl}");
            return true;
        }
        if (!string.IsNullOrEmpty(ep.YoutubeTrailerUrl)) return true;
        if (string.IsNullOrEmpty(ep.TitleText))
        {
            Notice($"trailer-notitle:{ep.EpisodeId}", $"{what}を入れました。DB のサブタイトルが空です。予告の題は「{t.Subtitle}」です。");
        }
        else if (Normalize(ep.TitleText) != Normalize(t.Subtitle))
        {
            Notice($"trailer-mismatch:{ep.EpisodeId}",
                $"{what}のサブタイトルが DB と違うので入れていません。予告「{t.Subtitle}」・DB「{ep.TitleText}」 {video.WatchUrl}");
            return true;
        }
        await WriteAsync(ep, EpisodeLinkColumn.YoutubeTrailer, video.WatchUrl, what, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>空の列にだけ書く。書いた（または書かずに試すとき）は true。</summary>
    private async Task<bool> WriteAsync(Episode ep, EpisodeLinkColumn column, string url, string what, CancellationToken ct)
    {
        if (_dryRun)
        {
            _log($"{what}：{url}（書かずに試しています）");
            return true;
        }
        if (await _episodes.FillLinkIfEmptyAsync(ep.EpisodeId, column, url, UpdatedBy, ct).ConfigureAwait(false))
        {
            WrittenCount++;
            _log($"{what}を入れました：episode {ep.EpisodeId} {url}");
        }
        else
        {
            _log($"{what}は、見ている間にもう入っていました：episode {ep.EpisodeId}");
        }
        return true;
    }

    private void Notice(string key, string text)
    {
        if (!_noticed.Add(key)) return;
        Notices.Add(text);
        _log("【知らせ】" + text);
    }

    /// <summary>比べるための正規化（NFKC・空白の詰め・前後の空白を除く）。</summary>
    private static string Normalize(string s)
        => Regex.Replace(s.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();
}
