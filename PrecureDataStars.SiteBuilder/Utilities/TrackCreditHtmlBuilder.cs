using System.Net;
using System.Text;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

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
///   <item>マスタ系（<see cref="PersonAlias"/> / <see cref="CharacterAlias"/> / <see cref="Role"/> /
///     name_alias → person_id の lookup）は呼び出し側で事前一括ロードしてコンストラクタへ渡す
///     （Generator 全体で 1 度だけロードしたいため）。</item>
///   <item>取引データ系（<c>song_credits</c> / <c>song_recording_singers</c> / <c>bgm_cue_credits</c>）は
///     対象 ID が事前に絞り込めないため、本クラス内のメソッドが必要なときにリポジトリ経由で
///     都度クエリする。1 トラックあたり数クエリ発生するが、商品詳細の総トラック数（1 商品 20〜30 程度）
///     の規模では実用上問題ない。</item>
///   <item>HTML エスケープは本クラス内で全て行う（呼び出し側は組み立て済み HTML を受け取って
///     そのまま流す前提）。</item>
/// </list>
/// </remarks>
public sealed class TrackCreditHtmlBuilder
{
    private readonly IReadOnlyDictionary<int, PersonAlias> _personAliasMap;
    private readonly IReadOnlyDictionary<int, CharacterAlias> _characterAliasMap;
    /// <summary>
    /// alias_id → person_id の lookup（<c>person_alias_persons</c> 中間テーブルを事前ロードして
    /// 作る）。共同名義（1 alias に複数 person）の場合は <c>person_seq</c> が最も小さい人物を採用する
    /// （個人名義よりは稀なケース）。alias が <c>person_alias_persons</c> に登録されていない（マスタ
    /// 整備の不備）場合は -1 を返す扱いとし、リンク URL は組み立てつつテキストだけ赤太字等で
    /// 警告する余地を残す。
    /// </summary>
    private readonly IReadOnlyDictionary<int, int> _personIdByAliasId;
    private readonly IReadOnlyDictionary<string, Role> _roleMap;

    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly BgmCueCreditsRepository _bgmCueCreditsRepo;

