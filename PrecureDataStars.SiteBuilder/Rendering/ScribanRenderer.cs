using Scriban;
using Scriban.Runtime;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// Scriban テンプレートのロード・キャッシュ・レンダリングを担当するヘルパー。
/// テンプレートは <c>Templates/*.sbn</c> を <see cref="AppContext.BaseDirectory"/> 配下から読む。
/// 一度パースしたテンプレートは <see cref="_cache"/> に保持し、ビルド中の再パースを防ぐ。
/// テンプレート間の <c>include</c> は <see cref="TemplateLoader"/> 経由で同フォルダから解決する。
/// 例: <c>{{ include '_layout.sbn' }}</c> でレイアウトの差し込みができる。
/// <para>
/// <see cref="Render"/> はページレンダリングの並列実行から同時に呼ばれる。パース済み
/// <see cref="Template"/>（AST）は不変で複数スレッドから同時レンダリング可能（評価中の状態は
/// すべて呼び出しごとに生成する <see cref="TemplateContext"/> 側が持つ）なため、
/// 共有キャッシュ辞書 2 つ（<see cref="_cache"/> / <see cref="_includeCache"/>）への
/// アクセスだけをロックで直列化すればスレッドセーフが成立する。
/// </para>
/// </summary>
public sealed class ScribanRenderer
{
    /// <summary>
    /// テンプレ 1 回のレンダリング中に許容するループ反復回数の上限。
    /// Scriban 既定の <c>TemplateContext.LoopLimit</c> は 1000 で、エピソード総数が
    /// 1000 を超えるシリーズ運用では <c>/episodes/</c> ランディングのレンダリングが
    /// "Exceeding number of iteration limit `1000`" エラーで失敗する。
    /// 全 TV シリーズの全エピソードを 1 ページで列挙する設計上、累積ループ数は
    /// 「シリーズ数 + 全エピソード数」になるため、十分な余裕を見て 1,000,000 に引き上げる。
    /// 安全側に振った数値であり、現実的なテンプレ無限ループの検出能力は失われない
    /// （無限ループは秒オーダーで 100 万に到達する）。
    /// </summary>
    private const int LoopLimit = 1_000_000;

    private readonly string _templateRoot;
    private readonly Dictionary<string, Template> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TemplateLoader _loader;

    /// <summary>
    /// <c>include</c> で読み込まれたテンプレートのパース結果キャッシュ（フルパス → パース済み Template）。
    /// Scriban は include 先のパース結果を <see cref="TemplateContext.CachedTemplates"/> に
    /// 「コンテキスト単位」でしか保持しないため、ページごとに新しい TemplateContext を作る本クラスの
    /// 構造では、素のままだと全ページ × 全 include でファイル読み込みと再パースが発生する
    /// （例：_layout.sbn → _share-buttons.sbn が 3,000 ページ分re-parse される）。
    /// レンダリング前に本辞書を CachedTemplates へ種付けし、レンダリング後に新規パース分を回収することで、
    /// include 先のパースをビルド全体で 1 回に抑える。
    /// </summary>
    private readonly Dictionary<string, Template> _includeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>共有キャッシュ辞書（<see cref="_cache"/> / <see cref="_includeCache"/>）の同期用ロック。</summary>
    private readonly object _cacheLock = new();

    public ScribanRenderer()
    {
        _templateRoot = Path.Combine(AppContext.BaseDirectory, "Templates");
        if (!Directory.Exists(_templateRoot))
            throw new InvalidOperationException(
                $"Templates ディレクトリが見つかりません: {_templateRoot}. " +
                "csproj の Copy 設定を確認してください。");
        _loader = new TemplateLoader(_templateRoot);
    }

    /// <summary>指定テンプレートを model でレンダリングして文字列を返す。</summary>
    /// <param name="templateName">"home.sbn" のようなファイル名。</param>
    /// <param name="model">テンプレート内で参照可能なオブジェクト（プロパティ名は snake_case にしない・MemberRenamer を使わず素のまま参照）。</param>
    public string Render(string templateName, object model)
    {
        var template = LoadTemplate(templateName);

        // Scriban 既定の MemberRenamer は PascalCase → snake_case 変換。
        // 本プロジェクトはテンプレート可読性のため、C# プロパティ名(PascalCase)のままで
        // 参照する方針なので、トップレベルにも、ネストしたオブジェクトのプロパティアクセスにも
        // 適用される TemplateContext.MemberRenamer を恒等関数で上書きする。
        // ScriptObject.Import の renamer 引数だけだと、トップレベルの自動 import 時のみ効いて
        // ネストプロパティアクセスでは効かない（既定のリネーマで snake_case を探しに行ってしまい、
        // 結果として空文字が返る）ため、両方を素通しに設定する必要がある。
        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: m => m.Name);

