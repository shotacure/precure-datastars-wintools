using Scriban;
using Scriban.Runtime;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// Scriban テンプレートのロード・キャッシュ・レンダリングを担当するヘルパー。
/// <para>
/// テンプレートは <c>Templates/*.sbn</c> を <see cref="AppContext.BaseDirectory"/> 配下から読む。
/// 一度パースしたテンプレートは <see cref="_cache"/> に保持し、ビルド中の再パースを防ぐ。
/// </para>
/// <para>
/// テンプレート間の <c>include</c> は <see cref="TemplateLoader"/> 経由で同フォルダから解決する。
/// 例: <c>{{ include '_layout.sbn' }}</c> でレイアウトの差し込みができる。
/// </para>
/// </summary>
public sealed class ScribanRenderer
{
    private readonly string _templateRoot;
    private readonly Dictionary<string, Template> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TemplateLoader _loader;

    public ScribanRenderer()
    {
        _templateRoot = Path.Combine(AppContext.BaseDirectory, "Templates");
        if (!Directory.Exists(_templateRoot))
            throw new InvalidOperationException(
                $"Templates ディレクトリが見つかりません: {_templateRoot}. " +
                "csproj の Copy 設定を確認してください。");
        _loader = new TemplateLoader(_templateRoot);
    }

    /// <summary>
    /// 指定テンプレートを model でレンダリングして文字列を返す。
    /// </summary>
    /// <param name="templateName">"home.sbn" のようなファイル名。</param>
    /// <param name="model">テンプレート内で参照可能なオブジェクト（プロパティ名は snake_case にしない・MemberRenamer を使わず素のまま参照）。</param>
    public string Render(string templateName, object model)
    {
        var template = LoadTemplate(templateName);

        // Scriban 既定の MemberRenamer は PascalCase → snake_case 変換。
        // 本プロジェクトはテンプレート可読性のため、C# プロパティ名（PascalCase）のままで
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
            MemberFilter = m => true
        };
        context.PushGlobal(scriptObject);
        return template.Render(context);
    }

    /// <summary>
    /// テンプレートを読み込み、キャッシュに格納したうえで返す。
    /// </summary>
    private Template LoadTemplate(string templateName)
    {
        if (_cache.TryGetValue(templateName, out var cached))
            return cached;

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

        _cache[templateName] = template;
        return template;
    }

    /// <summary>
    /// Scriban の <c>include</c> ディレクティブを同フォルダのテンプレート群から解決するためのローダ。
    /// </summary>
    private sealed class TemplateLoader : Scriban.Runtime.ITemplateLoader
    {
        private readonly string _root;
        public TemplateLoader(string root) => _root = root;

        public string GetPath(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templateName)
            => Path.Combine(_root, templateName);

        public string Load(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
            => File.ReadAllText(templatePath);

        // Scriban 7.x で ITemplateLoader.LoadAsync の戻り値が ValueTask<string?> に変更されたため
        // それに合わせる。本実装では実際に null を返すケースは無いが、シグネチャ整合のため string? で受ける。
        public ValueTask<string?> LoadAsync(TemplateContext context, Scriban.Parsing.SourceSpan callerSpan, string templatePath)
            => new ValueTask<string?>(File.ReadAllText(templatePath));
    }
}
