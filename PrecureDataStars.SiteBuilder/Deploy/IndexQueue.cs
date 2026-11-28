using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// 検索エンジン（Search Console の URL 検査）へ手で送る URL の順番待ちの一覧（<c>index-queue.json</c>）を、本番デプロイのたびに足す。
/// 一覧は SEO チームが上から順に送り、送った日時（<c>sentAt</c>）を書く。置き場は <see cref="BuildConfig.IndexQueueDirectory"/>（git に入らない）。
/// <list type="bullet">
///   <item>載せるもの：このデプロイで新しく出た URL（サイトマップの新規）と、段階の上がった各話のページ。
///   各話のページは新規でも段階で決め、「第N話」だけ（サブタイトルが無いか解禁前）の段階では載せない。</item>
///   <item>各話の段階（<see cref="EpisodeStage"/>）：1＝サブタイトルが入り解禁済み、2＝クレジットが入った（仮のクレジット、または放送前の正式のクレジット）、
///   3＝放送後で正式のクレジットが入った。ビルドのときに DB から求めて出力の隣（<c>….index-stages.json</c>）に控え、
///   本番に上げ終えたときに、前に控えた段階（<c>index-queue-state.json</c>）より上がった回を載せる。初めての回は控えるだけで載せない。</item>
///   <item>同じ URL が送っていないまま一覧にあれば足さない。並びは「コラム・ニュース → シリーズ → エピソード → 人物・団体・キャラ → 役職 → 盤・曲 → そのほか」
///   の段ごとに出た順で、送ったものは後ろ（送った日時の新しい順。90 日を過ぎたものは消す）。news の記事は <c>scripts/index-queue-add.ps1</c> が同じ並びで足す。</item>
///   <item>書くときは鍵のファイル（<c>index-queue.lock</c>）で順番を取り、別の名前に書いてから入れ替える（SEO チームが書いた送った日時を消さない）。</item>
/// </list>
/// </summary>
public static class IndexQueue
{
    /// <summary>一覧から消すまでの、送った後の日数。</summary>
    private const int KeepSentDays = 90;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>各話のページの段階（数が大きいほど中身が多い）。</summary>
    public enum EpisodeStage
    {
        /// <summary>「第N話」だけ（サブタイトルが無いか、解禁前）。</summary>
        NumberOnly = 0,
        /// <summary>サブタイトルが入り、解禁済み。</summary>
        Subtitle = 1,
        /// <summary>クレジットが入った（仮のクレジット、または放送前の正式のクレジット）。</summary>
        ProvisionalCredit = 2,
        /// <summary>放送後で、正式のクレジットが入った。</summary>
        FullCredit = 3
    }

    /// <summary>段階が上がったときの一覧の「理由」。</summary>
    private static string StageReason(int stage) => stage switch
    {
        1 => "サブタイトル",
        2 => "仮クレジット",
        _ => "クレジット"
    };

    /// <summary>出力の隣に置く、このビルドの各話の段階の控えのパス。</summary>
    public static string StagesPathFor(string outputDirectory) => outputDirectory.TrimEnd('/', '\\') + ".index-stages.json";

