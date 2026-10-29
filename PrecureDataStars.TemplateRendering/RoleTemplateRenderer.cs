using System.Text;
using PrecureDataStars.TemplateRendering.Handlers;
using PrecureDataStars.Data;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 役職テンプレ DSL の展開エンジン本体。
/// <see cref="TemplateParser"/> が生成した AST と <see cref="TemplateContext"/> を受け取って、
/// 1 つの役職分の表示文字列を生成する。プレースホルダ解決には <see cref="ILookupCache"/> を経由して
/// company_alias / person_alias の表示名を引く。
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
/// サポートする構文：
/// <list type="bullet">
///   <item><description><c>{#BLOCKS[:first|rest|last]}...{/BLOCKS[:filter]}</c> ... ブロック繰り返し</description></item>
///   <item><description><c>{#MEDLEY_PARTS}...{/MEDLEY_PARTS}</c> ... <c>{#THEME_SONGS}</c> の中で、メドレーの元の曲（song_medley_parts）を順に曲スコープにして反復。中では元の曲の {SONG_TITLE} / {LYRICIST} / {COMPOSER} / {ARRANGER} が使える。</description></item>
///   <item><description><c>{?NAME}...{/?NAME}</c> ... プレースホルダ NAME の解決値が非空のときだけ展開</description></item>
///   <item><description><c>{?!NAME}...{/?!NAME}</c> ... プレースホルダ NAME の解決値が空のときだけ展開（否定の条件）</description></item>
///   <item><description><c>{#THEME_SONGS[:kind=OP+ED][,seq=N|N-M]}...{/THEME_SONGS}</c> ... episode_theme_songs 楽曲行を反復。
///     <c>seq</c> を書くと、番号（seq）がその値・範囲に入る曲だけに絞る。
///     役職の行の <c>theme_song_seq</c>（<see cref="TemplateContext.ThemeSongSeq"/>）があれば、さらにその範囲の曲だけに絞る
///     （旧 <c>{THEME_SONGS}</c> プレースホルダにも効く）。
///     曲名（<c>{SONG_TITLE}</c>）は、画面の表記（<c>title_credited_text</c>）があれば文字だけその表記で出す（誤記があるときは使わない）。
///     <c>merge_same_song=1</c> を書くと、同じ曲の録音が続く行を 1 曲にまとめる（歌唱は歌い手を改行で並べ、版の名前は出さない）。
///     内側で <c>{SONG_TITLE}</c> / <c>{SONG_KIND}</c> / <c>{LYRICIST}</c> / <c>{COMPOSER}</c> /
///     <c>{ARRANGER}</c> / <c>{SINGER}</c> / <c>{CHORUS}</c> / <c>{VARIANT_LABEL}</c> / <c>{SONG_SERIES_TITLE}</c> の楽曲スコーププレースホルダが
///     解決可能（<c>{CHORUS}</c> は <c>BACKING_VOCALS</c> 役職の連名、不在時は空）。
///     連名のプレースホルダ（<c>{LYRICIST}</c> / <c>{COMPOSER}</c> / <c>{ARRANGER}</c> / <c>{MEDLEY_ARRANGER}</c> /
///     <c>{SINGER}</c> / <c>{CHORUS}</c>）は <c>sep="…"</c> で名義の間の区切りを差し替えられる（DB の区切りの代わりに
///     テンプレの文字を HTML のまま入れる。<c>sep="&lt;br&gt;"</c> で改行）。構造化クレジットが無い曲には効かない。
///     <c>{SONG_SERIES_TITLE}</c> は曲の作品（録音の作品）の正式タイトルで、クレジットの作品と同じ作品の曲なら空。
///     <c>{SAME_COMPOSER_ARRANGER}</c> は作曲と編曲が同じ名義（連名も同じ）なら "1"、違えば空で、
///     <c>{?SAME_COMPOSER_ARRANGER}</c> / <c>{?!SAME_COMPOSER_ARRANGER}</c> で書き分ける。
///     <c>{SAME_LYRICIST_COMPOSER_ARRANGER}</c> は作詞・作曲・編曲の 3 つが同じ名義なら "1"、違えば空。
///     所属の会社名だけを出す行（「Nostalgic Orchestra」）は会社名どうしで同じと見る。
///     <c>{LYRICIST}</c> / <c>{COMPOSER}</c> / <c>{ARRANGER}</c> は、会社名だけの行なら会社名のあとに実際の人物を小さい括弧で添え、
///     <c>{?SAME_LYRICIST_COMPOSER_ARRANGER}</c> / <c>{?SAME_COMPOSER_ARRANGER}</c> の中では、まとめた役職ぶんの人物を
///     役職つきで並べる（「作詞・作曲・編曲：Nostalgic Orchestra（作詞・作曲：藤本 記子、編曲：福富 雅之）」）。
///     <c>{LYRICS_TRANSLATOR}</c> は英訳（<c>LYRICS_TRANSLATION</c> 役職）の連名で、無い曲は空。
///     <c>{INSTRUMENTAL}</c> はその回（その映画）ではインストゥルメンタルで使った曲なら "1"、違えば空で、
///     <c>{?INSTRUMENTAL}</c> / <c>{?!INSTRUMENTAL}</c> で曲名の添え書きや「オリジナルボーカル曲」の見出しを書き分ける。
///     表記（カギ括弧の種類・項目ラベル・改行位置）はテンプレ作者が完全に制御できる。
///     旧 <c>{THEME_SONGS}</c> プレースホルダ版（ハードコード書式）も互換のため残置。</description></item>
/// </list>
/// </summary>
public static class RoleTemplateRenderer
{
    /// <summary>
    /// 連名のプレースホルダの値。<c>sep="…"</c> の指定があり、構造化クレジットの名義が 2 つ以上あれば、
    /// 名義の間をテンプレの文字（HTML のまま。<c>&lt;br&gt;</c> で改行）でつなぐ。指定が無いときや
    /// フリーテキストのときは、DB の区切りでつないだ <paramref name="joinedHtml"/> を返す。
    /// </summary>
    private static string NamesHtml(PlaceholderNode ph, string? joinedHtml,
        IReadOnlyList<(string Sep, string Html)>? parts)
    {
        if (parts is { Count: > 1 } && ph.Options.TryGetValue("sep", out var sep))
            return string.Join(sep, parts.Select(p => p.Html));
        return joinedHtml ?? "";
    }

