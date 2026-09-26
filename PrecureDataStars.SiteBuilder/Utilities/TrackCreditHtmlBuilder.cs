using System.Net;
using System.Text;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 商品詳細・楽曲詳細など複数のジェネレータから共通利用する「トラック／録音／劇伴の役職クレジット
/// HTML 組立」ヘルパー。
/// <para>
/// <c>song_credits</c> から指定役職の連名 HTML、<c>song_recording_singers</c> から歌唱者連名 HTML、
/// <c>bgm_cue_credits</c> から役職別バッジ + 名義の HTML を組む処理を 1 箇所に集約し、
/// 商品詳細トラックリストと楽曲詳細の歌セクションで完全に同じ表記を出す。
/// </para>
/// <para>
/// 役職バッジは <c>.role-badge[data-role-code="LYRICS|COMPOSITION|ARRANGEMENT|VOCALS|SERIES|…"]</c>
/// 規約に合わせて出す。バッジのラベル文字列は <c>roles</c> マスタ（<see cref="Role.NameJa"/>）を
/// 優先採用し、マスタ未登録の場合のみ呼び出し側が指定するフォールバック文字列に落ちる。
/// </para>
/// </summary>
/// <remarks>
/// 設計メモ：
/// <list type="bullet">
///   <item>マスタ系（<see cref="PersonAlias"/> / <see cref="CharacterAlias"/> / <see cref="Role"/>）は
///     呼び出し側で事前一括ロードしてコンストラクタへ渡す。人物リンクは他ページと同じ
///     <see cref="StaffNameLinkResolver"/>、歌唱者連名は <see cref="SingerHtmlBuilder"/> で組み、
///     サイト全体で同じ表記・同じリンク規則になるようにする。</item>
///   <item>取引データ系（<c>song_credits</c> / <c>song_recording_singers</c> / <c>bgm_cue_credits</c>）は
///     SiteDataLoader が起動時に全件取得して <see cref="Pipeline.BuildContext"/> 経由で共有する辞書を
///     コンストラクタに直接受け取り、本クラスは DB アクセスを一切持たない純粋な同期 HTML 組立器として動く。
///     旧版は per-key で Repository 呼び出しを発火していたため、商品 1 件 × 数十トラック分の DB 往復が
///     生成のボトルネックになっていたが、本構成では辞書 lookup のみで完結する。</item>
///   <item>HTML エスケープは本クラス内で全て行う（呼び出し側は組み立て済み HTML を受け取って
///     そのまま流す前提）。</item>
/// </list>
/// </remarks>
public sealed class TrackCreditHtmlBuilder
{
    private readonly IReadOnlyDictionary<int, PersonAlias> _personAliasMap;
    private readonly IReadOnlyDictionary<int, CharacterAlias> _characterAliasMap;
    /// <summary>名義 → 人物詳細リンクの解決（共有名義の添字付き複数リンクを含め、他ページと同じ規則）。</summary>
    private readonly StaffNameLinkResolver _staffLinkResolver;
    /// <summary>歌唱者・コーラスの連名 HTML（楽曲詳細・主題歌欄と同じ書式）。</summary>
    private readonly SingerHtmlBuilder _singerHtml;
    private readonly IReadOnlyDictionary<string, Role> _roleMap;

    private readonly IReadOnlyDictionary<int, IReadOnlyList<SongCredit>> _songCreditsBySong;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SongRecordingSinger>> _singersByRecording;
    private readonly IReadOnlyDictionary<(int SeriesId, string MNoDetail), IReadOnlyList<BgmCueCredit>> _bgmCueCreditsByCue;

