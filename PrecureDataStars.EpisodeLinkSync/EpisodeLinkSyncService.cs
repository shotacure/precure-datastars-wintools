using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Data.TitleCharStatsJson;

namespace PrecureDataStars.EpisodeLinkSync;

/// <summary>
/// TV の各話の、放送後に公式から出るリンクを見つけて episodes の空の列にだけ書く。
/// <list type="bullet">
///   <item>あらすじ（<c>toei_anim_summary_url</c>）・ラインナップ（<c>toei_anim_lineup_url</c>）：同じ作品で URL の入っている
///     いちばん近い回の URL の話数の部分（<c>/{話数}/</c>）を差し替えて組み、ページが 200 で返り本文にその回の「第N話」があれば書く。
///     まだ無い回のページは東映アニメーションのトップへ転送されるので、転送は「まだ」とみなす。</item>
///   <item>予告（<c>youtube_trailer_url</c>）：プリキュア公式 YouTube チャンネルの新着（Data API で読むアップロードの再生リストの先頭）から、題が
///     「{作品名}　第N話 予告「{サブタイトル}」」のものを拾う。予告の題のサブタイトルが DB の <c>title_text</c> と違うときは書かずに知らせる。
///     その回が DB に無いときは、1 回だけ見て回るときは回を作らずに知らせる（休止の週があり、放送日を機械では決められないため）。</item>
///   <item>日曜の見続ける回だけは、次の回の予告が出たときに、その回が DB に無ければ予告の題から回を仮で作り、
///     回はあるのにサブタイトルが空ならサブタイトルを仮で入れる（<see cref="CreateFromTrailerAsync"/>・<see cref="FillTitleFromTrailerAsync"/>）。
///     作った・入れたことは知らせ、ふりがな・ルビ・放送日は祥太さんが確かめる。</item>
/// </list>
/// 対象は本線の TV（kind_code='TV'）だけ。入っている値は上書きしない。知らせることは <see cref="Notices"/> にためる
/// （同じことは 1 回の実行で 1 度だけ）。
/// </summary>
public sealed class EpisodeLinkSyncService
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

    /// <summary>予告の題の末尾の「…」。DB に入れるサブタイトルは正規化する前の題から、字を変えずに取る。</summary>
    private static readonly Regex TrailerSubtitleRaw = new(@"「(?<sub>.*)」\s*$", RegexOptions.Compiled);

    /// <summary>サブタイトルの中の改行の位置を表す空白（半角・全角）。ルビ付き表記では改行にする。</summary>
    private static readonly Regex SubtitleBreak = new(@"[ 　]+", RegexOptions.Compiled);

    private readonly EpisodesRepository _episodes;
    private readonly SeriesRepository _series;
    private readonly WebProbe _web;
    private readonly bool _dryRun;
    private readonly Action<string> _log;
    private readonly Action<string> _status;
    private readonly HashSet<string> _noticed = new(StringComparer.Ordinal);

    /// <param name="status">見続ける間の「いま何を待っているか」の 1 行を出す先（ログには残さない。窓のいちばん下で書き換える）。</param>
    public EpisodeLinkSyncService(EpisodesRepository episodes, SeriesRepository series, WebProbe web, bool dryRun,
        Action<string> log, Action<string> status)
    {
        _episodes = episodes;
        _series = series;
        _web = web;
        _dryRun = dryRun;
        _log = log;
        _status = status;
    }

    /// <summary>
    /// 見続ける回の進み具合。<see cref="BeginWatchAsync"/> で作り、<see cref="WatchStepAsync"/> で 1 回ずつ進める。
    /// </summary>
    public sealed class WatchSession
    {
        internal WatchSession(Episode target, string seriesTitle)
        {
            Target = target;
            SeriesTitle = seriesTitle;
        }

        /// <summary>今日放送の回。</summary>
        public Episode Target { get; }

        /// <summary>その回の作品名。</summary>
        public string SeriesTitle { get; }

        /// <summary>予告を待つ次の回の話数。</summary>
        public int NextNo => Target.SeriesEpNo + 1;

        /// <summary>あらすじのページの URL が入った（入っていた）。</summary>
        public bool SummaryDone { get; internal set; }

        /// <summary>次の回の予告が入った（入っていた・書かずに知らせた）。</summary>
        public bool TrailerDone { get; internal set; }

        /// <summary>YouTube Data API に待っても直らない断りを受けて、予告を待つのをやめた。</summary>
        public bool TrailerGivenUp { get; internal set; }

        /// <summary>見ている回が DB から無くなった。</summary>
        public bool Gone { get; internal set; }

        /// <summary>これ以上見回らなくてよい。</summary>
        public bool Finished => Gone || (SummaryDone && (TrailerDone || TrailerGivenUp));
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

        var feed = await _web.ReadChannelUploadsAsync(PrecureChannelId, ct).ConfigureAwait(false);
        if (feed is null)
        {
            Notice("feed", "YouTube の新着を読めませんでした（予告は見ていません）。"
                + (_web.YouTubeStoppedReason is { } why ? $"{why}" : ""));
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
    /// <paramref name="today"/>（ふだんは今日）に放送の TV の回について、あらすじのページと次の回の予告が出るまで <paramref name="interval"/> おきに見続ける。
    /// どちらもそろったら true で終わる。<paramref name="until"/> を過ぎたら、そろわなかったものを知らせて false で終わる。
    /// 今日放送の回が無ければ（休止の週）何もせずに true。ラインナップは数日遅れで出るので、ここでは見ない。
    /// YouTube Data API に待っても直らない断り（クォータ超過など）を受けたら、予告は待つのをやめて知らせる。
    /// 待っている間は、回るたびに待っているものと締め切りまでの残りを 1 行で出す（<see cref="_status"/>）。
    /// 1 回ぶんの見回りは <see cref="WatchStepAsync"/> で、ほかの流れから 1 回ずつ呼ぶこともできる。
    /// </summary>
    public async Task<bool> RunWatchAsync(DateTime today, DateTime until, TimeSpan interval, CancellationToken ct)
    {
        var session = await BeginWatchAsync(today, ct).ConfigureAwait(false);
        if (session is null) return true;
        _log($"{session.SeriesTitle} 第{session.Target.SeriesEpNo}話（episode {session.Target.EpisodeId}）のあらすじのページと、第{session.NextNo}話の予告を {interval.TotalSeconds:0} 秒おきに見ます（{until:HH:mm} まで）。");

        while (true)
        {
            await WatchStepAsync(session, ct).ConfigureAwait(false);
            if (session.Gone) return false;
            if (session.SummaryDone && session.TrailerDone) return true;
            if (session.SummaryDone && session.TrailerGivenUp) return false;
            if (DateTime.Now >= until)
            {
                EndWatch(session, until);
                return false;
            }
            _status(WaitingStatus(session, until));
            await Task.Delay(interval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 見続ける回を決める。<paramref name="today"/> に放送の TV の回（同じ日に 2 回あればあとの回）。無ければ（休止の週）null。
    /// </summary>
    public async Task<WatchSession?> BeginWatchAsync(DateTime today, CancellationToken ct)
    {
        var first = await LoadAsync(ct).ConfigureAwait(false);
        var target = first.Episodes.Where(e => first.IsTv(e) && e.OnAirAt.Date == today.Date).OrderBy(e => e.OnAirAt).LastOrDefault();
        if (target is null)
        {
            _log("今日放送の TV の回はありません。");
            return null;
        }
        return new WatchSession(target, first.SeriesById[target.SeriesId].Title);
    }

    /// <summary>
    /// 見続ける回の 1 回ぶんの見回り：まだのあらすじのページと次の回の予告を 1 度ずつ見て、見つかれば書く。
    /// 手で入れた分も拾えるよう、毎回 DB を読み直す。
    /// </summary>
    public async Task WatchStepAsync(WatchSession session, CancellationToken ct)
    {
        if (session.Finished) return;
        var snap = await LoadAsync(ct).ConfigureAwait(false);
        var ep = snap.Episodes.FirstOrDefault(e => e.EpisodeId == session.Target.EpisodeId);
        if (ep is null)
        {
            session.Gone = true;
            Notice("gone", $"episode {session.Target.EpisodeId} が DB から無くなりました。");
            return;
        }

        if (!session.SummaryDone)
        {
            session.SummaryDone = !string.IsNullOrEmpty(ep.ToeiAnimSummaryUrl)
                || await TryPageAsync(snap, ep, EpisodeLinkColumn.ToeiAnimSummary, ct).ConfigureAwait(false);
            if (session.SummaryDone) _log($"第{ep.SeriesEpNo}話のあらすじのページ：そろいました。");
        }
        if (!session.TrailerDone && !session.TrailerGivenUp)
        {
            int nextNo = session.NextNo;
            var next = snap.Find(ep.SeriesId, nextNo);
            if (next is not null && !string.IsNullOrEmpty(next.YoutubeTrailerUrl))
                session.TrailerDone = true;
            else if (await _web.ReadChannelUploadsAsync(PrecureChannelId, ct).ConfigureAwait(false) is { } feed)
            {
                foreach (var video in feed)
                {
                    if (ParseTrailer(snap, video) is not { } t || t.SeriesId != ep.SeriesId || t.EpisodeNo != nextNo) continue;
                    session.TrailerDone = await ApplyTrailerAsync(t, video, ct, snap).ConfigureAwait(false);
                    break;
                }
            }
            if (session.TrailerDone) _log($"第{nextNo}話の予告：そろいました。");
            else if (_web.YouTubeStoppedReason is { } why)
            {
                session.TrailerGivenUp = true;
                Notice("watch-trailer-stopped", $"{session.SeriesTitle} 第{nextNo}話の予告は、YouTube Data API に断られたので待つのをやめました。{why}");
            }
        }
    }

    /// <summary>締め切り（<paramref name="until"/>）までにそろわなかったものを知らせる。</summary>
    public void EndWatch(WatchSession session, DateTime until)
    {
        if (!session.SummaryDone)
            Notice("watch-summary", $"{session.SeriesTitle} 第{session.Target.SeriesEpNo}話のあらすじのページが {until:HH:mm} までに見つかりませんでした。");
        if (!session.TrailerDone && !session.TrailerGivenUp)
            Notice("watch-trailer", $"{session.SeriesTitle} 第{session.NextNo}話の予告が {until:HH:mm} までに見つかりませんでした。");
    }

    /// <summary>見続ける間の 1 行：待っているもの・YouTube を最後に読めた時刻・締め切りまでの残り。</summary>
    public string WaitingStatus(WatchSession session, DateTime until)
    {
        var waiting = new List<string>();
        if (!session.SummaryDone) waiting.Add($"第{session.Target.SeriesEpNo}話のあらすじ");
        if (!session.TrailerDone && !session.TrailerGivenUp)
        {
            string yt = _web.YouTubeLastReadAt is { } at ? $"YouTube は {at:HH:mm:ss} に読めています" : "YouTube をまだ読めていません";
            waiting.Add($"第{session.NextNo}話の予告（{yt}）");
        }
        var left = until - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return $"待っています：{string.Join("・", waiting)}　{until:HH:mm} まであと {(int)left.TotalHours}:{left.Minutes:00}";
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

    /// <summary>
    /// 新着一覧の 1 本を予告として読んだ結果。<see cref="Episode"/> はその回が DB に無ければ null。
    /// <see cref="Subtitle"/> は比べるための正規化した形、<see cref="RawSubtitle"/> は題のままの字（DB に入れるのはこちら）。
    /// </summary>
    private sealed record Trailer(int SeriesId, string SeriesTitle, int EpisodeNo, string Subtitle, string RawSubtitle, Episode? Episode);

    /// <summary>題が「{TV の作品名} 第N話 予告「…」」の形なら読む。作品名は NFKC で正規化して比べる。</summary>
    private static Trailer? ParseTrailer(Snapshot snap, FeedVideo video)
    {
        var m = TrailerTitle.Match(Normalize(video.Title));
        if (!m.Success) return null;
        string seriesPart = m.Groups["series"].Value.Trim();
        var series = snap.SeriesById.Values.FirstOrDefault(s => s.KindCode == "TV" && Normalize(s.Title) == seriesPart);
        if (series is null || !int.TryParse(m.Groups["no"].Value, out int no)) return null;
        var raw = TrailerSubtitleRaw.Match(video.Title);
        string rawSub = raw.Success ? raw.Groups["sub"].Value.Trim() : m.Groups["sub"].Value;
        return new Trailer(series.SeriesId, series.Title, no, m.Groups["sub"].Value, rawSub, snap.Find(series.SeriesId, no));
    }

    /// <summary>
    /// 予告を書く。もう入っている・書いた・知らせた（DB に回が無い・サブタイトルが違う）ときは true（それ以上待たなくてよい）。
    /// <paramref name="fillFrom"/> を渡したとき（日曜の見続ける回）は、DB に無い回を予告の題から仮で作り、空のサブタイトルを仮で入れる。
    /// </summary>
    private async Task<bool> ApplyTrailerAsync(Trailer t, FeedVideo video, CancellationToken ct, Snapshot? fillFrom = null)
    {
        string what = $"{t.SeriesTitle} 第{t.EpisodeNo}話の予告";
        if (t.Episode is not { } ep)
        {
            if (fillFrom is not null)
            {
                await CreateFromTrailerAsync(fillFrom, t, video, ct).ConfigureAwait(false);
                return true;
            }
            Notice($"trailer-norow:{t.SeriesId}:{t.EpisodeNo}",
                $"{what}が出ましたが、DB にその回がありません。サブタイトル「{t.Subtitle}」 {video.WatchUrl}");
            return true;
        }
        if (!string.IsNullOrEmpty(ep.YoutubeTrailerUrl)) return true;
        if (string.IsNullOrEmpty(ep.TitleText) && fillFrom is not null)
        {
            await FillTitleFromTrailerAsync(ep, t, ct).ConfigureAwait(false);
        }
        else if (string.IsNullOrEmpty(ep.TitleText))
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

    /// <summary>
    /// DB に無い回を、予告の題から仮で作る（予告の URL も一緒に入れる）。
    /// 話数・通しの番号（全話・放送回・ニチアサ枠）は前の回に 1 を足し、放送日時は前の回のちょうど 1 週間後（休止は考えない）、
    /// 尺は前の回と同じ。サブタイトルは題のままの字で入れ、ルビ付き表記はサブタイトルの空白を改行にしただけのもの、
    /// 文字統計は Episodes の保存と同じ <see cref="TitleCharStatsBuilder"/> で作り、ふりがなは空にする。
    /// episode_id は採番の続きになるので、放送順と ID の順がずれないよう、作る回がいちばん後の放送で、
    /// 前の回が通しの番号でもいちばん後のときだけ作る。作れないときは知らせるだけ。
    /// </summary>
    private async Task CreateFromTrailerAsync(Snapshot snap, Trailer t, FeedVideo video, CancellationToken ct)
    {
        string what = $"{t.SeriesTitle} 第{t.EpisodeNo}話";
        string key = $"trailer-create:{t.SeriesId}:{t.EpisodeNo}";
        var prev = snap.Find(t.SeriesId, t.EpisodeNo - 1);
        if (prev is null || prev.TotalEpNo is null || prev.TotalOaNo is null || prev.NitiasaOaNo is null)
        {
            Notice(key, $"{what}の予告が出ましたが、前の回か前の回の通しの番号が DB に無いので、回を作っていません。サブタイトル「{t.RawSubtitle}」 {video.WatchUrl}");
            return;
        }
        var onAirAt = prev.OnAirAt.AddDays(7);
        if (snap.Episodes.Any(e => e.OnAirAt >= onAirAt
                || e.TotalEpNo > prev.TotalEpNo || e.TotalOaNo > prev.TotalOaNo || e.NitiasaOaNo > prev.NitiasaOaNo))
        {
            Notice(key, $"{what}の予告が出ましたが、DB に {onAirAt:M/d} 以降の回か、前の回より後の通しの番号の回があるので、回を作っていません。サブタイトル「{t.RawSubtitle}」 {video.WatchUrl}");
            return;
        }

        var episode = new Episode
        {
            SeriesId = t.SeriesId,
            SeriesEpNo = t.EpisodeNo,
            TotalEpNo = prev.TotalEpNo + 1,
            TotalOaNo = prev.TotalOaNo + 1,
            NitiasaOaNo = prev.NitiasaOaNo + 1,
            TitleText = t.RawSubtitle,
            TitleRichHtml = RichHtmlOf(t.RawSubtitle),
            TitleKana = null,
            TitleCharStats = TitleCharStatsBuilder.BuildJson(t.RawSubtitle),
            OnAirAt = onAirAt,
            DurationMinutes = prev.DurationMinutes,
            YoutubeTrailerUrl = video.WatchUrl,
            CreatedBy = UpdatedBy,
            UpdatedBy = UpdatedBy
        };
        if (_dryRun)
        {
            _log($"{what}を予告の題から作ります（書かずに試しています）：放送 {onAirAt:yyyy-MM-dd HH:mm}・サブタイトル「{t.RawSubtitle}」・予告 {video.WatchUrl}");
            return;
        }
        int id = await _episodes.InsertAsync(episode, ct).ConfigureAwait(false);
        WrittenCount++;
        _log($"{what}を予告の題から作りました：episode {id}・放送 {onAirAt:yyyy-MM-dd HH:mm}・サブタイトル「{t.RawSubtitle}」・予告 {video.WatchUrl}");
        Notice(key, $"{what}が DB に無かったので、予告の題から仮で作りました（episode {id}・放送 {onAirAt:M/d HH:mm}・サブタイトル「{t.RawSubtitle}」）。ふりがな・ルビ・改行の位置・放送日を確かめてください。");
    }

    /// <summary>
    /// 回はあるのにサブタイトルが空のとき、予告の題のサブタイトルを仮で入れる（ルビ付き表記・文字統計も。ふりがなは触らない）。
    /// </summary>
    private async Task FillTitleFromTrailerAsync(Episode ep, Trailer t, CancellationToken ct)
    {
        string what = $"{t.SeriesTitle} 第{t.EpisodeNo}話のサブタイトル";
        if (_dryRun)
        {
            _log($"{what}を予告の題から入れます（書かずに試しています）：「{t.RawSubtitle}」");
            return;
        }
        if (await _episodes.FillTitleIfEmptyAsync(ep.EpisodeId, t.RawSubtitle, RichHtmlOf(t.RawSubtitle),
                TitleCharStatsBuilder.BuildJson(t.RawSubtitle), UpdatedBy, ct).ConfigureAwait(false))
        {
            WrittenCount++;
            _log($"{what}を予告の題から入れました：episode {ep.EpisodeId}「{t.RawSubtitle}」");
            Notice($"trailer-title:{ep.EpisodeId}", $"{what}が空だったので、予告の題から仮で入れました（episode {ep.EpisodeId}「{t.RawSubtitle}」）。ふりがな・ルビ・改行の位置を確かめてください。");
        }
    }

    /// <summary>ルビの無いルビ付き表記：HTML の特殊文字を逃がし、改行の位置を表す空白を &lt;br&gt; にする。</summary>
    private static string RichHtmlOf(string subtitle)
        => string.Join("<br>", SubtitleBreak.Split(subtitle.Trim()).Select(WebUtility.HtmlEncode));

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