    /// <summary>
    /// 1 行にまとめる条件の名前 → その行にまとめた役職の並び。
    /// <c>SAME_LYRICIST_COMPOSER_ARRANGER</c> は作詞・作曲・編曲、<c>SAME_COMPOSER_ARRANGER</c> は作曲・編曲。それ以外は null。
    /// </summary>
    private static IReadOnlyList<string>? MergedRolesOf(string conditionName) => conditionName switch
    {
        "SAME_LYRICIST_COMPOSER_ARRANGER" => new[] { SongCreditRoles.Lyrics, SongCreditRoles.Composition, SongCreditRoles.Arrangement },
        "SAME_COMPOSER_ARRANGER" => new[] { SongCreditRoles.Composition, SongCreditRoles.Arrangement },
        _ => null,
    };

    /// <summary>
    /// <c>{#THEME_SONGS}</c> の <c>seq</c> オプション、および役職の行の <c>theme_song_seq</c>（<see cref="TemplateContext.ThemeSongSeq"/>）
    /// の値（"N" または "N-M"）を読む。空・読めない値なら false（絞り込まない）。
    /// </summary>
    private static bool TryParseSeqRange(string raw, out int from, out int to)
    {
        from = 0; to = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out from)) { to = from; return true; }
        return parts.Length == 2 && int.TryParse(parts[0], out from) && int.TryParse(parts[1], out to) && from <= to;
    }

    /// <summary>
    /// <c>{#THEME_SONGS}</c> の <c>merge_same_song=1</c>：並びの中で同じ曲（song_id）の録音が続く行を 1 行にまとめる。
    /// まとめた行は先頭の行を元にし、歌唱（<c>{SINGER}</c>）は各録音の歌い手を改行（&lt;br&gt;）でつなぎ、
    /// 版の名前（<c>{VARIANT_LABEL}</c>）は空にする。作詞・作曲・編曲は曲が同じなので先頭の行のまま。
    /// </summary>
    private static List<ThemeSongsHandler.ThemeSongRow> MergeSameSongRows(IReadOnlyList<ThemeSongsHandler.ThemeSongRow> rows)
    {
        var result = new List<ThemeSongsHandler.ThemeSongRow>();
        foreach (var row in rows)
        {
            var prev = result.Count > 0 ? result[^1] : null;
            if (prev is null || prev.SongId <= 0 || prev.SongId != row.SongId)
            {
                result.Add(row);
                continue;
            }
            // 続く録音の歌い手を、前の行の歌唱に改行でつなぐ（区切りの差し替え sep= にも効くよう、部品の並びにも足す）
            prev.SingerHtml = string.IsNullOrEmpty(prev.SingerHtml) ? row.SingerHtml
                            : string.IsNullOrEmpty(row.SingerHtml) ? prev.SingerHtml
                            : prev.SingerHtml + "<br>" + row.SingerHtml;
            prev.SingerParts = prev.SingerParts.Concat(row.SingerParts.Select((p, i) => i == 0 ? ("<br>", p.Html) : p)).ToList();
            prev.VariantLabel = null;
        }
        return result;
    }

    /// <summary>AST <paramref name="nodes"/> を <paramref name="ctx"/> に対して展開し、整形済み文字列を返す。</summary>
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
                        // テンプレの文字の「」の中に差し込む値（「{SONG_TITLE}」など）は、値の中の「」を『』にする。
                        if ((val.IndexOf('「') >= 0 || val.IndexOf('」') >= 0) && JapaneseQuotes.IsInsideQuotes(sb))
                            val = JapaneseQuotes.InQuotes(val);
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
                        string kindRaw = tsLoop.GetOption("kind", "");
                        IReadOnlyList<string>? kinds = null;
                        if (!string.IsNullOrWhiteSpace(kindRaw) && !kindRaw.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                        {
                            kinds = kindRaw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                           .Select(s => s.ToUpperInvariant())
                                           .ToArray();
                        }
                        var rows = await ThemeSongsHandler.FetchAsync(factory, ctx.ScopeEpisodeId, ctx.ScopeSeriesId, kinds, lookup, ct).ConfigureAwait(false);
                        // seq=N / seq=N-M：同じ区分の中で番号（episode_theme_songs / series_theme_songs.seq）が範囲に入る曲だけを出す。
                        // 「テーマソング」と「挿入歌」のように、同じ挿入歌の区分の曲を別々の役職の欄に分けて出す画面用。
                        if (TryParseSeqRange(tsLoop.GetOption("seq", ""), out int seqFrom, out int seqTo))
                            rows = rows.Where(r => r.Seq is byte s && s >= seqFrom && s <= seqTo).ToList();
                        // 役職の行の theme_song_seq：主題歌のカードが 2 枚に分かれ、挿入歌がカードごとに分かれて出る回用。
                        // テンプレは役職ごとに共通なので、カードごとの範囲は役職の行で持つ（テンプレの seq= と両方あれば両方を満たす曲だけ）。
                        if (TryParseSeqRange(ctx.ThemeSongSeq ?? "", out int roleSeqFrom, out int roleSeqTo))
                            rows = rows.Where(r => r.Seq is byte s && s >= roleSeqFrom && s <= roleSeqTo).ToList();
                        // merge_same_song=1：同じ曲の録音が続く行を 1 曲にまとめる（画面が 1 曲の下に歌い手を並べる作品用）。
                        if (tsLoop.GetOption("merge_same_song", "") == "1")
                            rows = MergeSameSongRows(rows);
                        foreach (var song in rows)
                        {
                            // THEME_SONGS ループ内では currentBlock は持ち越さない
                            await RenderNodesAsync(tsLoop.Body, ctx, currentBlock: null, currentSong: song, factory, lookup, sb, ct).ConfigureAwait(false);
                        }
                        break;
                    }

                case MedleyPartsLoopNode medleyLoop:
                    {
                        // {#MEDLEY_PARTS}...{/MEDLEY_PARTS}：いまの曲がメドレーなら元の曲を順に曲スコープにして展開する。
                        if (currentSong is null || currentSong.SongId <= 0) break;
                        var parts = await ThemeSongsHandler.FetchMedleyPartsAsync(factory, currentSong.SongId, lookup, ct).ConfigureAwait(false);
                        foreach (var part in parts)
                        {
                            await RenderNodesAsync(medleyLoop.Body, ctx, currentBlock: null, currentSong: part, factory, lookup, sb, ct).ConfigureAwait(false);
                        }
                        break;
                    }

                case ConditionalNode cond:
                    {
                        // 条件名を「カレントスコープのプレースホルダ」として一度解決し、非空なら本体を展開。
                        // 名前の頭に "!" があれば否定（解決値が空のときだけ展開）。
                        bool negate = cond.Name.StartsWith('!');
                        var probe = new PlaceholderNode(negate ? cond.Name.Substring(1) : cond.Name);
                        string val = await ResolvePlaceholderAsync(probe, ctx, currentBlock, currentSong, factory, lookup, ct).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(val) == negate)
                        {
                            // 「作詞・作曲・編曲：」「作曲・編曲：」の 1 行にまとめる条件の中では、まとめた役職を曲に覚えさせ、
                            // 会社名だけの行の人物の括弧に役職を添えられるようにする（抜けたら元に戻す）。
                            var merged = !negate && currentSong is not null ? MergedRolesOf(cond.Name) : null;
                            var saved = currentSong?.MergedCreditRoles;
                            if (merged is not null) currentSong!.MergedCreditRoles = merged;
                            try
                            {
                                await RenderNodesAsync(cond.Body, ctx, currentBlock, currentSong, factory, lookup, sb, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (merged is not null) currentSong!.MergedCreditRoles = saved;
                            }
                        }
                        break;
                    }

                case RoleReferenceNode roleRef:
                    {
                        // {ROLE:CODE.PLACEHOLDER} 構文の解決。
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

    /// <summary><c>{#BLOCKS:first}</c> / <c>:rest}</c> / <c>:last}</c> / <c>""</c>（全部）の意味でブロックリストを絞り込む。</summary>
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

            case "SERIES_TITLE":
                // scope_kind=SERIES のシリーズタイトル（series.title）。EPISODE スコープや未供給時は空文字。
                // テンプレ作者がシリーズ別カスタム見出しで「「{SERIES_TITLE}」主題歌」のように使う。
                return System.Net.WebUtility.HtmlEncode(ctx.ScopeSeriesTitle ?? "");

            case "FILM_RATING_NO":
                // scope_kind=SERIES のシリーズの映倫審査番号（series.film_rating_no）。未登録や EPISODE スコープは空文字。
                // 映画のタイトルカード（役職 TITLE）で「{SERIES_TITLE}{?FILM_RATING_NO} 映倫 {FILM_RATING_NO}{/?FILM_RATING_NO}」のように使う。
                return System.Net.WebUtility.HtmlEncode(ctx.ScopeFilmRatingNo ?? "");

            case "CJ_MARK":
                // scope_kind=SERIES のシリーズのタイトルカードに CJ マークが付くとき「CJ」、付かないときは空文字（series.film_cj_mark）。
                // 映画のタイトルカード（役職 TITLE）で「{?CJ_MARK} <span class="cj-mark">{CJ_MARK}</span>{/?CJ_MARK}」のように使う。
                return ctx.ScopeFilmCjMark ? "CJ" : "";

            case "PRODUCT":
                {
                    // {PRODUCT:kind=OST_MOVIE} 形式。現スコープのシリーズに紐付くディスクを持ち、
                    // 指定 product_kind に一致する商品を 1 件引いて「商品タイトル + 商品詳細ページへのリンク」
                    // を返す。テンプレに商品カタログ番号を焼き込まず、シリーズ × 種別で自動引き当てするため
                    // 1 つのテンプレを全シリーズに使い回せる（映画クレジットの「サウンドトラック」役職など）。
                    // SERIES スコープ以外（EPISODE スコープ）／kind 未指定／該当商品なしのいずれかなら空文字。
                    // label=... を付けると、リンク先はそのまま表示の文字だけを差し替える（画面の表記が盤の商品名と違う作品用。
                    // label 内の {SERIES_TITLE} は ROLE_LINK の label と同じく作品の正式タイトルに置き換える）。
                    string kindOpt = ph.GetOption("kind", "");
                    if (string.IsNullOrEmpty(kindOpt)) return "";
                    if (ctx.ScopeSeriesId is not int seriesIdForProd || seriesIdForProd <= 0) return "";
                    string prodLabel = ph.GetOption("label", "");
                    if (prodLabel.Contains("{SERIES_TITLE}", StringComparison.Ordinal))
                        prodLabel = prodLabel.Replace("{SERIES_TITLE}", ctx.ScopeSeriesTitle ?? "", StringComparison.Ordinal);
                    var prodHtml = await lookup.LookupProductHtmlBySeriesAndKindAsync(seriesIdForProd, kindOpt, prodLabel).ConfigureAwait(false);
                    return prodHtml ?? "";
                }

            // ── 追加：楽曲スコープのプレースホルダ ──
            // {#THEME_SONGS}...{/THEME_SONGS} ループ内でのみ意味を持つ。currentSong が null の場合は空文字。
            case "SONG_TITLE":
                {
                    // 楽曲詳細ページへのリンク化済み HTML を返す。
                    // SongId が有効（>0）なら <a href="/songs/{id}/">タイトル</a>、無効ならエスケープしたタイトルのみ。
                    var title = currentSong?.SongTitle;
                    if (string.IsNullOrEmpty(title)) return "";
                    // クレジットの画面の曲名の表記（誤記ではない表記違い）があれば、文字だけその表記にする（リンク先は曲のまま）。
                    var safe = System.Net.WebUtility.HtmlEncode(ThemeSongsHandler.CreditedTitleOf(currentSong!));
                    // 画面に出た曲名の誤記があれば、取り消し線で出してから正しい曲名を続ける。
                    string misprintPrefix = ThemeSongsHandler.BuildTitleMisprintPrefixHtml(currentSong?.TitleMisprintText);
                    if (currentSong is { SongId: > 0 } cs)
                    {
                        return $"{misprintPrefix}<a href=\"/songs/{cs.SongId}/\">{safe}</a>";
                    }
                    return misprintPrefix + safe;
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
                // 会社名だけの行（「Nostalgic Orchestra」）には、実際の人物の括弧を添える。
                return NamesHtml(ph, currentSong?.LyricistHtml, currentSong?.LyricistParts)
                     + ThemeSongsHandler.CompanyOnlyPeopleSuffixHtml(currentSong, SongCreditRoles.Lyrics);
            case "COMPOSER":
                return NamesHtml(ph, currentSong?.ComposerHtml, currentSong?.ComposerParts)
                     + ThemeSongsHandler.CompanyOnlyPeopleSuffixHtml(currentSong, SongCreditRoles.Composition);
            case "ARRANGER":
                return NamesHtml(ph, currentSong?.ArrangerHtml, currentSong?.ArrangerParts)
                     + ThemeSongsHandler.CompanyOnlyPeopleSuffixHtml(currentSong, SongCreditRoles.Arrangement);
            case "SAME_COMPOSER_ARRANGER":
                // 作曲と編曲が同じ名義（連名も同じ）なら "1"。画面で「作曲：」「編曲：」の 2 行に名前を 1 つだけ出す作品用。
                return currentSong is { ComposerHtml.Length: > 0 } sameCs
                       && string.Equals(sameCs.ComposerHtml, sameCs.ArrangerHtml, StringComparison.Ordinal)
                    ? "1" : "";
            case "SAME_LYRICIST_COMPOSER_ARRANGER":
                // 作詞・作曲・編曲の 3 つが同じ名義（連名も同じ）なら "1"。画面で「作詞・作曲・編曲：」の 1 行にまとめる作品用。
                return currentSong is { LyricistHtml.Length: > 0 } sameAll
                       && string.Equals(sameAll.LyricistHtml, sameAll.ComposerHtml, StringComparison.Ordinal)
                       && string.Equals(sameAll.ComposerHtml, sameAll.ArrangerHtml, StringComparison.Ordinal)
                    ? "1" : "";
            case "LYRICS_TRANSLATOR":
                // 楽曲スコープ：英訳（LYRICS_TRANSLATION）の連名（リンク化済み HTML）。無い曲は空文字列で、
                // テンプレ側で {?LYRICS_TRANSLATOR}英訳詞：{LYRICS_TRANSLATOR}{/?LYRICS_TRANSLATOR} のように条件展開できる。
                return NamesHtml(ph, currentSong?.LyricsTranslatorHtml, currentSong?.LyricsTranslatorParts);
            case "INSTRUMENTAL":
                // その回（その映画）ではインストゥルメンタルで使った曲なら "1"。
                // 画面が曲名に「（インストゥルメンタル）」を添え、「作曲・編曲」を 1 行にして
                // 「オリジナルボーカル曲」の見出しの下に元の歌の歌手を出す作品用。
                return currentSong is { IsInstrumental: true } ? "1" : "";
            case "MEDLEY_ARRANGER":
                // 楽曲スコープ：メドレー編曲の連名（リンク化済み HTML）。無い曲は空文字列で、
                // テンプレ側で {?MEDLEY_ARRANGER}メドレー編曲:{MEDLEY_ARRANGER}{/?MEDLEY_ARRANGER} のように条件展開できる。
                return NamesHtml(ph, currentSong?.MedleyArrangerHtml, currentSong?.MedleyArrangerParts);
            case "SINGER":
                return NamesHtml(ph, currentSong?.SingerHtml, currentSong?.SingerParts);
            case "CHORUS":
                // 楽曲スコープ：BACKING_VOCALS 役職の連名（リンク化済み HTML）。
                // 該当録音にコーラスが居ない場合は空文字列を返すため、
                // テンプレ側で {?CHORUS}コーラス:{CHORUS}{/?CHORUS} のように条件展開できる。
                return NamesHtml(ph, currentSong?.ChorusHtml, currentSong?.ChorusParts);
            case "VARIANT_LABEL":
                return System.Net.WebUtility.HtmlEncode(currentSong?.VariantLabel ?? "");
            case "SONG_SERIES_TITLE":
                // 楽曲スコープ：曲の作品の正式タイトル。クレジットの作品と同じ作品の曲なら空文字なので、
                // オールスターズ映画の劇中歌のように、他作品の曲にだけ作品名の見出しを付けるときに
                // {?SONG_SERIES_TITLE}{SONG_SERIES_TITLE}<br>{/?SONG_SERIES_TITLE} のように書く。
                return System.Net.WebUtility.HtmlEncode(currentSong?.SongSeriesTitle ?? "");

            case "THEME_SONGS":
                {
                    int cols = 1;
                    if (int.TryParse(ph.GetOption("columns", "1"), out var n) && n >= 1) cols = n;
                    // kind オプションは "OP" / "ED" / "INSERT" / "OP+ED" / "ED+INSERT" /
                    string kindRaw = ph.GetOption("kind", "");
                    IReadOnlyList<string>? kinds = null;
                    if (!string.IsNullOrWhiteSpace(kindRaw) && !kindRaw.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        kinds = kindRaw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                       .Select(s => s.ToUpperInvariant())
                                       .ToArray();
                    }
                    // 役職の行の theme_song_seq があれば、その劇中順の範囲の曲だけを出す。
                    int? phSeqFrom = null, phSeqTo = null;
                    if (TryParseSeqRange(ctx.ThemeSongSeq ?? "", out int roleFrom, out int roleTo)) { phSeqFrom = roleFrom; phSeqTo = roleTo; }
                    return await ThemeSongsHandler.RenderAsync(factory, ctx.ScopeEpisodeId, ctx.ScopeSeriesId, kinds, cols, lookup, ct, phSeqFrom, phSeqTo).ConfigureAwait(false);
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
                        // HTML 版で取得。<a href="/people/{名前}/">名義</a> が返る。
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
                    // 末尾の敬称（「ちゃん」「くん」）は画面どおり小さく組む（TextEntryHtml）。
                    var escapedTexts = texts.Select(TextEntryHtml.Format);
                    return string.Join(sep, escapedTexts);
                }

            case "ROLE_LINK":
                {
                    // 役職統計ページへのリンク化済み HTML を返すプレースホルダ。
                    // テンプレ作者が役職ラベルをハードコード（例: <strong>漫画</strong>）するのではなく、
                    // {ROLE_LINK:code=MANGA} と書くことで「役職コードから表示名解決 + リンク化 + 太字ラップ」を
                    // DSL レンダラ側に一任できるようにする。SiteBuilder 側では
                    // <a href="/creators/roles/{role_code}/">表示名</a> を、Catalog 側プレビューでは
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
                        // label 内の `{SERIES_TITLE}` を ScopeSeriesTitle で置換する（テンプレ作者が
                        // シリーズ別カスタム見出しでリンク全体に「「タイトル」主題歌」を含めたいケース対応）。
                        // 入れ子展開は浅い置換に留め、他プレースホルダ（{SONG_TITLE} 等）は対象外。
                        if (label.Contains("{SERIES_TITLE}", StringComparison.Ordinal))
                        {
                            label = label.Replace("{SERIES_TITLE}", ctx.ScopeSeriesTitle ?? "", StringComparison.Ordinal);
                        }
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

    /// <summary><paramref name="content"/> を <paramref name="wrapSpec"/> で包む。 <paramref name="wrapSpec"/> は 0/1/2 文字を想定： 0 文字 → 包まない、1 文字 → 両端に同じ文字、2 文字 → 開き / 閉じ。</summary>
    private static string Wrap(string content, string wrapSpec)
    {
        if (string.IsNullOrEmpty(wrapSpec)) return content;
        if (wrapSpec.Length == 1) return wrapSpec + content + wrapSpec;
        return wrapSpec[0] + content + wrapSpec[1];
    }
}
