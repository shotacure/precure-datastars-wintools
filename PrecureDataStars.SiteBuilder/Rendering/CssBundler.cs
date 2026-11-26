using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 全ページ共通の <c>wwwroot/assets/site.css</c> から、区画（<see cref="CssBundlePlan"/>）ごとに「その区画のページで当たりうる規則だけ」を
/// 抜き出した CSS（<c>/assets/css/{区画}.css</c>）を作る。書くのは site.css 1 本のままで、配るファイルはビルドのたびにここで作る。
/// <para>
/// 流れ：
/// <list type="number">
///   <item><description>ビルドの始め（<see cref="Create"/>）：site.css を規則に切り分け、切り分けた規則を元の順につなぐとコメントを除いた
///     site.css とバイト単位で一致するか（往復）を確かめる。セレクタに出る class・id・タグ名・属性名（語彙）と、版の印（<see cref="Version"/>）を求める。</description></item>
///   <item><description>ページを描くたび（<see cref="Collect"/>。並列）：描き上がった HTML（レイアウト込み。ページの中の script も含む）に出る語のうち、
///     語彙に当たる語と「<c>-</c> で終わる語」を、その区画の集合に足す。</description></item>
///   <item><description>全ページの後（<see cref="Finish"/>）：区画ごとに、要る語がそろうセレクタを持つ規則だけを、元の順のまま書き出す。</description></item>
/// </list>
/// </para>
/// <para>
/// 規則を残す条件：セレクタの並びのどれか 1 つで、要る語（<c>.class</c>・<c>#id</c>・<c>[属性名</c>・各複合セレクタの頭のタグ名）が全部その区画の集合にあること。
/// 疑似クラス・疑似要素と、<c>:not()</c>・<c>:is()</c>・<c>:has()</c> などの引数は要る語に数えない（数えなければ残す側に倒れる）。
/// 規則の順番は元のままなので、残った規則どうしの効き方（詳細度と後勝ち）は site.css と同じになる。落ちるのは、どのページでも当たりえない規則だけ。
/// </para>
/// <para>
/// 区画の集合には、そのページが読む JS（<c>wwwroot/assets/*.js</c> のうち HTML に <c>/assets/○○.js</c> と出てくるもの）の語と、
/// <see cref="CssBundlePlan.AlwaysPresentWords"/> も足す。
/// HTML か JS に <c>-</c> で終わる語（<c>'count-pill-'</c>・<c>'data-hide-'</c> のように、JS がつなげて class・属性名を作る頭）があれば、
/// その語で始まる語彙の class・id・属性名は全部あるとみなす。
/// </para>
/// <para>
/// 版の印は site.css・JS の語・区画の表・<see cref="AlgorithmVersion"/> から作るので、ビルドの前から決まり、ページは最初から正しい URL で書ける。
/// データやテンプレートの変化で区画の中身が変わっても印は変わらないので、同じ印のあいだは区画の語の集合を前回の控えとの和にして減らさない
/// （古い区画の CSS を持つ端末でも、それまでにあった部品の見た目は崩れない。配るときの Cache-Control は短めにする）。
/// </para>
/// <para>
/// 控え（<see cref="ManifestPathFor"/>。出力の隣）には、版の印・区画ごとの語の集合と大きさ・テンプレートごとの見本の URL を残す。
/// 確かめの道具（<c>scripts/verify-css-bundles.mjs</c>）も控えを読む。
/// </para>
/// </summary>
public sealed class CssBundler
{
    /// <summary>
    /// 振り分けの処理の版。規則の切り分け方・残す条件・語の取り方など、ここの C# の処理を変えたら 1 つ上げる
    /// （版の印が変わり、区画の語の集合を前回の分から引き継がずに作り直す）。区画の表と常にあるとみなす語は、表そのものを印に混ぜるので上げなくてよい。
    /// </summary>
    public const int AlgorithmVersion = 3;

    /// <summary>区画の CSS を置く、出力ルートからの相対ディレクトリ。</summary>
    public const string BundleDirectory = "assets/css";

    /// <summary>全部入りの CSS の URL のパス。</summary>
    private const string FullCssPath = "/assets/site.css";

    /// <summary>知らせ（定時デプロイのトーストに回す行）の頭。deploy.ps1 がこの頭の行を拾う。</summary>
    public const string NoticePrefix = "CSS 区画の知らせ：";

    /// <summary>区画ごとの大きさの行の頭。deploy.ps1 がこの頭の行を deploy.log に写す。</summary>
    public const string SizePrefix = "CSS 区画：";

    // ──────────────── 語の切り出し ────────────────

    /// <summary>語の頭になれる字（英字と <c>_</c>）。</summary>
    private static readonly SearchValues<char> WordStart =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_");

