namespace PrecureDataStars.SiteBuilder.TemplateRendering;

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

/// <summary>
/// 主題歌繰り返しノード <c>{#THEME_SONGS:opt=val,...}...{/THEME_SONGS}</c>
/// （v1.2.0 工程 H-16 で追加）。
/// <para>
/// <c>episode_theme_songs</c> + <c>song_recordings</c> + <c>songs</c> を JOIN して取得した楽曲行を
/// 順に反復し、内側の <see cref="Body"/> を曲ごとに展開する。<see cref="Body"/> 内では曲スコープの
/// プレースホルダ <c>{SONG_TITLE}</c> / <c>{SONG_KIND}</c> / <c>{LYRICIST}</c> /
/// <c>{COMPOSER}</c> / <c>{ARRANGER}</c> / <c>{SINGER}</c> / <c>{VARIANT_LABEL}</c> が解決可能。
/// </para>
/// <para>
/// オプション：
/// <list type="bullet">
///   <item><description><c>kind</c> = <c>OP</c> / <c>ED</c> / <c>INSERT</c> / <c>OP+ED</c> / <c>ALL</c> など。
///     省略時は <c>OP+ED+INSERT</c> 全部。</description></item>
/// </list>
/// 旧 <c>{THEME_SONGS}</c> プレースホルダ版（ハードコード書式）も互換のため残置。
/// 新ループ構文では <c>columns</c> オプションは使わない（縦並びが基本、横並びは
/// テンプレ作者が自前で HTML テーブル等を書く）。
/// </para>
/// </summary>
public sealed class ThemeSongsLoopNode : TemplateNode
{
    public IReadOnlyDictionary<string, string> Options { get; }
    public IReadOnlyList<TemplateNode> Body { get; }

    public ThemeSongsLoopNode(IReadOnlyDictionary<string, string>? options, IReadOnlyList<TemplateNode> body)
    {
        Options = options ?? new Dictionary<string, string>();
        Body = body ?? Array.Empty<TemplateNode>();
    }

    /// <summary>オプション値を取得（無ければ <paramref name="defaultValue"/>）。</summary>
    public string GetOption(string key, string defaultValue = "")
    {
        return Options.TryGetValue(key, out var v) ? v : defaultValue;
    }
}
