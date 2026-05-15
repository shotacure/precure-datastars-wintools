using System.Text;
using PrecureDataStars.TemplateRendering.Handlers;
using PrecureDataStars.Data;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 役職テンプレ DSL の展開エンジン本体。
/// <para>
/// <see cref="TemplateParser"/> が生成した AST と <see cref="TemplateContext"/> を受け取って、
/// 1 つの役職分の表示文字列を生成する。プレースホルダ解決には <see cref="ILookupCache"/> を経由して
/// company_alias / person_alias の表示名を引く。
/// </para>
/// <para>
/// サポートするプレースホルダ：
/// <list type="bullet">
///   <item><description><c>{ROLE_NAME}</c> ... 役職表示名</description></item>
///   <item><description><c>{LEADING_COMPANY}</c> ... カレントブロックの leading_company_alias_id 解決名</description></item>
///   <item><description><c>{COMPANIES[:sep="」「",wrap="「」"]}</c> ... カレントブロック内の COMPANY エントリ群を結合</description></item>
///   <item><description><c>{PERSONS[:sep="、"]}</c> ... カレントブロック内の PERSON エントリ群を結合</description></item>
///   <item><description><c>{LOGOS[:sep=" "]}</c> ... カレントブロック内の LOGO エントリ（ロゴ名義 → 文字列）</description></item>
///   <item><description><c>{TEXTS[:sep=" "]}</c> ... カレントブロック内の TEXT エントリの raw_text 結合</description></item>
///   <item><description><c>{THEME_SONGS[:kind=OP|ED|INSERT|OP+ED|ED+INSERT|ALL,columns=N]}</c> ... episode_theme_songs から動的取得（<see cref="ThemeSongsHandler"/> へ委譲）。kind 省略時は ALL 相当。</description></item>
///   <item><description><c>{ROLE_LINK:code=ROLE_CODE}</c> ... 役職統計ページへのリンク化済み HTML。
///     <c>code</c> オプションで指定された役職コードに対応する役職表示名を <see cref="ILookupCache.LookupRoleHtmlAsync"/>
///     で取得し、太字（<c>&lt;strong&gt;</c>）でラップして埋め込む。テンプレ作者が <c>&lt;strong&gt;</c> を書く・
///     書かないで揺れないように「役職リンクなら必ず太字」を DSL の責務として保証する。<c>code</c> オプションが
///     空 / 未登録の役職コードの場合は何も出力しない（タグ残骸も残らない）。</description></item>
/// </list>
/// </para>
/// <para>
/// サポートする構文：
/// <list type="bullet">
///   <item><description><c>{#BLOCKS[:first|rest|last]}...{/BLOCKS[:filter]}</c> ... ブロック繰り返し</description></item>
///   <item><description><c>{?NAME}...{/?NAME}</c> ... プレースホルダ NAME の解決値が非空のときだけ展開</description></item>
///   <item><description><c>{#THEME_SONGS[:kind=OP+ED]}...{/THEME_SONGS}</c> ... episode_theme_songs 楽曲行を反復。
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
                        // {#THEME_SONGS:opts}...{/THEME_SONGS} の処理。
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
                        var rows = await ThemeSongsHandler.FetchAsync(factory, ctx.ScopeEpisodeId, kinds, lookup, ct).ConfigureAwait(false);
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

                case RoleReferenceNode roleRef:
                    {
                        // {ROLE:CODE.PLACEHOLDER} 構文の解決。
                        // 同 Group 内の sibling 役職の BlockSnapshot 群を取得し、内側プレースホルダを
                        // sibling スコープで評価して結果を埋め込む。
                        string siblingValue = await ResolveRoleReferenceAsync(
                            roleRef, ctx, factory, lookup, ct).ConfigureAwait(false);
                        sb.Append(siblingValue);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// <c>{ROLE:CODE.PLACEHOLDER}</c> ノードを評価する。
    /// <para>
    /// 評価手順:
    /// <list type="number">
    ///   <item><description>再帰禁止チェック：<see cref="TemplateContext.VisitedRoleCodes"/> に
    ///     <see cref="RoleReferenceNode.TargetRoleCode"/> が含まれていれば空文字を返す（ネスト ROLE 参照は 1 段で打ち止め）。</description></item>
    ///   <item><description><see cref="TemplateContext.SiblingRoleResolver"/> で sibling 役職の
    ///     BlockSnapshot 群を引く。null（未供給または該当役職が同 Group 内に存在しない）なら空文字。</description></item>
    ///   <item><description>sibling スコープの一時 <see cref="TemplateContext"/> を構築し、内側プレースホルダを
    ///     全 Block 横断で評価する。具体的には Block を 1 つずつカレントブロックにして
    ///     <see cref="ResolvePlaceholderAsync"/> を呼び、結果を連結する（sibling 役職の全 Block に渡って
    ///     {PERSONS} や {COMPANIES} を集める）。</description></item>
    /// </list>
    /// 内側プレースホルダの sep オプションがあればそれを使う（既定は <see cref="PlaceholderNode"/> の
    /// プレースホルダごとの既定値、ResolvePlaceholderAsync 内に従う）。Block 間で各プレースホルダの結果を
    /// 単純に連結するため、複数 Block にまたがる場合は内側プレースホルダの既定セパレータ（"、" や "」「"）
    /// による区切りが Block 内のみに適用される。
    /// </para>
    /// </summary>
    private static async Task<string> ResolveRoleReferenceAsync(
        RoleReferenceNode roleRef,
        TemplateContext ctx,
        IConnectionFactory factory,
        ILookupCache lookup,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(roleRef.TargetRoleCode)) return "";

        // 再帰禁止：すでに ROLE 参照経由で展開中の役職への参照は空文字に。
        if (ctx.VisitedRoleCodes.Contains(roleRef.TargetRoleCode)) return "";

        // sibling 解決器が未供給 → 旧コンテキスト経由（後方互換）。空文字で素通す。
        if (ctx.SiblingRoleResolver is null) return "";

        var siblingBlocks = ctx.SiblingRoleResolver(roleRef.TargetRoleCode);
        if (siblingBlocks is null || siblingBlocks.Count == 0) return "";

        // sibling スコープのコンテキストを作って、各 Block を順にカレントブロックとして
        // 内側プレースホルダを評価し、結果を連結する。
        var subCtx = ctx.WithSiblingRoleScope(roleRef.TargetRoleCode, siblingBlocks);
        var sb = new StringBuilder();
        bool first = true;
        foreach (var b in siblingBlocks)
        {
            // Block 単位の結果が空ならスペーサも出さない（先頭判定で sep を入れる方式は使わず、
            // 内側プレースホルダの既定挙動に任せる：PERSONS/COMPANIES 等は Block 単独でカンマ区切り）。
            string part = await ResolvePlaceholderAsync(
                roleRef.InnerPlaceholder, subCtx, b, currentSong: null, factory, lookup, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(part)) continue;

            // Block 間の連結は内側プレースホルダの sep オプション（指定があれば）を使う。既定は "、"。
            // {PERSONS} ベースの想定ユースケース（連載クレジットの漫画家連名）にマッチさせる。
            if (!first)
            {
                string sep = roleRef.InnerPlaceholder.GetOption("sep", "、");
                sb.Append(sep);
            }
            sb.Append(part);
            first = false;
        }
        return sb.ToString();
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
                // HTML 出力経路のため、役職名も HTML エスケープを通す。
                // 既存テンプレでは「役職名 ＋ ブロック展開結果」の単純連結で使われており、
                // 役職名に HTML 特殊文字が含まれるケースは稀だが念のため。
                return System.Net.WebUtility.HtmlEncode(ctx.RoleName);

            // ── 追加：楽曲スコープのプレースホルダ ──
            // {#THEME_SONGS}...{/THEME_SONGS} ループ内でのみ意味を持つ。currentSong が null の場合は空文字。
            case "SONG_TITLE":
                {
                    // 楽曲詳細ページへのリンク化済み HTML を返す。
                    // SongId が有効（>0）なら <a href="/songs/{id}/">タイトル</a>、無効ならエスケープしたタイトルのみ。
                    var title = currentSong?.SongTitle;
                    if (string.IsNullOrEmpty(title)) return "";
                    var safe = System.Net.WebUtility.HtmlEncode(title);
                    if (currentSong is { SongId: > 0 } cs)
                    {
                        return $"<a href=\"/songs/{cs.SongId}/\">{safe}</a>";
                    }
                    return safe;
                }
            case "SONG_KIND":
                // クレジット展開時のコード値（OP / ED / INSERT）をそのまま出すと、
                // エピソード詳細クレジットセクションで「OP", "ED", "INSERT"」のような英語コードが
                // 読者の目に入ってしまうため、表示ラベル（"OP" / "ED" / "挿入歌"）に正規化する。
                // OP / ED はコードと表示ラベルが一致するのでそのまま、INSERT のみ「挿入歌」に置き換え。
                // 未知の値は安全側でそのまま返す。
                return currentSong?.ThemeKind switch
                {
                    "OP" => "OP",
                    "ED" => "ED",
                    "INSERT" => "挿入歌",
                    null => "",
                    var other => other
                };
            case "LYRICIST":
                // stage B-4：構造化クレジットがあればリンク化済み HTML、なければ
                // フリーテキストを HtmlEncode した平文が <see cref="ThemeSongsHandler.ThemeSongRow.LyricistHtml"/>
                // に詰まっているのでそのまま使う。
                return currentSong?.LyricistHtml ?? "";
            case "COMPOSER":
                return currentSong?.ComposerHtml ?? "";
            case "ARRANGER":
                return currentSong?.ArrangerHtml ?? "";
            case "SINGER":
                return currentSong?.SingerHtml ?? "";
            case "VARIANT_LABEL":
                return System.Net.WebUtility.HtmlEncode(currentSong?.VariantLabel ?? "");

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
                    return await ThemeSongsHandler.RenderAsync(factory, ctx.ScopeEpisodeId, kinds, cols, lookup, ct).ConfigureAwait(false);
                }

            case "LEADING_COMPANY":
                {
                    if (currentBlock?.Block.LeadingCompanyAliasId is not int leadId) return "";
                    // HTML 版を取得することで企業詳細ページへの <a href> リンクが入る。
                    // SiteBuilder 側 LookupCache はリンク化済み HTML を、Catalog 側は HTML エスケープした
                    // プレーンテキストを返す（ILookupCache のデフォルト実装にフォールバック）。
                    var name = await lookup.LookupCompanyAliasHtmlAsync(leadId).ConfigureAwait(false);
                    return name ?? "";
                }

            case "COMPANIES":
                {
                    if (currentBlock is null) return "";
                    var names = new List<string>();
                    foreach (var e in currentBlock.Entries.Where(x => x.EntryKind == "COMPANY" && x.CompanyAliasId.HasValue))
                    {
                        // HTML 版で取得。<a href="/companies/{id}/">屋号名</a> が返る。
                        var n = await lookup.LookupCompanyAliasHtmlAsync(e.CompanyAliasId!.Value).ConfigureAwait(false);
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
                        // HTML 版で取得。<a href="/persons/{id}/">名義</a> が返る。
                        // 共有名義（1 alias → 複数 person）は SiteBuilder 側 LookupCache 内で
                        // 「名義[1] [2]」のような添字付き複数リンクに展開される。
                        var n = await lookup.LookupPersonAliasHtmlAsync(e.PersonAliasId!.Value).ConfigureAwait(false);
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
                        // HTML 版で取得。<a href="/companies/{company_id}/">屋号名</a> が返る。
                        var n = await lookup.LookupLogoHtmlAsync(e.LogoId!.Value).ConfigureAwait(false);
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
                    // HTML 出力経路のため、TEXT エントリも HTML エスケープを通す。
                    // raw_text が HTML 特殊文字（< > &）を含むケースで XSS や表示崩れを防ぐ。
                    var escapedTexts = texts.Select(t => System.Net.WebUtility.HtmlEncode(t));
                    return string.Join(sep, escapedTexts);
                }

            case "ROLE_LINK":
                {
                    // 役職統計ページへのリンク化済み HTML を返すプレースホルダ。
                    // テンプレ作者が役職ラベルをハードコード（例: <strong>漫画</strong>）するのではなく、
                    // {ROLE_LINK:code=MANGA} と書くことで「役職コードから表示名解決 + リンク化 + 太字ラップ」を
                    // DSL レンダラ側に一任できるようにする。SiteBuilder 側では
                    // <a href="/stats/roles/{role_code}/">表示名</a> を、Catalog 側プレビューでは
                    // HTML エスケープした表示名のみが <see cref="ILookupCache.LookupRoleHtmlAsync"/> から返り、
                    // 本レンダラがその外側に一律 <strong> ラップを掛ける。
                    //
                    // 「役職リンクなら必ず太字、違えば太字ではない」という見た目ルールを DSL の責務として
                    // 保証する設計（テンプレ側に <strong> を書かせない）。code が空 / 未登録の場合は何も
                    // 出力しない（タグ残骸も残さない）。
                    //
                    // stage B-10：オプション label=... を追加。テンプレ側で表示ラベルを直接指定したい
                    // ケース（「作詞」「うた」など文脈ごとに表記揺れを管理したい場合）に使う。
                    //   ・label 未指定 → 既存挙動：roles.name_ja を表示、<strong> ラップ付き
                    //   ・label 指定あり → 指定文字列を表示、<strong> ラップなし（太字が要るならテンプレで明示）
                    // 役職コードがマスタ未登録のときは、リンク先 404 を避けるため、リンクなしでラベルだけ返す
                    // 挙動を ILookupCache.LookupRoleHtmlWithLabelAsync 側で持っている。
                    string roleCode = ph.GetOption("code", "");
                    if (string.IsNullOrEmpty(roleCode)) return "";
                    string label = ph.GetOption("label", "");
                    if (!string.IsNullOrEmpty(label))
                    {
                        var inner = await lookup.LookupRoleHtmlWithLabelAsync(roleCode, label).ConfigureAwait(false);
                        return inner ?? "";
                    }
                    var html = await lookup.LookupRoleHtmlAsync(roleCode).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(html)) return "";
                    return $"<strong>{html}</strong>";
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