    /// <summary>
    /// ヘルパーを構築する。事前ロード済みのマスタマップとリポジトリを受け取る。
    /// </summary>
    public TrackCreditHtmlBuilder(
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap,
        IReadOnlyDictionary<int, int> personIdByAliasId,
        IReadOnlyDictionary<string, Role> roleMap,
        SongCreditsRepository songCreditsRepo,
        SongRecordingSingersRepository songRecordingSingersRepo,
        BgmCueCreditsRepository bgmCueCreditsRepo)
    {
        _personAliasMap = personAliasMap ?? throw new ArgumentNullException(nameof(personAliasMap));
        _characterAliasMap = characterAliasMap ?? throw new ArgumentNullException(nameof(characterAliasMap));
        _personIdByAliasId = personIdByAliasId ?? throw new ArgumentNullException(nameof(personIdByAliasId));
        _roleMap = roleMap ?? throw new ArgumentNullException(nameof(roleMap));
        _songCreditsRepo = songCreditsRepo ?? throw new ArgumentNullException(nameof(songCreditsRepo));
        _songRecordingSingersRepo = songRecordingSingersRepo ?? throw new ArgumentNullException(nameof(songRecordingSingersRepo));
        _bgmCueCreditsRepo = bgmCueCreditsRepo ?? throw new ArgumentNullException(nameof(bgmCueCreditsRepo));
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
    /// <c>person_alias_persons</c> 中間テーブル経由で person_id を解決し、解決できれば
    /// <c>/persons/{personId}/</c> へのアンカー、解決できなければアンカーを張らずプレーンテキストで返す
    /// （マスタ整備の途上でも表示崩れしない設計）。
    /// </summary>
    public string BuildPersonAliasLinkHtml(int personAliasId)
    {
        string label = ResolvePersonAliasDisplayLabel(personAliasId);
        if (_personIdByAliasId.TryGetValue(personAliasId, out var personId) && personId > 0)
        {
            string href = $"/persons/{personId}/";
            return $"<a href=\"{Escape(href)}\">{Escape(label)}</a>";
        }
        return Escape(label);
    }

    /// <summary>
    /// キャラクター名義 <c>character_alias_id</c> からキャラクター詳細ページへのリンク HTML を返す。
    /// 表示文字列は <see cref="CharacterAlias.Name"/>（CharacterAlias には DisplayTextOverride 相当の
    /// フィールドが無いため Name 一本）。
    /// </summary>
    public string BuildCharacterAliasLinkHtml(int characterAliasId)
    {
        if (!_characterAliasMap.TryGetValue(characterAliasId, out var alias))
            return "(キャラ不明)";
        string label = string.IsNullOrEmpty(alias.Name) ? "(キャラ不明)" : alias.Name;
        string href = $"/characters/{alias.CharacterId}/";
        return $"<a href=\"{Escape(href)}\">{Escape(label)}</a>";
    }

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
    public async Task<string> BuildSongCreditNamesHtmlAsync(Song song, string roleCode, CancellationToken ct)
    {
        var credits = await _songCreditsRepo.GetBySongAndRoleAsync(song.SongId, roleCode, ct).ConfigureAwait(false);
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
    /// 録音の歌唱者（<c>song_recording_singers</c>）から名義 HTML を組み立てる。
    /// 役職コード VOCALS のみを対象、<c>singer_seq</c> 順に走査。
    /// 1 行の表示は <see cref="SongRecordingSinger.BillingKind"/> によって以下のように出し分ける：
    /// <list type="bullet">
    ///   <item><b>Person</b>：人物名義のリンクのみ。</item>
    ///   <item><b>CharacterWithCv</b>：キャラ名リンク + 「（CV: 人物名リンク）」の連結。
    ///     <c>character_alias_id</c> と <c>voice_person_alias_id</c> を解決して使う。</item>
    /// </list>
    /// スラッシュ表記（<c>slash_person_alias_id</c> / <c>slash_character_alias_id</c>）は本ステージでは
    /// 簡略化のため未対応（解決時の追加表示はしないが、本体名義は正しく出る）。将来要件として残置。
    /// 連名間の区切りは <see cref="SongRecordingSinger.PrecedingSeparator"/> を尊重（既定 "、"）。
    /// 該当行が無ければ <see cref="SongRecording.SingerName"/> のフリーテキストを HTML エスケープして返す
    /// （最終フォールバック：マスタ未整備でも表示崩れしないようにする）。
    /// </summary>
    public async Task<string> BuildRecordingVocalistsHtmlAsync(SongRecording rec, CancellationToken ct)
    {
        var singers = await _songRecordingSingersRepo.GetByRecordingAndRoleAsync(rec.SongRecordingId, "VOCALS", ct).ConfigureAwait(false);
        if (singers.Count == 0)
        {
            return string.IsNullOrEmpty(rec.SingerName) ? "" : Escape(rec.SingerName!);
        }
        var sb = new StringBuilder();
        for (int i = 0; i < singers.Count; i++)
        {
            var s = singers[i];
            if (i > 0)
            {
                string sep = string.IsNullOrEmpty(s.PrecedingSeparator) ? "、" : s.PrecedingSeparator!;
                sb.Append(Escape(sep));
            }
            // BillingKind が CharacterWithCv で character_alias と voice_person_alias の両方が
            // 揃っているケースを優先判定（揃わない場合は素の Person 扱いに落ちる）。
            if (s.BillingKind == SingerBillingKind.CharacterWithCv
                && s.CharacterAliasId.HasValue
                && s.VoicePersonAliasId.HasValue)
            {
                sb.Append(BuildCharacterAliasLinkHtml(s.CharacterAliasId.Value));
                sb.Append("（CV: ").Append(BuildPersonAliasLinkHtml(s.VoicePersonAliasId.Value)).Append("）");
            }
            else if (s.PersonAliasId.HasValue)
            {
                sb.Append(BuildPersonAliasLinkHtml(s.PersonAliasId.Value));
            }
            else
            {
                // 人物 alias も持たない異常データ。affiliation_text があればそれを出す、無ければ無印。
                if (!string.IsNullOrEmpty(s.AffiliationText)) sb.Append(Escape(s.AffiliationText!));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 劇伴クレジット（<c>bgm_cue_credits</c>）から「役職バッジ + 名義」セグメントの列 HTML を組み立てる。
    /// 役職並び順は SQL 側で COMPOSITION → ARRANGEMENT → その他のソート済み（劇伴の慣習順）。
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
    public async Task<string> BuildBgmCueCreditsSegmentsHtmlAsync(int seriesId, string mNoDetail, CancellationToken ct)
    {
        var credits = await _bgmCueCreditsRepo.GetByCueAsync(seriesId, mNoDetail, ct).ConfigureAwait(false);
        if (credits.Count == 0) return "";

        // ステップ 1：役職コードごとに連名グループを作る（SQL の ORDER BY が保証する順序を尊重して
        // 単純走査でグループ化）。
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