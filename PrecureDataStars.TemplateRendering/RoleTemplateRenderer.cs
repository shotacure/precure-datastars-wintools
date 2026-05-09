
using System.Text;
using PrecureDataStars.TemplateRendering.Handlers;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 役職テンプレ DSL の展開エンジン本体（v1.2.0 工程 H 追加）。
/// <para>
/// <see cref="TemplateParser"/> が生成した AST と <see cref="TemplateContext"/> を受け取って、
/// 1 つの役職分の表示文字列を生成する。プレースホルダ解決には <see cref="ILookupCache"/> を経由して
/// company_alias / person_alias の表示名を引く。
/// </para>
/// <para>
/// サポートするプレースホルダ（v1.2.0 工程 H 時点）：
/// <list type="bullet">
///   <item><description><c>{ROLE_NAME}</c> ... 役職表示名</description></item>
///   <item><description><c>{LEADING_COMPANY}</c> ... カレントブロックの leading_company_alias_id 解決名</description></item>
///   <item><description><c>{COMPANIES[:sep="」「",wrap="「」"]}</c> ... カレントブロック内の COMPANY エントリ群を結合</description></item>
///   <item><description><c>{PERSONS[:sep="、"]}</c> ... カレントブロック内の PERSON エントリ群を結合</description></item>
///   <item><description><c>{LOGOS[:sep=" "]}</c> ... カレントブロック内の LOGO エントリ（ロゴ名義 → 文字列）</description></item>
///   <item><description><c>{TEXTS[:sep=" "]}</c> ... カレントブロック内の TEXT エントリの raw_text 結合</description></item>
///   <item><description><c>{THEME_SONGS[:kind=OP|ED|INSERT|OP+ED|ED+INSERT|ALL,columns=N]}</c> ... episode_theme_songs から動的取得（<see cref="ThemeSongsHandler"/> へ委譲）。kind 省略時は ALL 相当。</description></item>
/// </list>
/// </para>
/// <para>
/// サポートする構文：
/// <list type="bullet">
///   <item><description><c>{#BLOCKS[:first|rest|last]}...{/BLOCKS[:filter]}</c> ... ブロック繰り返し</description></item>
///   <item><description><c>{?NAME}...{/?NAME}</c> ... プレースホルダ NAME の解決値が非空のときだけ展開</description></item>
///   <item><description><c>{#THEME_SONGS[:kind=OP+ED]}...{/THEME_SONGS}</c> ... episode_theme_songs 楽曲行を反復（v1.2.0 工程 H-16 追加）。
///     内側で <c>{SONG_TITLE}</c> / <c>{SONG_KIND}</c> / <c>{LYRICIST}</c> / <c>{COMPOSER}</c> /
///     <c>{ARRANGER}</c> / <c>{SINGER}</c> / <c>{VARIANT_LABEL}</c> の楽曲スコーププレースホルダが
///     解決可能。表記（カギ括弧の種類・項目ラベル・改行位置）はテンプレ作者が完全に制御できる。
///     旧 <c>{THEME_SONGS}</c> プレースホルダ版（ハードコード書式）も互換のため残置。</description></item>
/// </list>
/// </para>
/// </summary>
public static class RoleTemplateRenderer
{
    /// <summary>
    /// AST <paramref name="nodes"/> を <paramref name="ctx"/> に対して展開し、整形済み文字列を返す。
    /// <c>{THEME_SONGS}</c> プレースホルダの解決には <paramref name="factory"/> を使って DB JOIN を発行する。
    /// その他のプレースホルダ解決には <paramref name="lookup"/> を使う（company / person 名義の表示名引き）。
    /// </summary>
    public static async Task<string> RenderAsync(
        IReadOnlyList<TemplateNode> nodes,
        TemplateContext ctx,
        IConnectionFactory factory,
        ILookupCache lookup,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        // カレントブロック / カレント楽曲は対応するループ内でのみ意味を持つ。トップレベルでは null。
        await RenderNodesAsync(nodes, ctx, currentBlock: null, currentSong: null, factory, lookup, sb, ct).ConfigureAwait(false);
        return sb.ToString();
    }