        var context = new TemplateContext
        {
            TemplateLoader = _loader,
            MemberRenamer = m => m.Name,
            MemberFilter = m => true,
            // 既定値 1000 では /episodes/ ランディングなど累積ループ数が
            // 多いテンプレートで打ち止めになるため、十分大きな値を採用する。
            LoopLimit = LoopLimit,
            // Scriban が <c>{{ var }}</c> の出力時に内部 ScriptToString を経由する際、
            // <c>LimitToString</c> が非ゼロだと結果長を超過した文字列の末尾を「...」で切る挙動になる。
            // Scriban 7.x のデフォルト動作に依存して長文の HTML 断片（楽曲索引カードの
            // 累積メタ HTML など）が途中で <c>"..."</c> 終端に化けるのを避けるため、
            // 明示的にゼロ＝無制限に固定する。
            LimitToString = 0
        };
        context.PushGlobal(scriptObject);

        // include 先のパース済みテンプレートをコンテキストへ種付けする（キーはローダが解決するフルパス）。
        // これにより Scriban の GetOrCreateTemplate はキャッシュヒットし、ファイル読み込みも再パースも走らない。
        // 共有辞書からコンテキスト私有の CachedTemplates へ参照コピーするだけなので、ロック保持は一瞬で済む。
        lock (_cacheLock)
        {
            foreach (var (path, parsed) in _includeCache)
                context.CachedTemplates[path] = parsed;
        }

        var html = template.Render(context);

        // 本レンダリング中に新規パースされた include 先を共有キャッシュへ回収し、次ページ以降で再利用する。
        // 並列実行中に複数スレッドが同じ include を同時パースした場合も、回収は最後の 1 件が残るだけで
        // 実害はない（同一ソースのパース結果は等価）。
        lock (_cacheLock)
        {
            foreach (var (path, parsed) in context.CachedTemplates)
            {
                if (!_includeCache.ContainsKey(path))
                    _includeCache[path] = parsed;
            }
        }

        return html;
    }

    /// <summary>テンプレートを読み込み、キャッシュに格納したうえで返す。 並列レンダリングから同時に呼ばれるためキャッシュ参照・格納はロックで直列化する （同名テンプレの同時パースが起きても等価な結果の上書きになるだけで実害は無いが、 Dictionary 自体の並行書き込みは未定義動作のため必ずロックを通す）。</summary>
    private Template LoadTemplate(string templateName)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(templateName, out var cached))
                return cached;
        }

        var fullPath = Path.Combine(_templateRoot, templateName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Template not found: {fullPath}", fullPath);

        var source = File.ReadAllText(fullPath);
        var template = Template.Parse(source, fullPath);
        if (template.HasErrors)
        {
            var msg = string.Join("\n", template.Messages);
            throw new InvalidOperationException(
                $"テンプレートのパースに失敗しました: {templateName}\n{msg}");
        }

        lock (_cacheLock)
        {
            _cache[templateName] = template;
        }
        return template;
    }

    /// <summary>Scriban の <c>include</c> ディレクティブを同フォルダのテンプレート群から解決するためのローダ。</summary>
    private sealed class TemplateLoader : Scriban.Runtime.ITemplateLoader
    {
        private readonly string _root;
        public TemplateLoader(string root) => _root = root;

        public string GetPath(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templateName)
            => Path.Combine(_root, templateName);

        public string Load(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
            => File.ReadAllText(templatePath);

        // ITemplateLoader.LoadAsync は ValueTask&lt;string?&gt; を返す（本実装で null を返すケースは無い）。
        public ValueTask<string?> LoadAsync(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
            => new ValueTask<string?>(File.ReadAllText(templatePath));
    }
}