    /// <summary>語の続きになれる字（英数字・<c>_</c>・<c>-</c>）。</summary>
    private static readonly SearchValues<char> WordChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    /// <summary>相対の url()（<c>/</c>・<c>data:</c>・<c>http(s):</c> で始まらないもの）。区画の CSS は置き場が違うので、相対の url() は切れる。</summary>
    private static readonly Regex RelativeUrlRegex = new(
        @"url\(\s*(['""]?)(?!\s*(?:/|data:|https?:|#))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ──────────────── 状態 ────────────────

    private readonly BuildConfig _config;
    private readonly BuildLogger _logger;

    /// <summary>規則の木（site.css からコメントを除いたものを切り分けたもの）。振り分けを使わないときは空。</summary>
    private readonly IReadOnlyList<CssNode> _nodes;

    /// <summary>語彙のうち、class と id の名前（書かれたままの字）。</summary>
    private readonly HashSet<string> _vocabExact;

    /// <summary>語彙のうち、タグ名と属性名（小文字）。</summary>
    private readonly HashSet<string> _vocabLower;

    /// <summary>
    /// JS のファイル名（<c>search.js</c> の形）→ その JS から取った語（語彙に当たる語と「-」で終わる語）。
    /// ページが読む JS（HTML の <c>/assets/○○.js</c>）の語を、そのページの区画に足す。ページの DOM を書き換えられるのは、そのページが読む JS だけなので。
    /// </summary>
    private readonly IReadOnlyDictionary<string, HashSet<string>> _scriptWords;

    /// <summary>ページの HTML の中の、自前の JS の読み込み（<c>/assets/○○.js</c>）。</summary>
    private static readonly Regex ScriptSrcRegex = new(@"/assets/([A-Za-z0-9_.-]+\.js)\b", RegexOptions.Compiled);

    /// <summary>前回の控え。無い・読めないときは null。</summary>
    private readonly CssBundleManifest? _previous;

    /// <summary>スレッドごとの、区画 → 集めた語。並列の描画から書き、<see cref="Finish"/> で合わせる。</summary>
    private readonly ThreadLocal<Dictionary<string, HashSet<string>>> _collected =
        new(() => new Dictionary<string, HashSet<string>>(StringComparer.Ordinal), trackAllValues: true);

    /// <summary>テンプレートごとの見本の URL（最初に描いた 3 本まで）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> _samples = new(StringComparer.Ordinal);

    /// <summary>表に無いテンプレートで、すでに知らせたもの（1 テンプレートにつき 1 回だけ知らせる）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unknownReported = new(StringComparer.Ordinal);

    /// <summary>語を集めるのにかかった時間（全スレッドの合計。Stopwatch の刻み）。ビルドの延びを見るためにログへ出す。</summary>
    private long _collectTicks;

    /// <summary>語を集めたページの数。</summary>
    private int _collectedPages;

    /// <summary>区画の CSS を配るか。false のときは、全ページが全部入りの site.css を読む。</summary>
    public bool Enabled { get; }

    /// <summary>版の印（SHA-256 の先頭 10 桁）。区画の CSS の URL の <c>?v=</c> に付ける。振り分けを使わないときは空。</summary>
    public string Version { get; } = "";

    private CssBundler(BuildConfig config, BuildLogger logger, bool enabled, IReadOnlyList<CssNode> nodes,
        HashSet<string> vocabExact, HashSet<string> vocabLower, IReadOnlyDictionary<string, HashSet<string>> scriptWords, string version, CssBundleManifest? previous)
    {
        _config = config;
        _logger = logger;
        Enabled = enabled;
        _nodes = nodes;
        _vocabExact = vocabExact;
        _vocabLower = vocabLower;
        _scriptWords = scriptWords;
        Version = version;
        _previous = previous;
    }

    /// <summary>出力ディレクトリに対応する控えのパス（出力の兄弟。台帳と同じ置き方。出力の中には置かない）。</summary>
    public static string ManifestPathFor(string outputDirectory)
        => Path.GetFullPath(outputDirectory).TrimEnd('/', '\\') + ".css-bundles.json";

    /// <summary>
    /// ビルドの始めに作る。site.css を切り分けて点検し、語彙と版の印を求め、前回の控えを読む。DB を読む前に呼ぶ（点検で止まるなら早いほうがよい）。
    /// 振り分けを使わないとき（App.config の <c>CssBundles=false</c>、点検に当たった、一部だけのテストビルドで控えが使えない）は、
    /// <see cref="Enabled"/> が false の bundler を返し、全ページが全部入りの site.css を読む。
    /// </summary>
    /// <exception cref="InvalidOperationException">本番の一部だけのビルド（<c>--production --page</c>）で、前回の控えが使えないとき。</exception>
    public static CssBundler Create(BuildConfig config, BuildLogger logger)
    {
        var empty = new HashSet<string>(StringComparer.Ordinal);
        CssBundler Disabled() => new(config, logger, enabled: false, Array.Empty<CssNode>(), empty, empty,
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal), "", null);

        if (!config.CssBundles)
        {
            logger.Info("CSS 区画          : 使わない（App.config の CssBundles=false。全ページが site.css を読む）");
            return Disabled();
        }

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets");
        var cssPath = Path.Combine(assetsDir, "site.css");
        if (!File.Exists(cssPath))
        {
            logger.Warn($"site.css が見つからないので、CSS の区画を作りません: {cssPath}");
            return Disabled();
        }

        var cssBytes = File.ReadAllBytes(cssPath);
        var css = Encoding.UTF8.GetString(cssBytes);

        // 点検（切り分けの往復・相対の url()）。当たったら区画を使わず、全部入りの site.css で公開を続ける。
        // テストビルドでは警告（作業中に気づくため）、本番では知らせ（定時デプロイの公開を止めないため。トーストに回る）。
        string stripped;
        IReadOnlyList<CssNode> nodes;
        try
        {
            stripped = StripComments(css);
            int pos = 0;
            nodes = ParseList(stripped, ref pos, nested: false);
            var roundTrip = Emit(nodes, _ => true, keepAll: true);
            if (!string.Equals(roundTrip, stripped, StringComparison.Ordinal))
                throw new FormatException("切り分けた規則をつないでも、コメントを除いた site.css と一致しません");
            if (RelativeUrlRegex.IsMatch(stripped))
                throw new FormatException("site.css に相対の url() があります（区画の CSS は /assets/css/ に置くので、相対の url() は切れます。/ から始まる形にしてください）");
        }
        catch (FormatException ex)
        {
            var message = $"site.css の点検に当たったので、全ページを全部入りの site.css にしました：{ex.Message}";
            if (config.IsProductionMode) logger.Info(NoticePrefix + message);
            else logger.Warn(message);
            return Disabled();
        }

        // 語彙：セレクタに出る class・id（書かれたまま）と、タグ名・属性名（小文字）。
        var vocabExact = new HashSet<string>(StringComparer.Ordinal);
        var vocabLower = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in EnumerateRules(nodes))
        foreach (var selector in rule.Selectors)
        foreach (var req in selector)
        {
            if (req.Kind is RequirementKind.Class or RequirementKind.Id) vocabExact.Add(req.Name);
            else vocabLower.Add(req.Name);
        }
        foreach (var word in CssBundlePlan.AlwaysPresentWords) vocabLower.Add(word);

