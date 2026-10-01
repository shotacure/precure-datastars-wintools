using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 音盤のブックレットに載る音楽クレジット（<c>music_credits</c>）の全件索引。
/// SiteDataLoader が起動時に 1 度だけ組み立て、以降は読み取り専用（並列レンダリングから引いても安全）。
/// 紐付け先（曲 / 録音 / 劇伴セッション / 商品）ごとの行と、名義（人物 / キャラ / 企業）ごとの行を引ける。
/// 本編クレジットの関与索引（<see cref="CreditInvolvementIndex"/>）には一切混ぜない。
/// 商品・劇伴セッションの見出し用に、商品と劇伴セッションの全件辞書も持つ。
/// </summary>
public sealed class MusicCreditIndex
{
    public static readonly MusicCreditIndex Empty = new(
        Array.Empty<MusicCredit>(), Array.Empty<Product>(), Array.Empty<BgmSession>(), Array.Empty<Disc>());

    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> BySong { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> ByRecording { get; }
    public IReadOnlyDictionary<(int SeriesId, byte SessionNo), IReadOnlyList<MusicCredit>> BySession { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<MusicCredit>> ByProduct { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> ByPersonAlias { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> ByCharacterAlias { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> ByCompanyAlias { get; }
    /// <summary>所属の屋号（affiliation_company_alias_id）→ その屋号を所属として添えた名義の行。企業詳細の「所属スタッフのクレジット」に使う。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<MusicCredit>> ByAffiliationCompanyAlias { get; }

    /// <summary>product_catalog_no → 商品（見出し・根拠の盤の表示用）。</summary>
    public IReadOnlyDictionary<string, Product> ProductByCatalogNo { get; }

    /// <summary>(series_id, session_no) → 劇伴セッション。</summary>
    public IReadOnlyDictionary<(int SeriesId, byte SessionNo), BgmSession> SessionByKey { get; }

    /// <summary>product_catalog_no → 作品（ディスクに登録されたシリーズのうち、ディスク番号が最小のもの）。音楽制作ページの作品数の集計に使う。</summary>
    public IReadOnlyDictionary<string, int> SeriesIdByProduct { get; }

    public int Count { get; }

    /// <summary>
    /// 音楽系ページ（音楽制作・歌唱・作詞作曲編曲や音楽の役職詳細）の基準点ラベル。
    /// クレジット確認済み（products.music_credits_checked）の盤のうち発売日が最も新しいもので
    /// 「yyyy年M月d日発売「商品名」時点の情報を表示しています」と組み立てる。確認済みの盤が無ければ空文字。
    /// </summary>
    public string CoverageLabel { get; }

    public MusicCreditIndex(IReadOnlyList<MusicCredit> rows, IReadOnlyList<Product> products, IReadOnlyList<BgmSession> sessions, IReadOnlyList<Disc> discs)
    {
        SeriesIdByProduct = discs
            .Where(d => d.SeriesId.HasValue)
            .GroupBy(d => d.ProductCatalogNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DiscNoInSet ?? 0).First().SeriesId!.Value, StringComparer.Ordinal);
        Count = rows.Count;
        var latestChecked = products
            .Where(p => p.MusicCreditsChecked)
            .OrderByDescending(p => p.ReleaseDate)
            .ThenBy(p => p.ProductCatalogNo, StringComparer.Ordinal)
            .FirstOrDefault();
        CoverageLabel = latestChecked is null
            ? ""
            : $"{latestChecked.ReleaseDate:yyyy年M月d日}発売「{latestChecked.Title}」時点の情報を表示しています";
        static IReadOnlyDictionary<TKey, IReadOnlyList<MusicCredit>> Group<TKey>(IEnumerable<MusicCredit> src, Func<MusicCredit, TKey> key)
            where TKey : notnull
            => src.GroupBy(key).ToDictionary(g => g.Key, g => (IReadOnlyList<MusicCredit>)g
                .OrderBy(r => r.CreditSeq).ThenBy(r => r.MusicCreditId).ToList());

        BySong = Group(rows.Where(r => r.TargetKind == MusicCreditTargetKinds.Song && r.SongId.HasValue), r => r.SongId!.Value);
        ByRecording = Group(rows.Where(r => r.TargetKind == MusicCreditTargetKinds.SongRecording && r.SongRecordingId.HasValue), r => r.SongRecordingId!.Value);
        BySession = Group(rows.Where(r => r.TargetKind == MusicCreditTargetKinds.BgmSession && r.BgmSeriesId.HasValue && r.BgmSessionNo.HasValue),
            r => (r.BgmSeriesId!.Value, r.BgmSessionNo!.Value));
        ByProduct = Group(rows.Where(r => r.TargetKind == MusicCreditTargetKinds.Product && r.ProductCatalogNo is not null), r => r.ProductCatalogNo!);
        ByPersonAlias = Group(rows.Where(r => r.PersonAliasId.HasValue), r => r.PersonAliasId!.Value);
        ByCharacterAlias = Group(rows.Where(r => r.CharacterAliasId.HasValue), r => r.CharacterAliasId!.Value);
        ByCompanyAlias = Group(rows.Where(r => r.CompanyAliasId.HasValue), r => r.CompanyAliasId!.Value);
        ByAffiliationCompanyAlias = Group(rows.Where(r => r.AffiliationCompanyAliasId.HasValue), r => r.AffiliationCompanyAliasId!.Value);
        ProductByCatalogNo = products.GroupBy(p => p.ProductCatalogNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        SessionByKey = sessions.ToDictionary(s => (s.SeriesId, s.SessionNo));
    }
}

/// <summary>
/// 人物・企業詳細の音楽クレジットの件数の数え方。歌は曲単位（録音へのリンクの印「#…」を外した曲の URL）、
/// 劇伴は録音（セッション）やシリーズの行単位（URL + 補足）、音盤は盤単位（商品の URL）で重複を除いて数える。
/// </summary>
public static class MusicCreditCounting
{
    public const string Song = "歌";
    public const string Bgm = "劇伴";
    public const string Disc = "音盤";

    /// <summary>紐付け先の種類（歌 / 劇伴 / 音盤）と行の URL・補足から、重複を除くためのキーを作る。</summary>
    public static string Key(string kind, string url, string sub) => kind switch
    {
        Song => url.IndexOf('#') is int i && i >= 0 ? url[..i] : url,
        Bgm => url + "|" + sub,
        _ => url
    };

    /// <summary>枠の開閉ボタンに出す件数（「10曲」「7件」「8枚」）。0 なら空文字。</summary>
    public static string CountLabel(string kind, int count) => count <= 0 ? "" : kind switch
    {
        Song => $"{count}曲",
        Bgm => $"{count}件",
        _ => $"{count}枚"
    };
}

/// <summary>音楽クレジットの表示用ビュー：区分 1 つ分（例「演奏・コーラス等」）。</summary>
public sealed class MusicCreditGroupView
{
    public string GroupCode { get; init; } = "";
    public string GroupLabel { get; init; } = "";
    public IReadOnlyList<MusicCreditLineView> Lines { get; init; } = Array.Empty<MusicCreditLineView>();
}

/// <summary>音楽クレジットの表示用ビュー：役職 1 行（役職名 + 名義の並び）。</summary>
public sealed class MusicCreditLineView
{
    public string RoleCode { get; init; } = "";
    /// <summary>役職名（役職マスタの名前。編成の注記があれば括弧で添える）。</summary>
    public string RoleLabel { get; init; } = "";
    /// <summary>盤の役職の印刷表記（役職名と違うときだけ。記録用で、表には出さない）。</summary>
    public string PrintedRoleLabel { get; init; } = "";
    /// <summary>名義の並び（リンク・区切り・所属を含む組み立て済み HTML）。</summary>
    public string NamesHtml { get; init; } = "";
}

/// <summary>音楽クレジットの表示用ビュー：根拠にした盤。</summary>
public sealed class MusicCreditSourceView
{
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>音楽クレジットの表示用ビュー：1 つの紐付け先（曲・録音・劇伴セッション・商品）の全クレジット。</summary>
public sealed class MusicCreditBlockView
{
    public IReadOnlyList<MusicCreditGroupView> Groups { get; init; } = Array.Empty<MusicCreditGroupView>();
    public IReadOnlyList<MusicCreditSourceView> Sources { get; init; } = Array.Empty<MusicCreditSourceView>();
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>
/// 音楽クレジットの行群を表示用ビューに組み立てるヘルパ（状態を持たないので並列レンダリングから呼んでよい）。
/// 区分（作詞・作曲・編曲 → 演奏・コーラス等 → レコーディング → 音盤製作）ごとに、役職を盤の並びでの初出順に並べ、
/// 同じ役職の名義は区切り（preceding_separator、無ければ「、」）でつなぐ。
/// 名義は名義マスタの表記で出す。盤の印刷表記（printed_text / role_label_text）と備考（notes）は記録用で、表には出さない。
/// 所属は、続く名義と同じ所属なら最後の名義の後ろにまとめて 1 回だけ括弧で出す（「川崎公敬、渡辺絵里奈（タバック）」）。
/// </summary>
public static class MusicCreditViewBuilder
{
    public static MusicCreditBlockView Build(BuildContext ctx, IEnumerable<MusicCredit> rows, IReadOnlySet<string>? onlyGroups = null)
    {
        var list = rows.OrderBy(r => r.CreditSeq).ThenBy(r => r.MusicCreditId).ToList();
        if (list.Count == 0) return new MusicCreditBlockView();

        var groups = new List<MusicCreditGroupView>();
        foreach (var group in MusicCreditGroups.All)
        {
            if (onlyGroups is not null && !onlyGroups.Contains(group)) continue;
            var inGroup = list.Where(r => GroupOf(ctx, r.RoleCode) == group).ToList();
            if (inGroup.Count == 0) continue;
            var lines = inGroup
                .GroupBy(r => r.RoleCode, StringComparer.Ordinal)
                .Select(g => BuildLine(ctx, g.Key, g.ToList()))
                .ToList();
            groups.Add(new MusicCreditGroupView
            {
                GroupCode = group,
                GroupLabel = MusicCreditGroups.Label(group),
                Lines = lines
            });
        }

        var sources = list
            .Select(r => r.SourceProductCatalogNo)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .Select(c => ctx.MusicCredits.ProductByCatalogNo.TryGetValue(c!, out var p) ? p : null)
            .Where(p => p is not null)
            .OrderBy(p => p!.ReleaseDate).ThenBy(p => p!.ProductCatalogNo, StringComparer.Ordinal)
            .Select(p => new MusicCreditSourceView { Title = p!.Title, Url = PathUtil.ProductUrl(p.ProductCatalogNo) })
            .ToList();

        return new MusicCreditBlockView { Groups = groups, Sources = sources };
    }

    /// <summary>役職の音楽クレジット区分。役職マスタに区分が無い役職は演奏・コーラス等として扱う。</summary>
    public static string GroupOf(BuildContext ctx, string roleCode)
        => ctx.RoleByCode.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.MusicCreditGroup)
            ? role.MusicCreditGroup!
            : MusicCreditGroups.Performance;

    /// <summary>
    /// 演奏系の役職（楽器・指揮など）のバッジで役職名の前に付ける絵文字。専用の絵文字が無い楽器は近いもので代用する
    /// （金管はトランペット、木管はフルート、ハープはヴァイオリン、ハーモニカは音符）。作詞・作曲・編曲・歌唱・レコーディング・音盤製作の役職には付けない。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RoleEmojiByCode = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SYNTH_OPERATION"] = "🎹",
        ["PROGRAMMING"] = "🎛️",
        ["KEYBOARD"] = "🎹",
        ["PIANO"] = "🎹",
        ["ORGAN"] = "🎹",
        ["GUITAR"] = "🎸",
        ["FOLK_GUITAR"] = "🎸",
        ["ELECTRIC_GUITAR"] = "🎸",
        ["BASS"] = "🎸",
        ["ELECTRIC_BASS"] = "🎸",
        ["DRUMS"] = "🥁",
        ["PERCUSSION"] = "🪘",
        ["LATIN_PERCUSSION"] = "🪇",
        ["TRUMPET"] = "🎺",
        ["TROMBONE"] = "🎺",
        ["BASS_TROMBONE"] = "🎺",
        ["HORN"] = "🎺",
        ["SAXOPHONE"] = "🎷",
        ["ALTO_SAX"] = "🎷",
        ["TENOR_SAX"] = "🎷",
        ["BARITONE_SAX"] = "🎷",
        ["FLUTE"] = "🪈",
        ["PICCOLO"] = "🪈",
        ["CLARINET"] = "🪈",
        ["OBOE"] = "🪈",
        ["BASSOON"] = "🪈",
        ["HARMONICA"] = "🎵",
        ["HARP"] = "🎻",
        ["VIOLIN"] = "🎻",
        ["STRINGS"] = "🎻",
        ["BAND"] = "🎶",
        ["CONDUCTOR"] = "🎼",
        ["SHOUT"] = "📢",
        ["HAND_CLAP"] = "👏",
        ["CHORUS_DIRECTION"] = "🎶",
    };

    /// <summary>バッジに出す役職名（演奏系の役職は絵文字 + 半角スペース + 役職名、それ以外は役職名）。</summary>
    public static string BadgeLabel(BuildContext ctx, string roleCode)
        => RoleEmojiByCode.TryGetValue(roleCode, out var emoji) ? $"{emoji} {RoleName(ctx, roleCode)}" : RoleName(ctx, roleCode);

    /// <summary>役職名（役職マスタの日本語名。無ければコード）。</summary>
    public static string RoleName(BuildContext ctx, string roleCode)
        => ctx.RoleByCode.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa) ? role.NameJa : roleCode;

    private static MusicCreditLineView BuildLine(BuildContext ctx, string roleCode, IReadOnlyList<MusicCredit> rows)
    {
        string roleName = RoleName(ctx, roleCode);
        string badgeName = BadgeLabel(ctx, roleCode);
        var notes = rows.Select(r => r.EnsembleNote).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        string label = notes.Count > 0 ? $"{badgeName}（{string.Join("、", notes)}）" : badgeName;
        string printedRole = rows.Select(r => r.RoleLabelText).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
        if (string.Equals(printedRole, roleName, StringComparison.Ordinal)) printedRole = "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(HtmlUtil.Escape(Separator(r.PrecedingSeparator)));
            sb.Append(NameHtml(ctx, r));
            string aff = AffiliationName(ctx, r);
            string nextAff = i + 1 < rows.Count ? AffiliationName(ctx, rows[i + 1]) : "";
            if (aff.Length > 0 && !string.Equals(aff, nextAff, StringComparison.Ordinal))
                sb.Append("<span class=\"music-credit-affiliation muted\">（").Append(AffiliationHtml(ctx, r, aff)).Append("）</span>");
        }
        return new MusicCreditLineView
        {
            RoleCode = roleCode,
            RoleLabel = label,
            PrintedRoleLabel = printedRole,
            NamesHtml = sb.ToString()
        };
    }

    /// <summary>
    /// 名義の区切りの表示。盤の表記が「／」「/」「、」や区切りなしのときは「 / 」にそろえる。
    /// 「&」「,」「 with 」のように盤の表記に意味のある区切りはそのまま出す。
    /// </summary>
    private static string Separator(string? printed)
        => string.IsNullOrWhiteSpace(printed) || printed.Trim() is "／" or "/" or "、" ? " / " : printed;

    /// <summary>名義 1 つ分の HTML（詳細ページへのリンク）。</summary>
    public static string NameHtml(BuildContext ctx, MusicCredit r)
    {
        string name;
        string url = "";
        switch (r.EntryKind)
        {
            case "PERSON" when r.PersonAliasId is int pa && ctx.PersonAliasById.TryGetValue(pa, out var alias):
                name = alias.DisplayTextOverride ?? alias.Name;
                if (PersonIdOf(ctx, pa) is int pid) url = PathUtil.PersonUrl(pid);
                break;
            case "CHARACTER" when r.CharacterAliasId is int ca && ctx.CharacterAliasById.TryGetValue(ca, out var calias):
                name = calias.Name;
                url = PathUtil.CharacterUrl(calias.CharacterId);
                break;
            case "COMPANY" when r.CompanyAliasId is int co && ctx.CompanyAliasById.TryGetValue(co, out var coalias):
                name = coalias.Name;
                if (coalias.CompanyId > 0) url = PathUtil.CompanyUrl(coalias.CompanyId);
                break;
            default:
                name = r.RawText ?? r.PrintedText ?? "";
                break;
        }
        string link = url.Length > 0
            ? $"<a class=\"staff-name\" href=\"{HtmlUtil.Escape(url)}\">{HtmlUtil.Escape(name)}</a>"
            : $"<span class=\"staff-name\">{HtmlUtil.Escape(name)}</span>";
        return r.EntryKind == "PERSON" && r.PersonAliasId is int aliasId ? link + PrimaryNameSuffixHtml(ctx, aliasId) : link;
    }

    /// <summary>
    /// 所属の表示。企業マスタの屋号に紐付いていれば企業詳細へのリンク（hover 限定下線の staff-name）、
    /// 自由記述の所属は文字のみ。
    /// </summary>
    private static string AffiliationHtml(BuildContext ctx, MusicCredit r, string aff)
    {
        if (r.AffiliationCompanyAliasId is int a && ctx.CompanyAliasById.TryGetValue(a, out var ca) && ca.CompanyId > 0)
            return $"<a class=\"staff-name\" href=\"{HtmlUtil.Escape(PathUtil.CompanyUrl(ca.CompanyId))}\">{HtmlUtil.Escape(aff)}</a>";
        return HtmlUtil.Escape(aff);
    }

    private static string AffiliationName(BuildContext ctx, MusicCredit r)
    {
        if (r.AffiliationCompanyAliasId is int a && ctx.CompanyAliasById.TryGetValue(a, out var ca)) return ca.Name ?? "";
        return r.AffiliationText ?? "";
    }

    /// <summary>
    /// 音楽クレジット 1 行の紐付け先の見出し（曲名・シリーズ名・商品名）、リンク先、補足（劇伴セッション名など）、並べ替え用の日付。
    /// 日付は商品なら発売日、それ以外は根拠の盤の発売日（無ければ DateTime.MaxValue）。解決できない行は null。
    /// </summary>
    public static (string Title, string Url, string Sub, DateTime Sort)? DescribeTarget(BuildContext ctx, MusicCredit r)
    {
        DateTime sort = r.SourceProductCatalogNo is string src && ctx.MusicCredits.ProductByCatalogNo.TryGetValue(src, out var sp)
            ? sp.ReleaseDate : DateTime.MaxValue;
        switch (r.TargetKind)
        {
            case MusicCreditTargetKinds.Song when r.SongId is int sid && ctx.SongById.TryGetValue(sid, out var song):
                return (song.Title, PathUtil.SongUrl(sid), "", sort);
            case MusicCreditTargetKinds.SongRecording when r.SongRecordingId is int rid && ctx.SongRecordingById.TryGetValue(rid, out var rec)
                                                           && ctx.SongById.TryGetValue(rec.SongId, out var rsong):
                return (SongDisplayTitle.Build(rsong.Title, rec.VariantLabel),
                    ctx.SongRecordingAnchorUrlById.TryGetValue(rid, out var anchor) ? anchor : PathUtil.SongUrl(rec.SongId), "", sort);
            case MusicCreditTargetKinds.BgmSession when r.BgmSeriesId is int bsid && ctx.SeriesById.TryGetValue(bsid, out var series):
                string sub = ctx.MusicCredits.SessionByKey.TryGetValue((bsid, r.BgmSessionNo ?? 0), out var session)
                    ? $"劇伴 {session.SessionName}" : "劇伴";
                return (series.Title, PathUtil.BgmsForSeriesUrl(series.Slug), sub, sort);
            case MusicCreditTargetKinds.Product when r.ProductCatalogNo is string pc && ctx.MusicCredits.ProductByCatalogNo.TryGetValue(pc, out var product):
                return (product.Title, PathUtil.ProductUrl(pc), "", product.ReleaseDate < sort ? product.ReleaseDate : sort);
            default:
                return null;
        }
    }

    /// <summary>
    /// 別名義でのクレジットに、人物の本名義（人物詳細の見出しと同じ名義）を括弧で添える HTML。
    /// 例：「仲弓 香乃」→「（ゆかな）」。名義が本名義と同じ（空白の違いを除く）なら空文字。
    /// </summary>
    public static string PrimaryNameSuffixHtml(BuildContext ctx, int personAliasId)
    {
        if (!ctx.PersonAliasById.TryGetValue(personAliasId, out var alias)) return "";
        if (PersonIdOf(ctx, personAliasId) is not int pid) return "";
        string? primary = ctx.EntityUrls.PersonDisplayName(pid);
        if (string.IsNullOrEmpty(primary)) return "";
        static string Norm(string t) => t.Replace(" ", "").Replace("　", "");
        if (Norm(primary) == Norm(alias.DisplayTextOverride ?? alias.Name)) return "";
        return $"<span class=\"staff-primary-name muted\">（{HtmlUtil.Escape(primary)}）</span>";
    }

    /// <summary>名義 → 人物 ID（共同名義は先頭の人物）。</summary>
    public static int? PersonIdOf(BuildContext ctx, int personAliasId)
        => ctx.PersonIdByAlias.TryGetValue(personAliasId, out var pid) ? pid : null;
}

/// <summary>
/// 音楽クレジットのブロック（<see cref="MusicCreditBlockView"/>）を HTML に書き出すヘルパ。
/// 曲・録音・劇伴セッション・商品の各ページで同じ見た目にするため、テンプレではなくここで 1 か所に組み立てる。
/// 区分ごとの小見出しは立てず、役職ごとのユニット（役職バッジ + 名前）を横に流して並べる。
/// 根拠にした盤は末尾に「出典：」として添える。
/// </summary>
public static class MusicCreditHtml
{
    /// <summary>
    /// 役職ごとのユニット（役職バッジ + 名前の並び）を横に流して書き出す（外枠 .staff-badges-row 込み）。
    /// 役職ごとに改行して縦に伸びないよう、ユニットを行内で続けて並べ、ユニットの中では改行しない（.music-credit-unit）。
    /// 区分の見出しは立てず、区分の順（作詞・作曲・編曲 → 演奏・コーラス等 → レコーディング → 音盤製作）に並べる。
    /// </summary>
    public static string RenderUnits(MusicCreditBlockView block)
    {
        if (block.IsEmpty) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"staff-badges-row music-credit-row\">");
        foreach (var g in block.Groups)
        {
            foreach (var line in g.Lines)
            {
                sb.Append("<span class=\"staff-badge-group music-credit-unit\">")
                  .Append("<span class=\"role-badge role-badge-sm\" data-role-code=\"").Append(HtmlUtil.Escape(line.RoleCode)).Append("\">")
                  .Append(HtmlUtil.Escape(line.RoleLabel)).Append("</span>")
                  .Append(line.NamesHtml)
                  .Append("</span>");
            }
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>根拠にした盤の「出典：」段落（無ければ空文字）。</summary>
    public static string RenderSources(MusicCreditBlockView block)
    {
        if (block.Sources.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("<p class=\"muted music-credit-sources\">出典：");
        for (int i = 0; i < block.Sources.Count; i++)
        {
            if (i > 0) sb.Append('、');
            sb.Append("<a href=\"").Append(HtmlUtil.Escape(block.Sources[i].Url)).Append("\">")
              .Append(HtmlUtil.Escape(block.Sources[i].Title)).Append("</a>");
        }
        sb.Append("</p>");
        return sb.ToString();
    }

    /// <summary>全役職のユニットを横に流し、末尾に出典を添える。</summary>
    public static string Render(MusicCreditBlockView block)
    {
        if (block.IsEmpty) return "";
        return "<div class=\"music-credit-block\">" + RenderUnits(block) + RenderSources(block) + "</div>";
    }
}
