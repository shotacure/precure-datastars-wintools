namespace PrecureDataStars.Catalog.Forms.TemplateRendering;

/// <summary>
/// 役職テンプレ DSL の抽象構文木（AST）ノード基底（v1.2.0 工程 H 追加）。
/// <para>
/// 役職テンプレは Mustache 風の簡易シンタックスで以下の 3 種類を組み合わせる：
/// <list type="bullet">
///   <item><description><c>{NAME}</c> または <c>{NAME:opt1=val1,opt2=val2}</c> ... プレースホルダ展開（<see cref="PlaceholderNode"/>）</description></item>
///   <item><description><c>{#BLOCKS}...{/BLOCKS}</c> または <c>{#BLOCKS:first}...{/BLOCKS:first}</c> ... ブロック繰り返し（<see cref="BlockLoopNode"/>）</description></item>
///   <item><description><c>{?NAME}...{/?NAME}</c> ... 条件分岐（値が空でないとき表示、<see cref="ConditionalNode"/>）</description></item>
/// </list>
/// 上記以外の文字列は <see cref="LiteralNode"/> として扱う。
/// </para>
/// <para>
/// 実装方針：再帰ネストの厳密処理は省略し、<c>{#BLOCKS}</c> と <c>{?...}</c> は内側に
/// プレースホルダ・リテラルしか持てない（より複雑な階層は v1.2.0 では非対応、必要になったら拡張）。
/// </para>
/// </summary>
public abstract class TemplateNode
{
}

/// <summary>テンプレ中の素のリテラル文字列ノード。</summary>
public sealed class LiteralNode : TemplateNode
{
    public string Text { get; }
    public LiteralNode(string text) { Text = text; }
}

/// <summary>
/// プレースホルダノード <c>{NAME}</c> または <c>{NAME:opt=val,opt=val}</c>。
/// <see cref="Name"/> はプレースホルダ名（大文字スネークケース）、
/// <see cref="Options"/> は <c>:</c> 以降のキー＝値ペア（カンマ区切り）。
/// </summary>
public sealed class PlaceholderNode : TemplateNode
{
    public string Name { get; }
    public IReadOnlyDictionary<string, string> Options { get; }

    public PlaceholderNode(string name, IReadOnlyDictionary<string, string>? options = null)
    {
        Name = name;
        Options = options ?? new Dictionary<string, string>();
    }

    /// <summary>オプション値を取得（無ければ <paramref name="defaultValue"/>）。</summary>
    public string GetOption(string key, string defaultValue = "")
    {
        return Options.TryGetValue(key, out var v) ? v : defaultValue;
    }
}

/// <summary>
/// ブロック繰り返しノード <c>{#BLOCKS}...{/BLOCKS}</c>。
/// <see cref="Filter"/> は <c>:first</c> / <c>:rest</c> / <c>:last</c> または <c>""</c>（全部）。
/// <see cref="Body"/> は内側のテンプレ AST（複数ノードを順に展開）。
/// </summary>
public sealed class BlockLoopNode : TemplateNode
{
    public string Filter { get; }
    public IReadOnlyList<TemplateNode> Body { get; }

    public BlockLoopNode(string filter, IReadOnlyList<TemplateNode> body)
    {
        Filter = filter ?? "";
        Body = body ?? Array.Empty<TemplateNode>();
    }
}

/// <summary>
/// 条件分岐ノード <c>{?NAME}...{/?NAME}</c>。
/// プレースホルダ <see cref="Name"/> の解決値が非空の場合のみ <see cref="Body"/> を展開する。
/// </summary>
public sealed class ConditionalNode : TemplateNode
{
    public string Name { get; }
    public IReadOnlyList<TemplateNode> Body { get; }

    public ConditionalNode(string name, IReadOnlyList<TemplateNode> body)
    {
        Name = name;
        Body = body ?? Array.Empty<TemplateNode>();
    }
}