        // JS ごとの語。ページが読む JS の語を、そのページの区画に足す。版の印には、JS 本体ではなく全 JS の語の集合を混ぜる
        // （JS のコメントや変数名だけを直しても、全 HTML の URL が変わらないように）。
        var scriptWords = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allScriptWords = new HashSet<string>(StringComparer.Ordinal);
        var collector = new WordCollector(vocabExact, vocabLower);
        foreach (var js in Directory.GetFiles(assetsDir, "*.js", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            collector.AddWords(File.ReadAllText(js), set);
            scriptWords[Path.GetFileName(js)] = set;
            allScriptWords.UnionWith(set);
        }

        var version = ComputeVersion(cssBytes, allScriptWords);
        var previous = CssBundleManifest.Load(ManifestPathFor(config.OutputDirectory));

        // 一部だけのビルドは、前回の控えの語の集合に今回の分を足して区画を作る。控えが無い・読めない・印が違うときは、
        // 数ページ分の語だけで区画を作ると、ほかのページの見た目が欠ける。テストの出力では全部入りの site.css で描き（崩れない）、
        // 本番の出力では止める（全部入りや欠けた区画を本番に上げないため。全体のビルドを流す）。
        bool partial = !string.IsNullOrEmpty(config.PageFilter);
        if (partial && (previous is null || !string.Equals(previous.Version, version, StringComparison.Ordinal)))
        {
            var why = previous is null ? "前回の控えがありません" : "site.css か JS が前回の全体ビルドから変わっています";
            if (config.IsProductionMode)
                throw new InvalidOperationException(
                    $"一部だけの本番ビルド（--page）で CSS の区画を作れません（{why}）。全体のビルドを流してください。");
            logger.Info($"CSS 区画          : 一部だけのビルドで、{why}。今回は全ページを site.css で描きます（区画の振り分けは全体のビルドで確かめる）");
            return Disabled();
        }

        var bundler = new CssBundler(config, logger, enabled: true, nodes, vocabExact, vocabLower, scriptWords, version, previous);
        bundler.ReportUnknownTemplates();
        logger.Info($"CSS 区画          : 版 {version}（規則 {EnumerateRules(nodes).Count()} 本・語彙 {vocabExact.Count + vocabLower.Count} 語）");
        return bundler;
    }

    /// <summary>
    /// 版の印を求める：site.css・JS の語・区画の表と常にあるとみなす語・<see cref="AlgorithmVersion"/> の SHA-256 の先頭 10 桁。
    /// </summary>
    private static string ComputeVersion(byte[] cssBytes, IReadOnlySet<string> scriptWords)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(cssBytes);
        hash.AppendData(Encoding.UTF8.GetBytes("\n--js--\n" + string.Join("\n", scriptWords.OrderBy(w => w, StringComparer.Ordinal))));
        hash.AppendData(Encoding.UTF8.GetBytes("\n--plan--\n" + CssBundlePlan.Describe()));
        hash.AppendData(Encoding.UTF8.GetBytes($"\n--algorithm--\n{AlgorithmVersion}"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..10];
    }