    private static async Task RenderNodesAsync(
        IReadOnlyList<TemplateNode> nodes,
        TemplateContext ctx,
        BlockSnapshot? currentBlock,
        ThemeSongsHandler.ThemeSongRow? currentSong,
        IConnectionFactory factory,
        ILookupCache lookup,
        StringBuilder sb,
        CancellationToken ct)
    {
        foreach (var n in nodes)
        {
            switch (n)
            {
                case LiteralNode lit:
                    sb.Append(lit.Text);
                    break;

                case PlaceholderNode ph:
                    {
                        string val = await ResolvePlaceholderAsync(ph, ctx, currentBlock, currentSong, factory, lookup, ct).ConfigureAwait(false);
                        sb.Append(val);
                        break;
                    }

                case BlockLoopNode loop:
                    {
                        var targetBlocks = FilterBlocks(ctx.Blocks, loop.Filter);
                        foreach (var b in targetBlocks)
                        {
                            // BLOCKS ループ内では currentSong は持ち越さない（曲スコープと混同しないため null に上書き）
                            await RenderNodesAsync(loop.Body, ctx, b, currentSong: null, factory, lookup, sb, ct).ConfigureAwait(false);
                        }
                        break;
                    }

                case ThemeSongsLoopNode tsLoop:
                    {
                        // v1.2.0 工程 H-16：{#THEME_SONGS:opts}...{/THEME_SONGS} の処理。
                        // SQL で楽曲行を取得し、各行を「現在の楽曲スコープ」として子テンプレを再帰評価する。
                        // 楽曲スコープのプレースホルダ（{SONG_TITLE} 等）は ResolvePlaceholderAsync 内で
                        // currentSong を参照して解決される。
                        string kindRaw = tsLoop.GetOption("kind", "");
                        IReadOnlyList<string>? kinds = null;
                        if (!string.IsNullOrWhiteSpace(kindRaw) && !kindRaw.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                        {
                            kinds = kindRaw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                           .Select(s => s.ToUpperInvariant())
                                           .ToArray();
                        }
                        var rows = await ThemeSongsHandler.FetchAsync(factory, ctx.ScopeEpisodeId, kinds, ct).ConfigureAwait(false);
                        foreach (var song in rows)
                        {
                            // THEME_SONGS ループ内では currentBlock は持ち越さない
                            await RenderNodesAsync(tsLoop.Body, ctx, currentBlock: null, currentSong: song, factory, lookup, sb, ct).ConfigureAwait(false);
                        }
                        break;
                    }

                case ConditionalNode cond:
                    {
                        // 条件名を「カレントスコープのプレースホルダ」として一度解決し、非空なら本体を展開
                        var probe = new PlaceholderNode(cond.Name);
                        string val = await ResolvePlaceholderAsync(probe, ctx, currentBlock, currentSong, factory, lookup, ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(val))
                        {
                            await RenderNodesAsync(cond.Body, ctx, currentBlock, currentSong, factory, lookup, sb, ct).ConfigureAwait(false);
                        }
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// <c>{#BLOCKS:first}</c> / <c>:rest}</c> / <c>:last}</c> / <c>""</c>（全部）の意味でブロックリストを絞り込む。
    /// </summary>
    private static IReadOnlyList<BlockSnapshot> FilterBlocks(IReadOnlyList<BlockSnapshot> all, string filter)
    {
        if (all.Count == 0) return Array.Empty<BlockSnapshot>();
        return filter switch
        {
            "first" => new[] { all[0] },
            "last"  => new[] { all[^1] },
            "rest"  => all.Count > 1 ? all.Skip(1).ToList() : Array.Empty<BlockSnapshot>(),
            _       => all,
        };
    }

    /// <summary>
    /// プレースホルダ <paramref name="ph"/> を解決する。
    /// カレントブロック（<paramref name="currentBlock"/>）が null のときは「役職全体スコープ」と見做し、
    /// COMPANIES / PERSONS / LEADING_COMPANY 等のブロック依存プレースホルダは空文字を返す
    /// （これらは <c>{#BLOCKS}</c> 内でのみ意味を持つ）。
    /// 同様に <paramref name="currentSong"/> が null のときは <c>{SONG_TITLE}</c> 等の楽曲スコープ
    /// プレースホルダも空文字を返す（<c>{#THEME_SONGS}</c> 内でのみ意味を持つ）。
    /// </summary>
    private static async Task<string> ResolvePlaceholderAsync(
        PlaceholderNode ph,
        TemplateContext ctx,
        BlockSnapshot? currentBlock,
        ThemeSongsHandler.ThemeSongRow? currentSong,
        IConnectionFactory factory,
        ILookupCache lookup,
        CancellationToken ct)
    {
        switch (ph.Name)
        {
            case "ROLE_NAME":
                return ctx.RoleName;

            // ── v1.2.0 工程 H-16 で追加：楽曲スコープのプレースホルダ ──
            // {#THEME_SONGS}...{/THEME_SONGS} ループ内でのみ意味を持つ。currentSong が null の場合は空文字。
            case "SONG_TITLE":
                return currentSong?.SongTitle ?? "";
            case "SONG_KIND":
                return currentSong?.ThemeKind ?? "";
            case "LYRICIST":
                return currentSong?.LyricistName ?? "";
            case "COMPOSER":
                return currentSong?.ComposerName ?? "";
            case "ARRANGER":
                return currentSong?.ArrangerName ?? "";
            case "SINGER":
                return currentSong?.SingerName ?? "";
            case "VARIANT_LABEL":
                return currentSong?.VariantLabel ?? "";

            case "THEME_SONGS":
                {
                    int cols = 1;
                    if (int.TryParse(ph.GetOption("columns", "1"), out var n) && n >= 1) cols = n;
                    // kind オプションは "OP" / "ED" / "INSERT" / "OP+ED" / "ED+INSERT" /
                    // "OP+ED+INSERT" / "ALL" の形式で受け取る（"+" 区切り、空または "ALL" は全部）。
                    // ハンドラには文字列配列として渡し、SQL 側の IN 句で絞り込む。
                    string kindRaw = ph.GetOption("kind", "");
                    IReadOnlyList<string>? kinds = null;
                    if (!string.IsNullOrWhiteSpace(kindRaw) && !kindRaw.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        kinds = kindRaw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                       .Select(s => s.ToUpperInvariant())
                                       .ToArray();
                    }
                    return await ThemeSongsHandler.RenderAsync(factory, ctx.ScopeEpisodeId, kinds, cols, ct).ConfigureAwait(false);
                }

            case "LEADING_COMPANY":
                {
                    if (currentBlock?.Block.LeadingCompanyAliasId is not int leadId) return "";
                    var name = await lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
                    return name ?? "";
                }

            case "COMPANIES":
                {
                    if (currentBlock is null) return "";
                    var names = new List<string>();
                    foreach (var e in currentBlock.Entries.Where(x => x.EntryKind == "COMPANY" && x.CompanyAliasId.HasValue))
                    {
                        var n = await lookup.LookupCompanyAliasNameAsync(e.CompanyAliasId!.Value).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(n)) names.Add(n!);
                    }
                    if (names.Count == 0) return "";
                    string sep  = ph.GetOption("sep", "」「");
                    string wrap = ph.GetOption("wrap", "「」");
                    return Wrap(string.Join(sep, names), wrap);
                }

            case "PERSONS":
                {
                    if (currentBlock is null) return "";
                    var names = new List<string>();
                    foreach (var e in currentBlock.Entries.Where(x => x.EntryKind == "PERSON" && x.PersonAliasId.HasValue))
                    {
                        var n = await lookup.LookupPersonAliasNameAsync(e.PersonAliasId!.Value).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(n)) names.Add(n!);
                    }
                    string sep = ph.GetOption("sep", "、");
                    return string.Join(sep, names);
                }

            case "LOGOS":
                {
                    if (currentBlock is null) return "";
                    var names = new List<string>();
                    foreach (var e in currentBlock.Entries.Where(x => x.EntryKind == "LOGO" && x.LogoId.HasValue))
                    {
                        var n = await lookup.LookupLogoNameAsync(e.LogoId!.Value).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(n)) names.Add(n!);
                    }
                    string sep = ph.GetOption("sep", " ");
                    return string.Join(sep, names);
                }

            case "TEXTS":
                {
                    if (currentBlock is null) return "";
                    var texts = currentBlock.Entries
                        .Where(x => x.EntryKind == "TEXT" && !string.IsNullOrEmpty(x.RawText))
                        .Select(x => x.RawText!).ToList();
                    string sep = ph.GetOption("sep", " ");
                    return string.Join(sep, texts);
                }

            default:
                // 未知のプレースホルダは何も出さない（誤記の検出は呼び出し側でのテンプレ検証に任せる）
                return "";
        }
    }

    /// <summary>
    /// <paramref name="content"/> を <paramref name="wrapSpec"/> で包む。
    /// <paramref name="wrapSpec"/> は 0/1/2 文字を想定：
    /// 0 文字 → 包まない、1 文字 → 両端に同じ文字、2 文字 → 開き / 閉じ。
    /// </summary>
    private static string Wrap(string content, string wrapSpec)
    {
        if (string.IsNullOrEmpty(wrapSpec)) return content;
        if (wrapSpec.Length == 1) return wrapSpec + content + wrapSpec;
        return wrapSpec[0] + content + wrapSpec[1];
    }
}