    /// <summary>
    /// ヘルパーを構築する。事前ロード済みのマスタマップと、SiteDataLoader が <see cref="Pipeline.BuildContext"/>
    /// 経由で共有する取引データ辞書（<c>song_credits</c> / <c>song_recording_singers</c> /
    /// <c>bgm_cue_credits</c> を ID 単位でグルーピング済み）を受け取る。
    /// </summary>
    public TrackCreditHtmlBuilder(
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap,
        StaffNameLinkResolver staffLinkResolver,
        SingerHtmlBuilder singerHtml,
        IReadOnlyDictionary<string, Role> roleMap,
        IReadOnlyDictionary<int, IReadOnlyList<SongCredit>> songCreditsBySong,
        IReadOnlyDictionary<int, IReadOnlyList<SongRecordingSinger>> singersByRecording,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), IReadOnlyList<BgmCueCredit>> bgmCueCreditsByCue)
    {
        _personAliasMap = personAliasMap ?? throw new ArgumentNullException(nameof(personAliasMap));
        _characterAliasMap = characterAliasMap ?? throw new ArgumentNullException(nameof(characterAliasMap));
        _staffLinkResolver = staffLinkResolver ?? throw new ArgumentNullException(nameof(staffLinkResolver));
        _singerHtml = singerHtml ?? throw new ArgumentNullException(nameof(singerHtml));
        _roleMap = roleMap ?? throw new ArgumentNullException(nameof(roleMap));
        _songCreditsBySong = songCreditsBySong ?? throw new ArgumentNullException(nameof(songCreditsBySong));
        _singersByRecording = singersByRecording ?? throw new ArgumentNullException(nameof(singersByRecording));
        _bgmCueCreditsByCue = bgmCueCreditsByCue ?? throw new ArgumentNullException(nameof(bgmCueCreditsByCue));
    }

    /// <summary>HTML エスケープ（&amp;, &lt;, &gt;, "" など）。 ヘルパーが組み立てる HTML は全て本メソッドを通したテキストを使う。</summary>
    public static string Escape(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>
    /// 役職コード + 表示ラベルから「役職バッジ」の HTML を返す。
    /// 役職マスタに登録があれば <see cref="Role.NameJa"/> を採用、無ければ <paramref name="fallbackLabel"/>。
    /// CSS は既存の <c>.role-badge[data-role-code]</c> 規約に合わせる（色相環 4 色 +
    /// SERIES グレー等の既存マッピングがそのまま効く）。
    /// </summary>
    public string BuildRoleBadgeHtml(string roleCode, string fallbackLabel)
    {
        string label = (_roleMap.TryGetValue(roleCode, out var r) && !string.IsNullOrEmpty(r.NameJa))
            ? r.NameJa : fallbackLabel;
        return $"<span class=\"role-badge role-badge-sm\" data-role-code=\"{Escape(roleCode)}\">{Escape(label)}</span>";
    }

    /// <summary>
    /// 「役職バッジ + 名義 HTML」の塊（クレジットセグメント）を組み立てる。テンプレ側は本セグメントを
    /// 1 単位として横並びで連結すれば、商品詳細・楽曲詳細の双方で同じ意匠の行が出来上がる。
    /// </summary>
    public string BuildCreditSegmentHtml(string roleCode, string fallbackLabel, string namesHtml)
    {
        return "<span class=\"track-credit-segment\">"
             + BuildRoleBadgeHtml(roleCode, fallbackLabel)
             + $"<span class=\"track-credit-names\">{namesHtml}</span>"
             + "</span>";
    }

    /// <summary>
    /// 複数役職のクレジットを「名義 HTML 文字列レベルでの隣接マージ」付きで連結する。
    /// <para>
    /// 入力は (役職コード, フォールバックラベル, 名義 HTML) の順序付きタプル列。空文字の名義は無視。
    /// 隣り合うエントリの名義 HTML が完全一致する場合、それらを 1 つのセグメントに統合し、
    /// バッジを横並びにして名義を 1 回だけ出す。
    /// </para>
    /// <para>
    /// 用途例：歌の作詞・作曲・編曲・歌セグメントで「作曲=EFFY、編曲=EFFY」のように
    /// 同名義が連続するケースを <c>[作曲][編曲] EFFY</c> と整理する。
    /// 構造化クレジット由来（リンク付き <c>&lt;a&gt;</c>）同士でも、生成 HTML が完全一致するなら
    /// 同じ仕組みで自動的にマージされる（リンク先 person_id が同じなら HTML 文字列も同一になる）。
    /// 構造化由来 と フリーテキストフォールバック が混在する場合は HTML 文字列が異なる
    /// （リンクの有無の差）ため、意図的にマージしない。
    /// </para>
    /// </summary>
    public string BuildMergedRoleSegmentsHtml(
        IReadOnlyList<(string RoleCode, string FallbackLabel, string NamesHtml)> entries)
    {
        // 空文字エントリを除外（=その役職に名義が無いケース）。
        var nonEmpty = entries.Where(e => !string.IsNullOrEmpty(e.NamesHtml)).ToList();
        if (nonEmpty.Count == 0) return "";

        // 隣接マージ：直前マージグループの名義 HTML が完全一致するエントリを統合する。
        var merged = new List<(List<(string RoleCode, string FallbackLabel)> Roles, string NamesHtml)>();
        foreach (var e in nonEmpty)
        {
            if (merged.Count > 0 && string.Equals(merged[^1].NamesHtml, e.NamesHtml, StringComparison.Ordinal))
            {
                merged[^1].Roles.Add((e.RoleCode, e.FallbackLabel));
                continue;
            }
            merged.Add((new List<(string, string)> { (e.RoleCode, e.FallbackLabel) }, e.NamesHtml));
        }

        // HTML へ変換。
        var sb = new StringBuilder();
        foreach (var mg in merged)
        {
            sb.Append("<span class=\"track-credit-segment\">");
            foreach (var (rc, fb) in mg.Roles)
            {
                sb.Append(BuildRoleBadgeHtml(rc, fb));
            }
            sb.Append("<span class=\"track-credit-names\">").Append(mg.NamesHtml).Append("</span>");
            sb.Append("</span>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 人物名義 <c>alias_id</c> から「表示名」を解決する。
    /// 優先順位：<see cref="PersonAlias.DisplayTextOverride"/>（明示的に手動指定された表示文字列）→
    /// <see cref="PersonAlias.Name"/>。マスタ未登録 alias は「(名義不明)」を返す。
    /// </summary>
    public string ResolvePersonAliasDisplayLabel(int personAliasId)
    {
        if (!_personAliasMap.TryGetValue(personAliasId, out var alias))
            return "(名義不明)";
        if (!string.IsNullOrEmpty(alias.DisplayTextOverride)) return alias.DisplayTextOverride!;
        if (!string.IsNullOrEmpty(alias.Name)) return alias.Name;
        return "(名義不明)";
    }

    /// <summary>
    /// 人物名義 <c>alias_id</c> から人物詳細ページへのリンク HTML を返す。
    /// リンク化は <see cref="StaffNameLinkResolver"/> に委ね、他ページと同じ規則
    /// （1 人物なら単一リンク、共有名義なら添字付き複数リンク、人物に紐付かない名義は平文）で出す。
    /// </summary>
    public string BuildPersonAliasLinkHtml(int personAliasId)
        => _staffLinkResolver.ResolveAsHtml(personAliasId, ResolvePersonAliasDisplayLabel(personAliasId));

    /// <summary>
    /// 歌の構造化クレジット（<c>song_credits</c>）から、指定役職の連名 HTML を組み立てる。
    /// 連名間の区切りは <see cref="SongCredit.PrecedingSeparator"/> を尊重（既定 "、"）。
    /// <para>
    /// 構造化クレジット行が 1 件も無い場合は <see cref="Song"/> のフリーテキストフィールド
    /// （<see cref="Song.LyricistName"/> / <see cref="Song.ComposerName"/> / <see cref="Song.ArrangerName"/>）
    /// を役職コードに応じて取り出し、リンクなしの平文（HTML エスケープのみ）でフォールバック表示する。
    /// これにより、構造化クレジット未整備の歌でも作詞・作曲・編曲の名義は読めるようにする
    /// （実データには古い import 由来でフリーテキストだけ入っている楽曲が多数存在するため）。
    /// 構造化・フリーテキストともに空ならば空文字を返す（呼び出し側はセグメント自体を出さない判定に使える）。
    /// </para>
    /// </summary>
    public string BuildSongCreditNamesHtml(Song song, string roleCode)
    {
        // 事前展開済み辞書から (曲, 役職) で絞り込む。SongCreditsBySong は LYRICS → COMPOSITION →
        // ARRANGEMENT → その他 role_code 昇順で並んでいるため、ここで CreditRole 一致を線形フィルタしてから
        // CreditSeq 昇順を保つ並びになる。
        var credits = _songCreditsBySong.TryGetValue(song.SongId, out var byRole)
            ? byRole.Where(c => string.Equals(c.CreditRole, roleCode, StringComparison.Ordinal)).ToList()
            : new List<SongCredit>();
        if (credits.Count > 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < credits.Count; i++)
            {
                var c = credits[i];
                if (i > 0)
                {
                    string sep = string.IsNullOrEmpty(c.PrecedingSeparator) ? "、" : c.PrecedingSeparator!;
                    sb.Append(Escape(sep));
                }
                sb.Append(BuildPersonAliasLinkHtml(c.PersonAliasId));
            }
            return sb.ToString();
        }

        // フォールバック：構造化クレジットが無いので、Song のフリーテキストを役職コードに応じて拾う。
        // 表示はリンクなしの平文（HTML エスケープのみ）。「、」区切りの連名がフリーテキストに含まれていても
        // 1 文字列として出すだけ（構造化された連名分解はマスタ整備が先）。
        string? freeText = roleCode switch
        {
            "LYRICS" => song.LyricistName,
            "COMPOSITION" => song.ComposerName,
            "ARRANGEMENT" => song.ArrangerName,
            _ => null
        };
        return string.IsNullOrEmpty(freeText) ? "" : Escape(freeText);
    }

    /// <summary>
    /// 録音の歌唱者（<c>song_recording_singers</c> の VOCALS 役）から名義 HTML を組み立てる。
    /// 書式は <see cref="SingerHtmlBuilder.BuildVocalistsHtml"/> と同一（キャラ歌唱は「キャラ(CV:声優)」、
    /// スラッシュ並列、<c>affiliation_text</c> の併記を含む）。該当行が無ければ
    /// <see cref="SongRecording.SingerName"/> のフリーテキストを HTML エスケープして返す。
    /// </summary>
    public string BuildRecordingVocalistsHtml(SongRecording rec)
        => _singerHtml.BuildVocalistsHtml(SingersOf(rec), rec.SingerName, _personAliasMap, _characterAliasMap);

    /// <summary>録音のコーラス（BACKING_VOCALS 役）連名 HTML を組み立てる（書式は <see cref="SingerHtmlBuilder.BuildChorusHtml"/> と同一）。 該当行が無ければ空文字列（VOCALS と違いフリーテキストフォールバックは持たない）。</summary>
    public string BuildRecordingChorusHtml(SongRecording rec)
        => _singerHtml.BuildChorusHtml(SingersOf(rec), _personAliasMap, _characterAliasMap);

    private IReadOnlyList<SongRecordingSinger> SingersOf(SongRecording rec)
        => _singersByRecording.TryGetValue(rec.SongRecordingId, out var list) ? list : Array.Empty<SongRecordingSinger>();

    /// <summary>
    /// 劇伴クレジット（<c>bgm_cue_credits</c>）から「役職バッジ + 名義」セグメントの列 HTML を組み立てる。
    /// 役職並び順は SiteDataLoader が事前展開した辞書の並びを尊重（COMPOSITION → ARRANGEMENT →
    /// その他 role_code 昇順、同役内は credit_seq 昇順）。
    /// 同一役職内の連名は <see cref="BgmCueCredit.PrecedingSeparator"/> 尊重で連結する。
    /// <para>
    /// 隣り合う役職グループで「連名 <c>person_alias_id</c> の列が順序通り完全一致」する場合は、
    /// 1 つのセグメントに集約してバッジを横並びにし、名義は 1 回だけ出す。
    /// 例：作曲・編曲ともに「佐藤 直紀」のみ → <c>[作曲][編曲] 佐藤 直紀</c>。
    /// 例：作曲・編曲ともに「佐藤 直紀、菅野 祐悟」（連名同順） → <c>[作曲][編曲] 佐藤 直紀、菅野 祐悟</c>。
    /// 単独同一のケースもこの一般則に内包される。これにより劇伴の同一スタッフによる作曲・編曲が
    /// 単純な視覚ノイズで重複表記されないよう整理する。
    /// </para>
    /// 該当 cue にクレジットが無ければ空文字を返す。
    /// </summary>
    public string BuildBgmCueCreditsSegmentsHtml(int seriesId, string mNoDetail)
    {
        if (!_bgmCueCreditsByCue.TryGetValue((seriesId, mNoDetail), out var credits)
            || credits.Count == 0)
        {
            return "";
        }

        // ステップ 1：役職コードごとに連名グループを作る（辞書の並びを尊重して単純走査でグループ化）。
        var groups = new List<(string RoleCode, List<BgmCueCredit> Items)>();
        foreach (var c in credits)
        {
            if (groups.Count == 0 || !string.Equals(groups[^1].RoleCode, c.CreditRole, StringComparison.Ordinal))
            {
                groups.Add((c.CreditRole, new List<BgmCueCredit>()));
            }
            groups[^1].Items.Add(c);
        }

        // ステップ 2：隣り合うグループで連名 person_alias_id の列が順序通り完全一致するなら統合する。
        // 統合グループは複数の役職コードを並べてバッジを出し、名義は 1 回だけ出力する。
        var merged = new List<(List<string> RoleCodes, List<BgmCueCredit> Items)>();
        foreach (var g in groups)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.Items.Count == g.Items.Count
                    && last.Items.Zip(g.Items, (a, b) => a.PersonAliasId == b.PersonAliasId).All(x => x))
                {
                    last.RoleCodes.Add(g.RoleCode);
                    continue;
                }
            }
            merged.Add((new List<string> { g.RoleCode }, g.Items));
        }

        // ステップ 3：各マージグループを HTML セグメントへ変換。バッジは役職コードの数だけ並べ、
        // 名義は連名 preceding_separator 尊重で 1 回だけ出す。
        var segments = new List<string>();
        foreach (var mg in merged)
        {
            var badgesSb = new StringBuilder();
            foreach (var rc in mg.RoleCodes)
            {
                string fallback = rc switch
                {
                    "COMPOSITION" => "作曲",
                    "ARRANGEMENT" => "編曲",
                    _ => rc
                };
                badgesSb.Append(BuildRoleBadgeHtml(rc, fallback));
            }
            var nameSb = new StringBuilder();
            for (int i = 0; i < mg.Items.Count; i++)
            {
                var c = mg.Items[i];
                if (i > 0)
                {
                    string sep = string.IsNullOrEmpty(c.PrecedingSeparator) ? "、" : c.PrecedingSeparator!;
                    nameSb.Append(Escape(sep));
                }
                nameSb.Append(BuildPersonAliasLinkHtml(c.PersonAliasId));
            }
            segments.Add(
                "<span class=\"track-credit-segment\">"
                + badgesSb
                + $"<span class=\"track-credit-names\">{nameSb}</span>"
                + "</span>");
        }
        return string.Join("", segments);
    }
}