    /// <summary><c>Templates/*.sbn</c>（<c>_</c> で始まる部品を除く）のうち、区画の表に無いものを知らせる。</summary>
    private void ReportUnknownTemplates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Templates");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.sbn").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('_')) continue;
            if (CssBundlePlan.GroupOf(name) is null) ReportUnknown(name);
        }
    }

    /// <summary>
    /// 表に無いテンプレートを 1 回だけ知らせる。そのページは全部入りの site.css を読むので見た目は崩れない。
    /// テストビルドでは警告（作業中に気づくため）、本番では Info（定時デプロイの安全ゲートで公開を止めないため）。
    /// </summary>
    private void ReportUnknown(string templateName)
    {
        if (!_unknownReported.TryAdd(templateName, 0)) return;
        var message = $"CSS の区画の表（CssBundlePlan）に無いテンプレートです。このページは site.css を読みます: {templateName}";
        if (_config.IsProductionMode) _logger.Info(message);
        else _logger.Warn(message);
    }

    /// <summary>
    /// テンプレートのページが読む CSS の URL。区画の CSS（<c>/assets/css/{区画}.css?v=印</c>）か、振り分けを使わないとき・表に無いテンプレートのときは全部入りの site.css。
    /// </summary>
    public string CssUrlFor(string templateName)
    {
        if (!Enabled) return AssetUrl.Versioned(FullCssPath);
        var group = CssBundlePlan.GroupOf(templateName);
        if (group is null)
        {
            ReportUnknown(templateName);
            return AssetUrl.Versioned(FullCssPath);
        }
        return $"/{BundleDirectory}/{group}.css?v={Version}";
    }

    /// <summary>
    /// 描き上がったページの HTML（レイアウト込み）の語を、そのテンプレートの区画の集合に足す。並列の描画から呼んでよい。
    /// </summary>
    public void Collect(string templateName, string urlPath, string pageHtml)
    {
        if (!Enabled) return;
        var group = CssBundlePlan.GroupOf(templateName);
        if (group is null) return;

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var local = _collected.Value!;
        if (!local.TryGetValue(group, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            local[group] = set;
        }
        new WordCollector(_vocabExact, _vocabLower).AddWords(pageHtml, set);
        // ページが読む JS の語も足す（JS があとから付ける class・属性名・要素）。
        for (var m = ScriptSrcRegex.Match(pageHtml); m.Success; m = m.NextMatch())
        {
            if (_scriptWords.TryGetValue(m.Groups[1].Value, out var scriptSet)) set.UnionWith(scriptSet);
        }

        var key = templateName.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase) ? templateName[..^4] : templateName;
        var samples = _samples.GetOrAdd(key, _ => new List<string>(3));
        lock (samples)
        {
            if (samples.Count < 3) samples.Add(urlPath);
        }
        Interlocked.Add(ref _collectTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        Interlocked.Increment(ref _collectedPages);
    }

    /// <summary>
    /// 全ページの後に呼ぶ。区画ごとに規則を選んで <c>/assets/css/{区画}.css</c> を書き、控えを書き、大きさをログに出す。
    /// 出力の掃除（PruneOrphans）より前に呼ぶ（書いた区画のファイルが今回の出力として記録され、消されないように）。
    /// </summary>
    public void Finish(OutputWriter output)
    {
        if (!Enabled) return;
        long finishStarted = System.Diagnostics.Stopwatch.GetTimestamp();

        // 今回集めた語を区画ごとに合わせる。
        var words = CssBundlePlan.Groups.ToDictionary(g => g, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var local in _collected.Values)
        foreach (var (group, set) in local)
            words[group].UnionWith(set);

        // 同じ版の印のあいだは減らさない（前回の控えとの和）。
        bool sameVersion = _previous is not null && string.Equals(_previous.Version, Version, StringComparison.Ordinal);
        if (sameVersion)
        {
            foreach (var (group, previousWords) in _previous!.Words)
            {
                if (words.TryGetValue(group, out var set)) set.UnionWith(previousWords);
            }
        }

        var manifest = new CssBundleManifest { Version = Version };
        var notices = new List<string>();
        foreach (var group in CssBundlePlan.Groups)
        {
            var groupWords = words[group];
            manifest.Words[group] = groupWords.OrderBy(w => w, StringComparer.Ordinal).ToList();

            var present = BuildPresence(groupWords);
            var text = Emit(_nodes, rule => rule.Selectors.Any(sel => sel.All(present.Contains)));
            var bytes = Encoding.UTF8.GetBytes(text);
            output.WriteText(Path.Combine(output.Root, BundleDirectory.Replace('/', Path.DirectorySeparatorChar), group + ".css"), text);

            int gzip = GzipLength(bytes);
            manifest.Sizes[group] = new CssBundleSize(bytes.Length, gzip);
            var prefixCount = groupWords.Count(w => w.EndsWith('-'));

            string delta = "";
            if (_previous is not null && _previous.Sizes.TryGetValue(group, out var before) && before.Raw > 0)
            {
                delta = $"（前回 {before.Raw / 1024.0:F1}KB から {(bytes.Length - before.Raw) / 1024.0:+0.0;-0.0;0}KB）";
                // 同じ印なのに小さくなった（減らさない決まりなので起きないはず）、印が変わって 3 割以上小さくなった、は知らせる。
                if (sameVersion && bytes.Length < before.Raw)
                    notices.Add($"{group}.css が、版が同じなのに小さくなりました（{before.Raw}→{bytes.Length} バイト）");
                else if (!sameVersion && bytes.Length < before.Raw * 0.7)
                    notices.Add($"{group}.css が前回より 3 割以上小さくなりました（{before.Raw}→{bytes.Length} バイト）。見た目が欠けていないか確かめてください");
            }
            _logger.Info($"{SizePrefix}{group}.css {bytes.Length / 1024.0:F1}KB（gzip {gzip / 1024.0:F1}KB）{delta}・頭の一致の語 {prefixCount}");
        }

        foreach (var (template, urls) in _samples.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            lock (urls) manifest.Samples[template] = urls.ToList();
        }
        // 一部だけのビルドでは、今回描かなかったテンプレートの見本を前回の控えから引き継ぐ。
        if (_previous is not null)
        {
            foreach (var (template, urls) in _previous.Samples)
                manifest.Samples.TryAdd(template, urls);
        }

        foreach (var notice in notices) _logger.Info(NoticePrefix + notice);
        if (!output.DiffOnly) manifest.Save(ManifestPathFor(_config.OutputDirectory));

        var collectSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(0, _collectTicks).TotalSeconds;
        var finishSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(finishStarted).TotalSeconds;
        _logger.Info($"CSS 区画          : 語を集めた {_collectedPages} ページ・{collectSeconds:F1} 秒（全スレッドの合計）／区画を作った {finishSeconds:F1} 秒"
                     + (sameVersion ? "（同じ版：前回の語に足した）" : "（版が変わった：今回の語で作り直した）"));
    }

    /// <summary>区画の語の集合から、語彙のうちその区画で「ある」とみなす語（要る語の照合に使う形）を作る。</summary>
    private HashSet<Requirement> BuildPresence(HashSet<string> groupWords)
    {
        // 書かれたままの集合と、小文字にした写し。「-」で終わる語は頭の一致に使う。
        var exact = new HashSet<string>(groupWords, StringComparer.Ordinal);
        var lower = new HashSet<string>(exact.Select(w => w.ToLowerInvariant()), StringComparer.Ordinal);
        lower.UnionWith(CssBundlePlan.AlwaysPresentWords);
        var prefixes = exact.Where(w => w.Length >= 2 && w.EndsWith('-')).ToList();
        var prefixesLower = prefixes.Select(p => p.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();

        var present = new HashSet<Requirement>();
        foreach (var name in _vocabExact)
        {
            if (exact.Contains(name) || prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                present.Add(new Requirement(RequirementKind.Class, name));
                present.Add(new Requirement(RequirementKind.Id, name));
            }
        }
        foreach (var name in _vocabLower)
        {
            if (lower.Contains(name))
            {
                present.Add(new Requirement(RequirementKind.Tag, name));
                present.Add(new Requirement(RequirementKind.Attribute, name));
            }
            else if (prefixesLower.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                // 頭の一致は、つなげて作る class・id・属性名に当てる（タグ名はつなげて作らない）。
                present.Add(new Requirement(RequirementKind.Attribute, name));
            }
        }
        return present;
    }

    private static int GzipLength(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) gz.Write(bytes);
        return (int)ms.Length;
    }

    // ──────────────── 語を集める ────────────────

    /// <summary>
    /// HTML・JS の文字から語を切り出し、語彙に当たる語と「-」で終わる語だけを集合に足す。語彙に当たる語は語彙の文字列をそのまま足すので、
    /// 語ごとに文字列を作らない（「-」で終わる語だけ作る）。
    /// </summary>
    private readonly struct WordCollector
    {
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _exact;
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lower;

        public WordCollector(HashSet<string> vocabExact, HashSet<string> vocabLower)
        {
            _exact = vocabExact.GetAlternateLookup<ReadOnlySpan<char>>();
            _lower = vocabLower.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public void AddWords(string text, HashSet<string> into)
        {
            var span = text.AsSpan();
            Span<char> lowered = stackalloc char[128];
            int i = 0;
            while (i < span.Length)
            {
                int start = span[i..].IndexOfAny(WordStart);
                if (start < 0) break;
                start += i;
                int length = span[start..].IndexOfAnyExcept(WordChars);
                int end = length < 0 ? span.Length : start + length;
                var word = span[start..end];
                i = end;

                if (_exact.TryGetValue(word, out var known))
                {
                    into.Add(known);
                }
                else if (word.Length <= lowered.Length)
                {
                    var lw = lowered[..word.Length];
                    word.ToLowerInvariant(lw);
                    if (_lower.TryGetValue(lw, out var knownLower)) into.Add(knownLower);
                }
                if (word.Length >= 2 && word[^1] == '-') into.Add(word.ToString());
            }
        }
    }

    // ──────────────── CSS の切り分け ────────────────

    /// <summary>規則の木の 1 つ。<see cref="CssText"/>（地の文・文・丸ごと残す at 規則）・<see cref="CssRule"/>・<see cref="CssGroup"/>。</summary>
    private abstract class CssNode { }

    /// <summary>そのまま残す文字（規則のあいだの空白、<c>@charset</c> のような文、<c>@keyframes</c>・<c>@font-face</c> などの at 規則）。</summary>
    private sealed class CssText : CssNode
    {
        public CssText(string text) => Text = text;
        public string Text { get; }
    }

    /// <summary>規則 1 つ（前の空白からセレクタ・宣言の閉じかっこまで）と、セレクタの並びごとの要る語。</summary>
    private sealed class CssRule : CssNode
    {
        public CssRule(string text, IReadOnlyList<Requirement[]> selectors) { Text = text; Selectors = selectors; }
        public string Text { get; }
        public IReadOnlyList<Requirement[]> Selectors { get; }
    }

    /// <summary>中をたどる at 規則（<c>@media</c>・<c>@supports</c> など）。見出し（前の空白から <c>{</c> まで）・中身・閉じかっこ。</summary>
    private sealed class CssGroup : CssNode
    {
        public CssGroup(string header, IReadOnlyList<CssNode> children) { Header = header; Children = children; }
        public string Header { get; }
        public IReadOnlyList<CssNode> Children { get; }
    }

    private enum RequirementKind { Class, Id, Tag, Attribute }

    /// <summary>セレクタが当たるのに要る語 1 つ。class・id は書かれたままの字、タグ名・属性名は小文字。</summary>
    private readonly record struct Requirement(RequirementKind Kind, string Name);

    private static IEnumerable<CssRule> EnumerateRules(IReadOnlyList<CssNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is CssRule rule) yield return rule;
            else if (node is CssGroup group)
                foreach (var inner in EnumerateRules(group.Children)) yield return inner;
        }
    }

    /// <summary>
    /// 規則の木を文字に戻す。<paramref name="keep"/> が false の規則は書かない。中身の規則が 1 つも残らない <c>@media</c> などは、見出しごと書かない。
    /// 全部残せば、コメントを除いた site.css と同じ文字になる（往復の点検）。
    /// </summary>
    private static string Emit(IReadOnlyList<CssNode> nodes, Func<CssRule, bool> keep, bool keepAll = false)
    {
        var sb = new StringBuilder();
        EmitInto(sb, nodes, keep, keepAll);
        return sb.ToString();
    }

    /// <summary>
    /// 規則の木を <paramref name="sb"/> へ書く。戻り値は、規則か残す at 規則を 1 つでも書いたか。
    /// <paramref name="keepAll"/> が true（往復の点検）のときは、中身の規則が元から無い @media も書く（元の文字と一致させるため）。
    /// </summary>
    private static bool EmitInto(StringBuilder sb, IReadOnlyList<CssNode> nodes, Func<CssRule, bool> keep, bool keepAll)
    {
        bool wroteContent = false;
        foreach (var node in nodes)
        {
            switch (node)
            {
                case CssText text:
                    sb.Append(text.Text);
                    if (!string.IsNullOrWhiteSpace(text.Text)) wroteContent = true;
                    break;
                case CssRule rule:
                    if (keep(rule))
                    {
                        sb.Append(rule.Text);
                        wroteContent = true;
                    }
                    break;
                case CssGroup group:
                    var inner = new StringBuilder();
                    bool innerContent = EmitInto(inner, group.Children, keep, keepAll);
                    // 中身が空になった @media は書かない。全部残すとき（往復の点検）は、元から空のものもそのまま書く。
                    if (innerContent || keepAll)
                    {
                        sb.Append(group.Header).Append(inner).Append('}');
                        wroteContent |= innerContent;
                    }
                    break;
            }
        }
        return wroteContent;
    }

    /// <summary>
    /// コメント（<c>/* … */</c>）を除く。引用符の中の <c>/*</c> はコメントにしない。site.css のコメントには波かっこや引用符を含むものがあるので、
    /// 規則を切り分ける前に除く。
    /// </summary>
    private static string StripComments(string css)
    {
        var sb = new StringBuilder(css.Length);
        int i = 0;
        while (i < css.Length)
        {
            char c = css[i];
            if (c is '"' or '\'')
            {
                int end = SkipString(css, i);
                sb.Append(css, i, end - i);
                i = end;
            }
            else if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                int close = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0) throw new FormatException("閉じていないコメントがあります");
                i = close + 2;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary><paramref name="start"/> の引用符で始まる文字列の、閉じ引用符の次の位置（\ のエスケープを飛ばす）。</summary>
    private static int SkipString(string s, int start)
    {
        char quote = s[start];
        int i = start + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == quote) return i + 1;
            if (s[i] == '\n') throw new FormatException("閉じていない文字列があります");
            i++;
        }
        throw new FormatException("閉じていない文字列があります");
    }

    /// <summary>
    /// <paramref name="i"/> から、括弧（<c>( )</c>・<c>[ ]</c>）と文字列の外にある <paramref name="stops"/> のどれかの字の位置を探す。無ければ -1。
    /// </summary>
    private static int ScanTo(string s, int i, string stops)
    {
        int depth = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c is '"' or '\'') { i = SkipString(s, i); continue; }
            // 探す字は、括弧の数を数えるより先に見る（属性セレクタの閉じかっこ「]」を探すときに、括弧として数えて読み過ごさないため）。
            if (depth == 0 && stops.IndexOf(c) >= 0) return i;
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') { if (depth > 0) depth--; }
            i++;
        }
        return -1;
    }

    /// <summary><paramref name="open"/> の <c>{</c> に対応する <c>}</c> の位置（文字列と入れ子の波かっこを飛ばす）。</summary>
    private static int FindBlockEnd(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '"' or '\'') { i = SkipString(s, i) - 1; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        throw new FormatException("閉じていない波かっこがあります");
    }

    /// <summary>中をたどる at 規則か（<c>@media</c>・<c>@supports</c>・<c>@container</c>・<c>@layer</c> のかたまり・<c>@document</c>）。</summary>
    private static bool IsGroupAtRule(string prelude)
        => Regex.IsMatch(prelude, @"^@(media|supports|container|layer|document)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// 規則の並びを読む。<paramref name="nested"/> が true のときは <c>}</c> で終わる（@media などの中身）。
    /// 各規則は、前の空白から閉じかっこまでをそのまま持つので、全部つなぐと元の文字に戻る。
    /// </summary>
    private static List<CssNode> ParseList(string s, ref int i, bool nested)
    {
        var list = new List<CssNode>();
        while (true)
        {
            int start = i;
            int j = ScanTo(s, i, "{;}");
            if (j < 0)
            {
                if (nested) throw new FormatException("閉じていない @media などがあります");
                if (start < s.Length) list.Add(new CssText(s[start..]));
                i = s.Length;
                return list;
            }
            char c = s[j];
            if (c == '}')
            {
                if (!nested) throw new FormatException($"対応の無い }} があります（{LineOf(s, j)} 行目の辺り。コメントを除いた後の行）");
                if (j > start) list.Add(new CssText(s[start..j]));
                i = j + 1;
                return list;
            }
            if (c == ';')
            {
                list.Add(new CssText(s[start..(j + 1)]));
                i = j + 1;
                continue;
            }

            var prelude = s[start..j].Trim();
            if (IsGroupAtRule(prelude))
            {
                int k = j + 1;
                var children = ParseList(s, ref k, nested: true);
                list.Add(new CssGroup(s[start..(j + 1)], children));
                i = k;
                continue;
            }

            int end = FindBlockEnd(s, j);
            var text = s[start..(end + 1)];
            // @keyframes・@font-face などの at 規則は丸ごと残す。
            if (prelude.StartsWith('@')) list.Add(new CssText(text));
            else list.Add(new CssRule(text, ParseSelectorList(prelude)));
            i = end + 1;
        }
    }

    private static int LineOf(string s, int index) => s.AsSpan(0, index).Count('\n') + 1;

    /// <summary>セレクタの並び（<c>,</c> 区切り。括弧と文字列の中の <c>,</c> では切らない）を、セレクタごとの要る語にする。</summary>
    private static IReadOnlyList<Requirement[]> ParseSelectorList(string prelude)
    {
        var result = new List<Requirement[]>();
        int i = 0;
        while (i <= prelude.Length)
        {
            int comma = ScanTo(prelude, i, ",");
            int end = comma < 0 ? prelude.Length : comma;
            var selector = prelude[i..end].Trim();
            if (selector.Length > 0) result.Add(ParseSelector(selector));
            if (comma < 0) break;
            i = comma + 1;
        }
        return result;
    }

    /// <summary>
    /// セレクタ 1 つの要る語：<c>.class</c>・<c>#id</c>・<c>[属性名</c>・各複合セレクタの頭のタグ名。
    /// 疑似クラス・疑似要素とその引数（<c>:not(…)</c> など）、<c>*</c>、属性の値は数えない。エスケープを含む名前も数えない（残す側に倒れる）。
    /// </summary>
    private static Requirement[] ParseSelector(string sel)
    {
        var reqs = new List<Requirement>();
        int i = 0;
        bool compoundStart = true;
        while (i < sel.Length)
        {
            char c = sel[i];
            if (char.IsWhiteSpace(c) || c is '>' or '+' or '~')
            {
                compoundStart = true;
                i++;
                continue;
            }
            switch (c)
            {
                case '.':
                {
                    i++;
                    var name = ReadIdent(sel, ref i);
                    if (name is not null) reqs.Add(new Requirement(RequirementKind.Class, name));
                    break;
                }
                case '#':
                {
                    i++;
                    var name = ReadIdent(sel, ref i);
                    if (name is not null) reqs.Add(new Requirement(RequirementKind.Id, name));
                    break;
                }
                case '[':
                {
                    i++;
                    while (i < sel.Length && char.IsWhiteSpace(sel[i])) i++;
                    var name = ReadIdent(sel, ref i);
                    // 名前空間付き（ns|attr）は名前空間を飛ばして読む。
                    if (i < sel.Length && sel[i] == '|' && (i + 1 >= sel.Length || sel[i + 1] != '='))
                    {
                        i++;
                        name = ReadIdent(sel, ref i);
                    }
                    if (name is not null) reqs.Add(new Requirement(RequirementKind.Attribute, name.ToLowerInvariant()));
                    int close = ScanTo(sel, i, "]");
                    i = close < 0 ? sel.Length : close + 1;
                    break;
                }
                case ':':
                {
                    i++;
                    if (i < sel.Length && sel[i] == ':') i++;
                    ReadIdent(sel, ref i);
                    if (i < sel.Length && sel[i] == '(') i = SkipParens(sel, i);
                    break;
                }
                case '*':
                case '|':
                case '&':
                    i++;
                    break;
                default:
                    if (compoundStart && IsIdentStart(c))
                    {
                        var name = ReadIdent(sel, ref i);
                        if (name is not null) reqs.Add(new Requirement(RequirementKind.Tag, name.ToLowerInvariant()));
                    }
                    else i++;
                    break;
            }
            compoundStart = false;
        }
        return reqs.ToArray();
    }

    /// <summary><paramref name="open"/> の <c>(</c> に対応する <c>)</c> の次の位置（入れ子と文字列を飛ばす）。</summary>
    private static int SkipParens(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '"' or '\'') { i = SkipString(s, i) - 1; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i + 1;
        }
        return s.Length;
    }

    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c is '_' or '-' || c > 0x7F;

    /// <summary>識別子を読む。エスケープ（<c>\</c>）を含むときは、その先まで読み飛ばして null（要る語に数えない）。</summary>
    private static string? ReadIdent(string s, ref int i)
    {
        int start = i;
        bool escaped = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\') { escaped = true; i = Math.Min(i + 2, s.Length); continue; }
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || c > 0x7F) { i++; continue; }
            break;
        }
        if (escaped || i == start) return null;
        return s[start..i];
    }
}