    /// <summary>
    /// 本番のビルドのときに、各話のページの段階を DB から求めて出力の隣に控える（上げるのはドライランの後のこともあるので、ここでは一覧に足さない）。
    /// </summary>
    public static void SaveEpisodeStages(BuildContext ctx, BuildConfig config)
    {
        var now = DateTimeOffset.Now;
        var stages = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var series in ctx.Series)
        {
            if (!ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var eps)) continue;
            foreach (var ep in eps)
            {
                var revealAt = SubtitleGuardRenderer.RevealAtFor(ep.EpisodeId, ctx.SubtitleRevealAtByEpisodeId);
                bool titled = !string.IsNullOrWhiteSpace(ep.TitleText) && !SubtitleGuardRenderer.IsEmbargoedAt(revealAt, now);
                bool hasCredits = ctx.CreditsByEpisode.TryGetValue(ep.EpisodeId, out var cs) && cs.Count > 0;
                bool hasProvisional = ctx.ProvisionalCreditsByEpisode.TryGetValue(ep.EpisodeId, out var ps) && ps.Count > 0;
                var stage = !titled ? EpisodeStage.NumberOnly
                    : hasCredits && ep.OnAirAt <= now.LocalDateTime ? EpisodeStage.FullCredit
                    : hasCredits || hasProvisional ? EpisodeStage.ProvisionalCredit
                    : EpisodeStage.Subtitle;
                stages[PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo)] = (int)stage;
            }
        }
        File.WriteAllText(StagesPathFor(config.OutputDirectory), JsonSerializer.Serialize(stages, JsonOptions));
    }

    /// <summary>
    /// 本番に上げ終えた後に、一覧へ足す。<paramref name="changed"/> はこのデプロイで更新日が進んだページ（新規はここから取る）。
    /// 置き場が未設定なら何もしない。失敗しても警告にとどめ、デプロイの結果は変えない。
    /// </summary>
    public static void RecordAfterPublish(BuildConfig config, BuildLogger logger, ChangedPages? changed)
        => Record(config.IndexQueueDirectory, config.BaseUrl, StagesPathFor(config.OutputDirectory), logger, changed);

    /// <summary>
    /// <see cref="RecordAfterPublish"/> の芯（置き場・URL の頭・このビルドの段階の控えのパスを直接受け取る。試しの道具からも呼ぶ）。
    /// </summary>
    public static void Record(string queueDirectory, string baseUrl, string stagesPath, BuildLogger logger, ChangedPages? changed)
    {
        if (string.IsNullOrEmpty(queueDirectory) || string.IsNullOrEmpty(baseUrl)) return;
        try
        {
            Directory.CreateDirectory(queueDirectory);
            string foundAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
            var additions = new List<QueueItem>();

            // 新しく出た URL（各話のページは段階で決めるので、ここでは外す）。
            foreach (var path in changed?.Added ?? new List<string>())
            {
                var (_, kind) = Classify(path);
                if (kind == "エピソード") continue;
                additions.Add(new QueueItem { Url = baseUrl + path, Kind = kind, Reason = "新規", FoundAt = foundAt });
            }

            // 段階の上がった各話のページ。
            int episodeCount = 0;
            string statePath = Path.Combine(queueDirectory, "index-queue-state.json");
            if (File.Exists(stagesPath))
            {
                var current = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(stagesPath)) ?? new();
                var state = File.Exists(statePath)
                    ? JsonSerializer.Deserialize<StateFile>(File.ReadAllText(statePath)) ?? new StateFile()
                    : null;
                if (state is not null)
                {
                    foreach (var (path, stage) in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        int before = state.EpisodeStages.TryGetValue(path, out var b) ? b : 0;
                        if (stage <= before || stage < (int)EpisodeStage.Subtitle) continue;
                        additions.Add(new QueueItem { Url = baseUrl + path, Kind = "エピソード", Reason = StageReason(stage), FoundAt = foundAt });
                        episodeCount++;
                    }
                }
                // 段階は上がった分だけ控えを進める（下がった回は前の段階のまま。戻ってまた上がっても載せ直さない）。
                var next = state ?? new StateFile();
                foreach (var (path, stage) in current)
                    if (!next.EpisodeStages.TryGetValue(path, out var b) || stage > b) next.EpisodeStages[path] = stage;
                WriteAtomic(statePath, JsonSerializer.Serialize(next, JsonOptions));
                if (state is null) logger.Info($"Index queue      : 各話の段階を初めて控えた（{current.Count} 話。一覧には載せない）");
            }

            int added = WithLock(queueDirectory, () => AddItems(Path.Combine(queueDirectory, "index-queue.json"), additions));
            logger.Info($"Index queue      : {added} 件を足した（新規 {additions.Count - episodeCount}・各話 {episodeCount} のうち、送っていない同じ URL を除く）");
        }
        catch (Exception ex)
        {
            logger.Warn($"Index queue      : 足せませんでした（{ex.GetType().Name}: {ex.Message}）");
        }
    }

    /// <summary>一覧に項目を足して並べ直し、書き直す。足した件数を返す。</summary>
    private static int AddItems(string queuePath, IReadOnlyList<QueueItem> additions)
    {
        var file = File.Exists(queuePath)
            ? JsonSerializer.Deserialize<QueueFile>(File.ReadAllText(queuePath)) ?? new QueueFile()
            : new QueueFile();
        var unsent = new HashSet<string>(file.Items.Where(i => string.IsNullOrEmpty(i.SentAt)).Select(i => i.Url), StringComparer.Ordinal);
        int added = 0;
        foreach (var item in additions)
        {
            if (!unsent.Add(item.Url)) continue;
            file.Items.Add(item);
            added++;
        }
        var keepFrom = DateTimeOffset.Now.AddDays(-KeepSentDays);
        file.Items = file.Items
            .Where(i => string.IsNullOrEmpty(i.SentAt) || !DateTimeOffset.TryParse(i.SentAt, out var sent) || sent >= keepFrom)
            .OrderBy(i => string.IsNullOrEmpty(i.SentAt) ? 0 : 1)
            .ThenBy(i => string.IsNullOrEmpty(i.SentAt) ? Classify(PathOf(i.Url)).Tier : 0)
            .ThenBy(i => string.IsNullOrEmpty(i.SentAt) ? i.FoundAt : "", StringComparer.Ordinal)
            .ThenByDescending(i => i.SentAt ?? "", StringComparer.Ordinal)
            .ThenBy(i => i.Url, StringComparer.Ordinal)
            .ToList();
        if (added > 0 || !File.Exists(queuePath)) WriteAtomic(queuePath, JsonSerializer.Serialize(file, JsonOptions));
        return added;
    }

    /// <summary>
    /// URL のパスから、並びの段と種類を決める（<c>scripts/index-queue-add.ps1</c> と同じ表）。
    /// 0＝コラム・ニュース、1＝シリーズ、2＝エピソード、3＝人物・団体・キャラ、4＝役職と /creators/ の下の一覧、5＝盤・曲、6＝そのほか。
    /// </summary>
    public static (int Tier, string Kind) Classify(string path)
    {
        if (path.StartsWith("/articles/", StringComparison.Ordinal)) return (0, "コラム");
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^/series/[^/]+/\d+/$")) return (2, "エピソード");
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^/series/[^/]+/$")) return (1, "シリーズ");
        if (path.StartsWith("/people/", StringComparison.Ordinal)) return (3, "人物");
        if (path.StartsWith("/companies/", StringComparison.Ordinal)) return (3, "団体");
        if (path.StartsWith("/units/", StringComparison.Ordinal)) return (3, "ユニット");
        if (path.StartsWith("/characters/", StringComparison.Ordinal)) return (3, "キャラ");
        if (path.StartsWith("/creators/roles/", StringComparison.Ordinal)) return (4, "役職");
        // スタッフ一覧・声優一覧など、/creators/ の下のほかの一覧のページも役職と同じ段に置く。
        if (path.StartsWith("/creators/", StringComparison.Ordinal)) return (4, "一覧");
        if (path.StartsWith("/products/", StringComparison.Ordinal)) return (5, "盤");
        if (path.StartsWith("/songs/", StringComparison.Ordinal)) return (5, "曲");
        return (6, "そのほか");
    }

    /// <summary>絶対 URL からパスを取り出す（news の記事など、ほかのサイトの URL はコラム・ニュースの段）。</summary>
    private static string PathOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        return u.Host.EndsWith("precure.news", StringComparison.OrdinalIgnoreCase) ? "/articles/" : u.AbsolutePath;
    }

    /// <summary>鍵のファイルで順番を取ってから処理する（SEO チームの書き込みや news の道具と重ならないように。最大 30 秒待つ）。</summary>
    private static T WithLock<T>(string directory, Func<T> action)
    {
        string lockPath = Path.Combine(directory, "index-queue.lock");
        for (int i = 0; ; i++)
        {
            try
            {
                using var _ = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                return action();
            }
            catch (IOException) when (i < 60)
            {
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>別の名前に書いてから入れ替える（書きかけのファイルを残さない）。</summary>
    private static void WriteAtomic(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>一覧のファイル。</summary>
    private sealed class QueueFile
    {
        [JsonPropertyName("items")] public List<QueueItem> Items { get; set; } = new();
    }

    /// <summary>一覧の 1 件。</summary>
    private sealed class QueueItem
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("foundAt")] public string FoundAt { get; set; } = "";
        [JsonPropertyName("sentAt")] public string? SentAt { get; set; }
    }

    /// <summary>各話の段階の控え。</summary>
    private sealed class StateFile
    {
        [JsonPropertyName("episodeStages")] public Dictionary<string, int> EpisodeStages { get; set; } = new(StringComparer.Ordinal);
    }
}