/// <summary>区画の CSS の大きさ（バイト）。</summary>
/// <param name="Raw">圧縮前。</param>
/// <param name="Gzip">gzip で圧縮した後。</param>
public sealed record CssBundleSize(
    [property: System.Text.Json.Serialization.JsonPropertyName("raw")] int Raw,
    [property: System.Text.Json.Serialization.JsonPropertyName("gzip")] int Gzip);

/// <summary>
/// 区画の CSS の控え（出力の隣の <c>{出力}.css-bundles.json</c>）。版の印・区画ごとの語の集合と大きさ・テンプレートごとの見本の URL。
/// 次のビルド（同じ印のあいだは語の集合を減らさない・一部だけのビルドの土台）と、確かめの道具（<c>scripts/verify-css-bundles.mjs</c>）が読む。
/// </summary>
public sealed class CssBundleManifest
{
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("words")]
    public Dictionary<string, List<string>> Words { get; set; } = new(StringComparer.Ordinal);

    [System.Text.Json.Serialization.JsonPropertyName("sizes")]
    public Dictionary<string, CssBundleSize> Sizes { get; set; } = new(StringComparer.Ordinal);

    [System.Text.Json.Serialization.JsonPropertyName("samples")]
    public Dictionary<string, List<string>> Samples { get; set; } = new(StringComparer.Ordinal);

    /// <summary>控えを読む。無い・読めない・中身が欠けているときは null。</summary>
    public static CssBundleManifest? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var loaded = JsonSerializer.Deserialize<CssBundleManifest>(File.ReadAllBytes(path));
            if (loaded is null || string.IsNullOrEmpty(loaded.Version) || loaded.Words is null) return null;
            loaded.Sizes ??= new Dictionary<string, CssBundleSize>(StringComparer.Ordinal);
            loaded.Samples ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>控えを書く。一時ファイルに書いてから置き換えるので、途中で止まっても壊れない。</summary>
    public void Save(string path)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = false }));
        File.Move(temp, path, overwrite: true);
    }
}
