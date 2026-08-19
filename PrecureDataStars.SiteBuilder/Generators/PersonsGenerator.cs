using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>人物索引（/persons/）と人物詳細（/persons/{person_id}/）の生成。</summary>
public sealed class PersonsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly PersonAliasPersonsRepository _aliasPersonsRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly RolesRepository _rolesRepo;
    /// <summary>屋号 alias_id → 屋号名 解決用。 クレジット履歴行に所属屋号併記を出すときに、Involvement に詰めた AffiliationCompanyAliasId から屋号名を引くために使う。</summary>
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    /// <summary>
    /// 主題歌種別マスタ（song_music_classes）を読むためのリポジトリ。
    /// クレジット履歴で主題歌系の役職に「オープニング主題歌」「エンディング主題歌」「挿入歌」の
    /// プレフィックスを付与する際、コード値（OP / ED / INSERT）から日本語表示名を引くために使う。
    /// episode_theme_songs.theme_kind は形式上 ENUM だが、コード値が song_music_classes.class_code と
    /// 一致する運用なので、マスタの name_ja をそのまま流用する。
    /// </summary>
    private readonly SongMusicClassesRepository _songMusicClassesRepo;

    private readonly CreditInvolvementIndex _index;

    /// <summary>person_id → 当該人物に紐付く全 alias_id のリスト（person_alias_persons の逆引き）。 1 度ロードしたら使い回す。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasesByPerson;

    /// <summary>役職コード → Role モデル。役職の表示名解決と display_order の取得に使う。</summary>
    private IReadOnlyDictionary<string, Role>? _roleMap;

    /// <summary>character_alias_id → CharacterAlias。声優関与のときキャラ名表示に使う。</summary>
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();

    /// <summary>company_alias_id → 屋号名 のキャッシュ。 クレジット履歴の所属屋号併記で同じ alias を何度も解決するため。 値が <c>null</c> のときは「未登録」を意味する（負の結果もキャッシュ）。</summary>
    private readonly Dictionary<int, string?> _companyAliasNameCache = new();

    /// <summary>主題歌種別コード（OP / ED / INSERT 等）→ SongMusicClass モデル のマップ。 クレジット履歴で「オープニング主題歌 作曲」のようなラベルを組み立てるための辞書。 <c>GenerateAsync</c> で 1 度だけロードして使い回す。</summary>
    private IReadOnlyDictionary<string, SongMusicClass>? _songMusicClassMap;

    /// <summary>person_alias_id → (song_id, role_code) の前計算索引。「楽曲」セクションで人物が担当した曲一覧を一発引きするために、 song_credits と song_recording_singers の両ソースを 1 度だけスキャンして alias_id 別にバケットしておく。 <c>GenerateAsync</c> で 1 度だけ詰めて使い回す。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>>? _songRolesByAlias;

    /// <summary>song_id → 当該曲のすべての song_recordings（song_recording_id 昇順）。「楽曲」セクションのカード表記で出典シリーズと音楽種別を引き当てる際の代表 recording 解決に使う。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<SongRecording>>? _recordingsBySong;

    /// <summary>person_alias_id → (song_id → その人が歌った録音)。歌唱（song_recording_singers）は録音単位で
    /// 出典シリーズ・版（VariantLabel）を持つため、「楽曲」カードの出典・タイトルを “実際に歌った録音” から
    /// 正確に解決するための索引。同一曲を複数録音で歌っている場合は出典が最も早い録音を採る。
    /// 作詞・作曲・編曲（曲単位の仕事）だけの曲は本索引に乗らず、従来どおり曲の代表録音から出典を解決する。</summary>
    private IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>>? _sungRecordingByAlias;

    public PersonsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _personsRepo = new PersonsRepository(factory);
        _aliasesRepo = new PersonAliasesRepository(factory);
        _aliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
        _songMusicClassesRepo = new SongMusicClassesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating persons");

        // 全人物・全名義を一括ロード。
        var persons = await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allAliases = await _aliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var aliasById = allAliases.ToDictionary(a => a.AliasId);

        // 役職マスタ（表示名 + display_order）を引いておく。
        if (_roleMap is null)
        {
            var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _roleMap = allRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        }

        // 主題歌種別マスタ（OP / ED / INSERT 等のコード値 → 日本語表示名）も引く。
        // クレジット履歴で「オープニング主題歌 作曲」のような展開を組み立てるための辞書。
        // 形式上は episode_theme_songs.theme_kind が ENUM だが、コード値が song_music_classes.class_code と
        // 一致する運用なので、マスタ側の name_ja を流用して自然な日本語ラベルに展開する。
        if (_songMusicClassMap is null)
        {
            var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _songMusicClassMap = allClasses.ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
        }

        // person_id → alias_id 群の逆引きは SiteDataLoader が BuildContext.AliasIdsByPerson に
        // 全件辞書化済み。本ジェネレータ内でローカル辞書を持たず、共有辞書を直接参照する。
        _aliasesByPerson ??= _ctx.AliasIdsByPerson;

        // 「楽曲」セクションの person_alias_id → (song_id, role_code) 索引を 1 度だけ前計算。
        // song_credits（作詞・作曲・編曲）と song_recording_singers（歌・コーラス）の両ソースから
        // person_alias_id をキーに集約する。後者は recording → song の解決を挟む。
        if (_songRolesByAlias is null)
        {
            var bucket = new Dictionary<int, List<(int SongId, string RoleCode)>>();
            foreach (var (songId, credits) in _ctx.SongCreditsBySong)
            {
                foreach (var c in credits)
                {
                    if (!bucket.TryGetValue(c.PersonAliasId, out var list))
                    {
                        list = new List<(int, string)>();
                        bucket[c.PersonAliasId] = list;
                    }
                    list.Add((songId, c.CreditRole));
                }
            }
            // 歌唱は 3 系統の人物名義を、いずれも当該人物の担当として記録する：
            //   (1) 主名義（PERSON 歌唱）               … PersonAliasId
            //   (2) スラッシュ並列の相方（PERSON 側）     … SlashPersonAliasId
            //   (3) キャラ歌唱(CHARACTER_WITH_CV)の声優   … VoicePersonAliasId
            // PersonAliasId だけ見ると、声優が「キャラ名義として歌った曲」を取りこぼす。
            // 歌系役職ページ /creators/roles/vocals/ と同じ 3 系統合算に揃える。
            void AddSingerSong(int aliasId, int songId, string roleCode)
            {
                if (!bucket.TryGetValue(aliasId, out var list))
                {
                    list = new List<(int, string)>();
                    bucket[aliasId] = list;
                }
                list.Add((songId, roleCode));
            }
            // 歌った録音を alias × song_id 単位で記録する（出典・版をカードで正確に出すため）。
            // 同一曲を複数録音で歌っている場合は出典シリーズが最も早い録音を採用する。
            var sungRecByAlias = new Dictionary<int, Dictionary<int, SongRecording>>();
            void AddSungRecording(int aliasId, SongRecording rec)
            {
                if (!sungRecByAlias.TryGetValue(aliasId, out var bySong))
                {
                    bySong = new Dictionary<int, SongRecording>();
                    sungRecByAlias[aliasId] = bySong;
                }
                if (!bySong.TryGetValue(rec.SongId, out var existing)
                    || _ctx.RecordingSeriesStart(rec) < _ctx.RecordingSeriesStart(existing))
                {
                    bySong[rec.SongId] = rec;
                }
            }
            foreach (var (recId, singers) in _ctx.SingersByRecording)
            {
                if (!_ctx.SongRecordingById.TryGetValue(recId, out var rec)) continue;
                foreach (var s in singers)
                {
                    if (s.PersonAliasId.HasValue) { AddSingerSong(s.PersonAliasId.Value, rec.SongId, s.RoleCode); AddSungRecording(s.PersonAliasId.Value, rec); }
                    if (s.SlashPersonAliasId.HasValue) { AddSingerSong(s.SlashPersonAliasId.Value, rec.SongId, s.RoleCode); AddSungRecording(s.SlashPersonAliasId.Value, rec); }
                    if (s.VoicePersonAliasId.HasValue) { AddSingerSong(s.VoicePersonAliasId.Value, rec.SongId, s.RoleCode); AddSungRecording(s.VoicePersonAliasId.Value, rec); }
                }
            }
            _songRolesByAlias = bucket.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<(int SongId, string RoleCode)>)kv.Value);
            _sungRecordingByAlias = sungRecByAlias.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<int, SongRecording>)kv.Value);
        }

        // song_id → recordings の索引も 1 度だけ組み立てておく（カード行の出典シリーズ解決用）。
        if (_recordingsBySong is null)
        {
            _recordingsBySong = _ctx.SongRecordingById.Values
                .GroupBy(r => r.SongId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<SongRecording>)g.OrderBy(r => r.SongRecordingId).ToList());
        }

        // 人物索引は「クリエーター > スタッフ」（/creators/staff/）に集約。
        // 本ジェネレータは人物単体の詳細ページ（/persons/{id}/）生成に専念する。

        // 詳細ページ。関与が 1 件もない人物もページは作る（直リンク用）。
        // 2 相生成：レンダリング＋ファイル書き出し（出力先はページごとに別パス）は並列、
        // サマリ・進捗・sitemap 記録だけを元順序で逐次に行う。
        // 詳細ページ生成経路は本メソッド前半で確定済みの読み取り専用辞書（_aliasesByPerson /
        // _songRolesByAlias / _recordingsBySong 等）とスレッドセーフな描画ヘルパしか触らないため、
        // 人物単位で安全に並列化できる（sitemap.xml の URL 並びは逐次記録で決定論を維持）。
        var urlPaths = new string[persons.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, persons.Count),
            new ParallelOptions { CancellationToken = ct },
            (i, _) =>
            {
                urlPaths[i] = RenderDetail(persons[i], aliasById);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        foreach (var urlPath in urlPaths)
        {
            _page.RecordWritten(urlPath, "persons");
        }

        _ctx.Logger.Success($"persons: {persons.Count} ページ");
    }

    /// <summary>人物詳細ページ <c>/persons/{person_id}/</c> をレンダリングしてファイルへ書き出し、URL パスを返す。
    /// 並列レンダリングフェーズから複数スレッドで同時に呼ばれるため共有状態への書き込みは行わない
    /// （出力ファイルパスはページごとに異なるため書き出しは安全。サマリ・sitemap 記録は
    /// 呼び出し側が逐次フェーズで行う）。</summary>
    private string RenderDetail(
        Person person,
        IReadOnlyDictionary<int, PersonAlias> aliasById)
    {
        var aliasIds = _aliasesByPerson!.TryGetValue(person.PersonId, out var ids)
            ? ids
            : Array.Empty<int>();
        var aliases = aliasIds
            .Select(id => aliasById.TryGetValue(id, out var a) ? a : null)
            .Where(a => a is not null)
            .Cast<PersonAlias>()
            .ToList();

        // 名義の時系列順序付け：predecessor を逆方向にたどって最古の alias を root にし、
        // そこから successor チェーンで並べる。リング構造や複線がある場合は、
        // 解けたチェーンに含まれない alias を末尾にまとめて出す。
        var aliasViews = OrderAliasesChronologically(aliases);

        // 代表名義（successor が無い alias を優先、無ければ先頭）。
        PersonAlias? currentAlias = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases.FirstOrDefault();
        string displayName = currentAlias is null
            ? person.FullName
            : (currentAlias.DisplayTextOverride ?? currentAlias.Name);

        // 役職別グループ化された関与一覧を組み立て（フラット、全名義横断）。
        var involvementGroups = BuildPersonInvolvementGroups(aliasIds);

        // クレジットのある名義が 2 つ以上あるときだけ、名義単位のセクション（初登場順）に分ける
        // （企業・団体詳細と同じ規律）。1 つ以下ならテンプレ側は involvementGroups のフラット表示を使う。
        var involvementSections = BuildPersonAliasInvolvementSections(aliasIds, aliasById);

        // クレジット合計バッジは role 別 EpisodeCount の単純合算ではなく distinct 話数・本数で出す。
        // 同一名義が同じ話数に複数役職でクレジットされている場合、role ごとの EpisodeCount を
        // 合算すると同じ話数を二重に数えてしまうため、全名義の関与を再度 InvolvementRowBuilder に
        // 通してシリーズ単位で distinct 集計する（CompaniesGenerator と同じ規律）。
        var allPersonInvolvements = aliasIds.Where(_index.ByPersonAlias.ContainsKey).SelectMany(id => _index.ByPersonAlias[id]).ToList();
        var (_, creditEpisodeCountTotal, creditMovieCountTotal) = InvolvementRowBuilder.BuildSeriesRows(_ctx, allPersonInvolvements);

        // 「楽曲」セクションのカード行（構造化エントリ song_credits / song_recording_singers から）。
        var songCards = BuildPersonSongCards(aliasIds);
        // 誕生日表記：BirthYearVisibility=PUBLIC かつ BirthYear ありなら「YYYY年M月D日」、
        // 非公開もしくは未設定なら年抜きの「M月D日」。BirthMonth / BirthDay の片方でも未設定なら空文字。
        string birthday = FormatBirthday(person);

        var content = new PersonDetailModel
        {
            Person = new PersonView
            {
                PersonId = person.PersonId,
                DisplayName = displayName,
                FullName = person.FullName,
                FullNameKana = person.FullNameKana ?? "",
                NameEn = person.NameEn ?? "",
                Notes = person.Notes ?? "",
                Birthday = birthday,
                OfficialUrl = person.OfficialUrl ?? "",
                XUrl = person.XUrl ?? "",
                InstagramUrl = person.InstagramUrl ?? "",
                YoutubeUrl = person.YoutubeUrl ?? ""
            },
            InvolvementGroups = involvementGroups,
            InvolvementSections = involvementSections,
            CreditEpisodeCountTotal = creditEpisodeCountTotal,
            CreditMovieCountTotal = creditMovieCountTotal,
            SongCards = songCards,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        // 人物詳細の構造化データは Schema.org の Person 型。
        string baseUrl = _ctx.Config.BaseUrl;
        string personUrl = PathUtil.PersonUrl(person.PersonId);
        var alternateNames = aliasViews
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, person.FullName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // MetaDescription を実データから組み立てる。
        // 「{人名}は、プリキュアシリーズで{役職1}({N話})・{役職2}({N話})などを担当。」を骨格にする。
        var metaDescription = BuildPersonMetaDescription(displayName, involvementGroups);

        // jobTitle は involvementGroups の RoleLabel（役職名 / 例：「監督」「脚本」）から
        // 担当話数の多い順で上位 3 件のラベルを取り出す。Count は当該役職での担当エピソード数。
        var topJobTitles = involvementGroups
            .OrderByDescending(g => g.Count)
            .Select(g => g.RoleLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label) && label != "(役職未設定)")
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();

        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["name"] = displayName,
            // description は SERP のリッチスニペットや AI 検索エンジンの要約候補として参照される。
            // MetaDescription と同じ文面を入れて二重整合性を担保する。
            ["description"] = metaDescription
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (!string.IsNullOrEmpty(person.NameEn)) jsonLdDict["givenName"] = person.NameEn;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + personUrl;
        if (topJobTitles.Count > 0)
        {
            // jobTitle は単一文字列でも配列でも有効（Schema.org 仕様）。Person の主要な役職を
            // 配列で複数並べる形式は職能横断を素直に表現できる（アニメスタッフは複数の役職を兼ねるため）。
            jsonLdDict["jobTitle"] = topJobTitles;
        }

        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = displayName,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
                new BreadcrumbItem { Label = displayName, Url = "" }
            },
            OgType = "profile",
            JsonLd = jsonLd,
            OgCard = BuildOgCard(displayName, involvementGroups, creditEpisodeCountTotal, creditMovieCountTotal, _ctx.CreditCoverageLabel)
        };

        _page.RenderAndWriteFile(personUrl, "persons-detail.sbn", content, layout);
        return personUrl;
    }

    /// <summary>
    /// 人物詳細ページの OGP カードを組み立てる。
    /// 「氏名 → 関与規模のバッジ → 基準点 → 担当役職と話数」の順に置く。
    /// 氏名だけのカードでは誰なのか伝わらないため、担当話数の多い役職を上から並べて
    /// 「プリキュアで何をしてきた人か」を一目で示す。
    /// </summary>
    private static OgCardSpec BuildOgCard(
        string displayName,
        IReadOnlyList<InvolvementGroup> involvementGroups,
        int creditEpisodeCountTotal,
        int creditMovieCountTotal,
        string coverageLabel)
    {
        // TV と映画はどちらも「担当した量」なので、片方だけを「担当」と呼ばず媒体名で対等に並べる。
        var badges = new List<OgCardBadge>();
        if (creditEpisodeCountTotal > 0) badges.Add(new OgCardBadge("TV", $"{creditEpisodeCountTotal}話"));
        if (creditMovieCountTotal > 0) badges.Add(new OgCardBadge("映画", $"{creditMovieCountTotal}本"));

        // 役職は担当規模の多い順。カードに載るのは上位数件で、溢れた分はレンダラ側が切り落とす。
        var roles = involvementGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.RoleLabel) && g.Count > 0)
            .OrderByDescending(g => g.Count)
            .Select(g => new OgCardFactLine(g.RoleLabel, FormatInvolvementCount(g)))
            .ToArray();

        // 前置きは置かない。「クリエーター」と名乗らせなくても、氏名と担当役職の並びで何者かは伝わる。
        return new OgCardSpec(Kicker: "", Title: displayName)
        {
            // 担当話数はクレジット登録済みの範囲でしか数えられない。母数を示さずに数だけ出すと
            // 「歴代の全担当数」と受け取られてしまうため、基準点をカード上で明記する。
            // 位置は数の直下。数を読んだ直後に効く但し書きなので、数より先に目に入る上段には置かない。
            MetaLeft = OgCoverageLabel.Compact(coverageLabel),
            Badges = badges,
            InlineFacts = roles
        };
    }

    /// <summary>
    /// 役職ごとの関与規模をカード用に短く整形する。総数はバッジ側で出しているため、
    /// <see cref="InvolvementGroup.CountLabel"/> のように「担当」を繰り返さず件数だけを並べる。
    /// </summary>
    private static string FormatInvolvementCount(InvolvementGroup group)
    {
        if (group.EpisodeCount > 0 && group.MovieCount > 0) return $"{group.EpisodeCount}話 / {group.MovieCount}本";
        return group.MovieCount > 0 ? $"{group.MovieCount}本" : $"{group.EpisodeCount}話";
    }

    /// <summary>
    /// 人物詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// 構成：「{人名}は、プリキュアシリーズで{役職1}({N話})・{役職2}({N話})・{役職3}({N話})などを担当。」を骨格に、
    /// 各セグメント追加前に <c>targetMaxChars=140</c> を超えないかを確認しつつ追記する。
    /// 役職は <see cref="InvolvementGroup.Count"/> 降順（担当話数の多い順）でソートして
    /// 上位を採用する。声優役は <see cref="InvolvementGroup.HasCharacterColumn"/> が true なので
    /// 「演じた役（声優）」を簡略表現で別途付ける手もあるが、本リビジョンでは役職ラベルで統一する。
    /// 関与役職が 1 件も無い人物（呼ばれない想定だが安全網として）は、定型文「{人名}のプリキュア関連クレジット一覧です。」に
    /// フォールバックする。
    /// </summary>
    private static string BuildPersonMetaDescription(
        string displayName,
        IReadOnlyList<InvolvementGroup> involvementGroups)
    {
        const int targetMaxChars = 140;

        if (involvementGroups.Count == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        // 担当話数の多い順で上位役職を取り出し、最大 3 件まで採用する。
        var ordered = involvementGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.RoleLabel) && g.RoleLabel != "(役職未設定)")
            .OrderByDescending(g => g.Count)
            .Take(3)
            .ToList();

        if (ordered.Count == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(displayName).Append("は、プリキュアシリーズで");

        int appended = 0;
        foreach (var g in ordered)
        {
            // 「役職(N話・M本)」のフラグメントを組む。担当ゼロ（話数 + 本数とも 0）は弾く。
            // TV 系シリーズへの担当は「話」、映画系シリーズへの担当は「本」で表記し、両方あれば「N話・M本」併記。
            if (g.Count <= 0) continue;
            var fragment = $"{g.RoleLabel}({g.CountLabel.Replace(" ", "")})";
            // 末尾「などを担当。」(7 字) ぶんを残せるかの判定を含めて追加可否を決める。
            int suffixLen = 7;
            int joinerLen = appended > 0 ? 1 : 0;
            if (sb.Length + joinerLen + fragment.Length + suffixLen > targetMaxChars) break;
            if (appended > 0) sb.Append('・');
            sb.Append(fragment);
            appended++;
        }

        if (appended == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        sb.Append("などを担当。");
        return sb.ToString();
    }

    /// <summary>人物の名義群を時系列に並べる（predecessor チェーンを上に辿って root を見つけ、 successor チェーンで下降）。チェーンに含まれなかった alias は末尾に並べる。</summary>
    private static IReadOnlyList<PersonAliasView> OrderAliasesChronologically(IReadOnlyList<PersonAlias> aliases)
    {
        if (aliases.Count == 0) return Array.Empty<PersonAliasView>();

        var byId = aliases.ToDictionary(a => a.AliasId);
        var visited = new HashSet<int>();
        var ordered = new List<PersonAlias>();

        // predecessor を持たない alias（チェーンの先頭候補）から開始。複数あればそれぞれの鎖を順に。
        foreach (var head in aliases.Where(a => a.PredecessorAliasId is null
            || !byId.ContainsKey(a.PredecessorAliasId!.Value)))
        {
            var cur = head;
            while (cur is not null && visited.Add(cur.AliasId))
            {
                ordered.Add(cur);
                if (cur.SuccessorAliasId is int nextId && byId.TryGetValue(nextId, out var next)) cur = next;
                else cur = null;
            }
        }

        // 上で訪問できなかった alias（チェーン構造異常時の取りこぼし）を末尾に追加。
        foreach (var a in aliases)
        {
            if (visited.Add(a.AliasId)) ordered.Add(a);
        }

        return ordered.Select(a => new PersonAliasView
        {
            AliasId = a.AliasId,
            Name = a.DisplayTextOverride ?? a.Name,
            NameKana = a.NameKana ?? "",
            NameEn = a.NameEn ?? "",
            ValidFrom = JpDateFormat.NullableDate(a.ValidFrom),
            ValidTo = JpDateFormat.NullableDate(a.ValidTo),
            Notes = a.Notes ?? ""
        }).ToList();
    }

    /// <summary>
    /// 人物に紐付く alias_id 群から関与情報を集約し、役職別 → シリーズ単位の話数圧縮表記に編成する。
    /// 役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示する。
    /// 全話担当のときは話数表記を省略し、代わりに「(全話)」マークを付ける。
    /// 声優役（CHARACTER_VOICE）のときは演じたキャラ名（シリーズ内全話分の連名）も併記する。
    /// シリーズ全体スコープ（episode_id NULL）の関与は別行として「（シリーズ全体）」で残す。
    /// シリーズ行の共通骨格（映画系判定・話数集合・全話判定・圧縮表記・行構築）は
    /// <see cref="InvolvementRowBuilder"/>（CompaniesGenerator と共用）に集約し、本メソッドは
    /// 役職グループ編成と人物詳細固有の付加情報（<see cref="ResolveSeriesRowExtras"/>）を担う。
    /// </summary>
    private IReadOnlyList<InvolvementGroup> BuildPersonInvolvementGroups(IReadOnlyList<int> aliasIds)
    {
        var all = aliasIds
            .Where(_index.ByPersonAlias.ContainsKey)
            .SelectMany(id => _index.ByPersonAlias[id])
            .ToList();
        if (all.Count == 0) return Array.Empty<InvolvementGroup>();

        // 役職グループの並びは「その役職で当該人物が最初にクレジットされた位置」（初参加）昇順。
        // キーは (シリーズ放送開始日のシリアル値, シリーズ内話数, クレジット階層位置) の辞書順で、
        // CreatorsGenerator.FirstCreditAccumulator と同じ合成基準（roles.display_order には依存しない）。
        // シリーズ全体スコープ（episode_id=null）の関与は話数 0 として当該シリーズ内で最優先に扱う。
        (long StartDay, int EpNo, long Pos) EarliestCreditKey(IEnumerable<Involvement> invs)
        {
            long bestDay = long.MaxValue;
            int bestEpNo = int.MaxValue;
            long bestPos = long.MaxValue;
            foreach (var inv in invs)
            {
                long day = _ctx.SeriesStartDate(inv.SeriesId).DayNumber;
                int epNo = inv.EpisodeId is int eid
                    ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
                    : 0;
                long pos = inv.CreditPos;
                if (day < bestDay
                    || (day == bestDay && epNo < bestEpNo)
                    || (day == bestDay && epNo == bestEpNo && pos < bestPos))
                {
                    bestDay = day;
                    bestEpNo = epNo;
                    bestPos = pos;
                }
            }
            return (bestDay, bestEpNo, bestPos);
        }

        // グループ分けキーは「カテゴリプレフィックスコード × 役職コード」の複合。
        static string CategoryPrefixOf(Involvement inv)
        {
            if (string.Equals(inv.EntryKind, "SONG_CREDIT", StringComparison.Ordinal)
                || string.Equals(inv.EntryKind, "RECORDING_SINGER", StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(inv.ThemeKind) ? "" : inv.ThemeKind!;
            }
            if (string.Equals(inv.EntryKind, "BGM_CUE_CREDIT", StringComparison.Ordinal))
            {
                return "BGM";
            }
            return "";
        }

        // カテゴリプレフィックスの表示順：プレフィックス無し → OP → ED → INSERT → BGM。
        // song_music_classes.display_order を踏襲し、BGM はマスタ外なので末尾固定。
        int CategoryOrder(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return 0;
            if (string.Equals(prefix, "BGM", StringComparison.Ordinal)) return 99;
            if (_songMusicClassMap is not null && _songMusicClassMap.TryGetValue(prefix, out var m))
            {
                return m.DisplayOrder is byte d ? d : 100;
            }
            return 100;
        }

        // 役職ラベルに付与するカテゴリプレフィックスの日本語表現。
        //   - "OP"/"ED"/"INSERT" → song_music_classes.name_ja（オープニング主題歌／エンディング主題歌／挿入歌）
        //   - "BGM" → "劇伴"（マスタ外なのでハードコード）
        //   - 空文字 → 空文字
        // マスタ未登録の不明コードはコード値そのままで暫定表示し、データ不整合を視覚的に検知できるようにする。
        string CategoryPrefixLabel(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return "";
            if (string.Equals(prefix, "BGM", StringComparison.Ordinal)) return "劇伴";
            if (_songMusicClassMap is not null && _songMusicClassMap.TryGetValue(prefix, out var m)
                && !string.IsNullOrEmpty(m.NameJa))
            {
                return m.NameJa;
            }
            return prefix;
        }

        var groups = new List<InvolvementGroup>();
        foreach (var roleGroup in all
            .GroupBy(i => (Prefix: CategoryPrefixOf(i), Role: i.RoleCode))
            .OrderBy(g => CategoryOrder(g.Key.Prefix))
            .ThenBy(g => EarliestCreditKey(g)))
        {
            string categoryPrefix = roleGroup.Key.Prefix;
            string roleCode = roleGroup.Key.Role;
            string baseRoleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);
            // カテゴリプレフィックスがある場合は「オープニング主題歌 作曲」のように半角スペース区切りで連結。
            string prefixLabel = CategoryPrefixLabel(categoryPrefix);
            string roleLabel = string.IsNullOrEmpty(prefixLabel)
                ? baseRoleLabel
                : $"{prefixLabel} {baseRoleLabel}";

            // 役職グループ内をさらにシリーズ単位で集約。共通骨格（映画系判定 → 話数集合の収集 →
            // 全話判定 → 話数圧縮表記 → シリーズ全体スコープ行 → 行構築）は InvolvementRowBuilder に
            // 集約し、人物詳細固有の付加情報（演じたキャラ名・所属屋号ラベル）はフックで差し込む。
            var (seriesRows, episodeCountTotal, movieCountTotal) =
                InvolvementRowBuilder.BuildSeriesRows(_ctx, roleGroup, ResolveSeriesRowExtras);

            if (seriesRows.Count == 0) continue;

            // 役職別グループ見出しに付けるリンク先。役職詳細ページが実在する役職だけリンクする：
            // ・THEME_SONG 形式（OP/ED 主題歌・挿入歌のブロック見出し役職）は /creators/roles/ 配下に
            //   ページが生成されないためリンクなし（404 リンクを出さない）
            // ・VOICE_CAST 形式は声の出演一覧（/creators/voice-cast/）へ
            // ・役職コードが空（マスタ未登録）はリンクなし
            string roleUrl = "";
            if (!string.IsNullOrEmpty(roleCode))
            {
                string formatKind = _roleMap!.TryGetValue(roleCode, out var roleDef)
                    ? (roleDef.RoleFormatKind ?? "")
                    : "";
                roleUrl = formatKind switch
                {
                    "THEME_SONG" => "",
                    "VOICE_CAST" => PathUtil.CreatorsVoiceCastUrl(),
                    _ => PathUtil.CreatorsRoleUrl(roleCode)
                };
            }
            // 声の出演で複数のキャラ（役）を演じている場合は、役を大くくりにしたサブセクションを組み立てる。
            // テンプレはこちらを優先描画し、各役の配下にシリーズと話数を出す（シリーズや映画をまたぐ役も
            // 1 つのくくりに通算される）。役が 1 つだけなら従来どおりシリーズ行に「— キャラ名」を併記する。
            var characterSections = BuildVoiceCharacterSections(roleGroup);

            groups.Add(new InvolvementGroup
            {
                RoleCode = roleCode,
                RoleLabel = roleLabel,
                RoleUrl = roleUrl,
                SeriesRows = seriesRows,
                CharacterSections = characterSections,
                EpisodeCount = episodeCountTotal,
                MovieCount = movieCountTotal,
                HasCharacterColumn = seriesRows.Any(r => !string.IsNullOrEmpty(r.CharacterNames))
            });
        }
        return groups;
    }

    /// <summary>
    /// クレジット履歴を「名義」単位のセクション（初登場順）に分けて組み立てる。
    /// クレジットのある名義が 2 つ以上あるときだけ非空リストを返し、1 つ以下なら空リスト（呼び出し側が
    /// <see cref="BuildPersonInvolvementGroups"/> の全名義横断フラット表示にフォールバックする前提）。
    /// 各セクションの役職別グループは、当該名義 1 件だけを渡した <see cref="BuildPersonInvolvementGroups"/> の
    /// 再利用で組み立てる（声の出演の役（キャラ）大くくりサブセクションも自動的に引き継がれる）。
    /// 並びは <see cref="CompaniesGenerator"/> の屋号別セクションと同じ「最早クレジット位置」昇順。
    /// </summary>
    private IReadOnlyList<AliasInvolvementSection> BuildPersonAliasInvolvementSections(
        IReadOnlyList<int> aliasIds,
        IReadOnlyDictionary<int, PersonAlias> aliasById)
    {
        var candidates = aliasIds.Where(id => _index.ByPersonAlias.ContainsKey(id) && _index.ByPersonAlias[id].Count > 0).ToList();
        if (candidates.Count <= 1) return Array.Empty<AliasInvolvementSection>();

        var sections = new List<(DateTime FirstAt, AliasInvolvementSection Section)>();
        foreach (var aliasId in candidates)
        {
            var groups = BuildPersonInvolvementGroups(new[] { aliasId });
            if (groups.Count == 0) continue;

            DateTime firstAt = DateTime.MaxValue;
            foreach (var inv in _index.ByPersonAlias[aliasId])
            {
                DateTime at = inv.EpisodeId is int eid && _ctx.LookupEpisode(inv.SeriesId, eid) is { } ep
                    ? ep.OnAirAt
                    : _ctx.SeriesStartDate(inv.SeriesId).ToDateTime(TimeOnly.MinValue);
                if (at < firstAt) firstAt = at;
            }

            string aliasName = aliasById.TryGetValue(aliasId, out var a) ? (a.DisplayTextOverride ?? a.Name) : "";
            if (string.IsNullOrEmpty(aliasName)) continue;

            // 名義見出しの合計バッジも role 別 EpisodeCount の単純合算ではなく distinct 集計にする。
            var (_, aliasEpisodeCount, aliasMovieCount) = InvolvementRowBuilder.BuildSeriesRows(_ctx, _index.ByPersonAlias[aliasId]);

            sections.Add((firstAt, new AliasInvolvementSection
            {
                AliasName = aliasName,
                Groups = groups,
                EpisodeCount = aliasEpisodeCount,
                MovieCount = aliasMovieCount
            }));
        }
        return sections.Count > 1
            ? sections.OrderBy(s => s.FirstAt).Select(s => s.Section).ToList()
            : Array.Empty<AliasInvolvementSection>();
    }

    /// <summary>
    /// 人物詳細のシリーズ単位行に併記する付加情報（演じたキャラ名・所属屋号ラベル）を、
    /// 当該シリーズの関与群からスコープ別（シリーズ全体 / エピソード単位）に収集して解決する。
    /// <see cref="InvolvementRowBuilder.BuildSeriesRows"/> のフックとして役職×シリーズごとに呼ばれる。
    /// </summary>
    private InvolvementSeriesRowExtras ResolveSeriesRowExtras(IEnumerable<Involvement> invs)
    {
        var seriesScopeCharacterNames = new List<string>();
        var perEpisodeCharacterNames = new List<string>();
        // 所属屋号 ID の集合をシリーズスコープ別・エピソード単位別に分けて収集。
        // 同一シリーズ内で複数の屋号で所属クレジットされる例（移籍など）があるため、
        // HashSet で重複排除し、後で名前解決して列挙する。
        // OrderedSet 相当の挙動が欲しいので「初出順を保つために」List + Contains で管理する。
        var seriesScopeAffiliationIds = new List<int>();
        var perEpisodeAffiliationIds = new List<int>();

        foreach (var inv in invs)
        {
            if (inv.EpisodeId is int)
            {
                // 声優関与のとき演じたキャラ名を集める（シリーズ単位で重複排除）。
                if (inv.Kind == InvolvementKind.CharacterVoice && inv.CharacterAliasId.HasValue)
                {
                    string? name = ResolveCharacterName(inv.CharacterAliasId.Value);
                    if (!string.IsNullOrEmpty(name) && !perEpisodeCharacterNames.Contains(name))
                        perEpisodeCharacterNames.Add(name);
                }
                // 所属屋号 ID を初出順で記録（人物詳細での所属併記用）。
                if (inv.AffiliationCompanyAliasId is int affId
                    && !perEpisodeAffiliationIds.Contains(affId))
                {
                    perEpisodeAffiliationIds.Add(affId);
                }
            }
            else
            {
                if (inv.Kind == InvolvementKind.CharacterVoice && inv.CharacterAliasId.HasValue)
                {
                    string? name = ResolveCharacterName(inv.CharacterAliasId.Value);
                    if (!string.IsNullOrEmpty(name) && !seriesScopeCharacterNames.Contains(name))
                        seriesScopeCharacterNames.Add(name);
                }
                if (inv.AffiliationCompanyAliasId is int affIdS
                    && !seriesScopeAffiliationIds.Contains(affIdS))
                {
                    seriesScopeAffiliationIds.Add(affIdS);
                }
            }
        }

        // 所属屋号 ID 集合を表示名（テンプレ用ラベル）に解決する。
        // 屋号名は company_aliases.name 由来（display_text_override は使わない、当該人物の所属としての
        // 自然な屋号名を出すため）。複数屋号がある場合は「、」で連結。
        // 1 件も無いシリーズ行では空文字を返す（テンプレ側で「(屋号名)」全体を非表示にする）。
        string ResolveAffLabel(IReadOnlyList<int> ids)
        {
            if (ids.Count == 0) return "";
            var names = new List<string>(ids.Count);
            foreach (var id in ids)
            {
                var name = GetCompanyAliasName(id);
                if (!string.IsNullOrEmpty(name)) names.Add(name!);
            }
            return string.Join("、", names);
        }

        return new InvolvementSeriesRowExtras(
            SeriesScopeCharacterNames: string.Join("、", seriesScopeCharacterNames),
            PerEpisodeCharacterNames: string.Join("、", perEpisodeCharacterNames),
            SeriesScopeAffiliationsLabel: ResolveAffLabel(seriesScopeAffiliationIds),
            PerEpisodeAffiliationsLabel: ResolveAffLabel(perEpisodeAffiliationIds));
    }

    /// <summary>
    /// 声の出演グループを「役（キャラクター）」単位のサブセクションに分ける。
    /// 対象は CharacterVoice 関与が複数キャラに跨る場合のみで、1 キャラ以下なら空リストを返す
    /// （テンプレは空のとき従来のシリーズ行表示にフォールバックする）。
    /// セクションの並びは役の初登場（シリーズ開始日 → 最早話数）順。各セクション内はシリーズ行
    /// （放送開始日順、話数の圧縮表記つき）。表示名は最初にクレジットされた名義（character_aliases.name）。
    /// </summary>
    private IReadOnlyList<CharacterRoleSection> BuildVoiceCharacterSections(IEnumerable<Involvement> roleGroup)
    {
        // キャラ ID → (表示名, 表示順キー, シリーズ ID → 話数集合 / シリーズ全体スコープ有無)
        var byChar = new Dictionary<int, (string Label, long FirstKey, Dictionary<int, (HashSet<int> EpNos, bool SeriesScope)> Series)>();

        foreach (var inv in roleGroup)
        {
            if (inv.Kind != InvolvementKind.CharacterVoice) continue;
            if (inv.CharacterAliasId is not int caId) continue;
            if (!_ctx.CharacterAliasById.TryGetValue(caId, out var ca)) continue;
            int charId = ca.CharacterId;

            // 初登場キー：シリーズ開始日（日数）×100000 + 話数（シリーズ全体スコープは末尾送り）。
            int epNo = 0;
            if (inv.EpisodeId is int eid && _ctx.LookupEpisode(inv.SeriesId, eid) is { } ep) epNo = ep.SeriesEpNo;
            long key = (long)_ctx.SeriesStartDate(inv.SeriesId).DayNumber * 100000
                       + (epNo == 0 ? 99999 : epNo);

            if (!byChar.TryGetValue(charId, out var acc))
            {
                acc = (ca.Name, key, new Dictionary<int, (HashSet<int>, bool)>());
                byChar[charId] = acc;
            }
            if (key < acc.FirstKey)
            {
                acc = (acc.Label, key, acc.Series);
                byChar[charId] = acc;
            }
            if (!acc.Series.TryGetValue(inv.SeriesId, out var s))
            {
                s = (new HashSet<int>(), false);
            }
            if (epNo > 0) s.EpNos.Add(epNo);
            else s.SeriesScope = true;
            acc.Series[inv.SeriesId] = s;
        }

        if (byChar.Count <= 1) return Array.Empty<CharacterRoleSection>();

        var sections = new List<CharacterRoleSection>();
        foreach (var kv in byChar.OrderBy(kv => kv.Value.FirstKey))
        {
            var rows = new List<InvolvementSeriesRow>();
            foreach (var sk in kv.Value.Series.OrderBy(s => _ctx.SeriesStartDate(s.Key)))
            {
                if (!_ctx.SeriesById.TryGetValue(sk.Key, out var series)) continue;
                var allSeriesEpNos = _ctx.EpisodesBySeries.TryGetValue(sk.Key, out var allEps)
                    ? allEps.Select(e => e.SeriesEpNo).ToList()
                    : new List<int>();
                bool isAll = sk.Value.EpNos.Count > 0 && allSeriesEpNos.Count > 0
                             && sk.Value.EpNos.SetEquals(allSeriesEpNos);
                string rangeLabel = sk.Value.EpNos.Count == 0
                    ? ""
                    : (isAll ? "" : EpisodeRangeCompressor.Compress(sk.Value.EpNos));
                rows.Add(new InvolvementSeriesRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    RangeLabel = rangeLabel,
                    IsAllEpisodes = isAll,
                    CharacterNames = "",
                    AffiliationsLabel = ""
                });
            }
            if (rows.Count == 0) continue;

            sections.Add(new CharacterRoleSection
            {
                CharacterLabel = kv.Value.Label,
                CharacterUrl = PathUtil.CharacterUrl(kv.Key),
                SeriesRows = rows
            });
        }
        return sections;
    }

    /// <summary>誕生日表記を組み立てる。 BirthYearVisibility=PUBLIC かつ BirthYear ありなら「YYYY年M月D日」、 非公開もしくは未設定なら年抜きの「M月D日」。 BirthMonth / BirthDay の片方でも未設定なら空文字を返す（誕生日行を出さない）。</summary>
    private static string FormatBirthday(Person p)
    {
        if (p.BirthMonth is not byte m || p.BirthDay is not byte d) return "";
        if (p.BirthYear is ushort y
            && string.Equals(p.BirthYearVisibility, "PUBLIC", StringComparison.Ordinal))
        {
            return $"{y}年{m}月{d}日";
        }
        return $"{m}月{d}日";
    }

    /// <summary>
    /// 構造化エントリ（song_credits / song_recording_singers）に紐付いた当該人物の担当楽曲をカード行群に集約する。
    /// 1 カード = 1 曲。同じ曲で複数役職（作詞 + 作曲 等）を持つ場合は同カード内に役職バッジを並べる。
    /// 出典シリーズ・タイトルは、その人が歌った曲は「歌った録音」から、作詞作曲編曲だけの曲は当該曲の
    /// 最古 recording から解決する。並びは「シリーズ開始年昇順 → 曲タイトル昇順」。
    /// 共通中間処理（song_id 単位の集約 → 歌った録音・出典・種別・役職バッジの解決）は
    /// <see cref="SongCardBuilder"/>（CharactersGenerator と共用）に集約し、本メソッドは
    /// 人物詳細用 DTO への射影と最終ソートだけを行う。
    /// </summary>
    private IReadOnlyList<PersonSongCard> BuildPersonSongCards(IReadOnlyList<int> aliasIds)
    {
        if (aliasIds.Count == 0 || _songRolesByAlias is null) return Array.Empty<PersonSongCard>();

        // 共通中間処理。歌唱が無く作詞作曲編曲だけの曲の出典解決用に、曲の代表録音索引
        // （_recordingsBySong）をフォールバックとして渡す（人物詳細のみの経路。
        // キャラ詳細は「歌った録音」だけで完結するため渡さない）。
        var cores = SongCardBuilder.BuildCores(
            _ctx, aliasIds, _songRolesByAlias, _sungRecordingByAlias, _recordingsBySong, _roleMap!);
        if (cores.Count == 0) return Array.Empty<PersonSongCard>();

        // 共通中間表現から人物詳細用 DTO へ射影。役職バッジは役職統計ページ
        // （/creators/roles/{code}/）への URL 付きバッジ（RoleBadgeView）に変換する。
        var cards = new List<PersonSongCard>(cores.Count);
        foreach (var core in cores)
        {
            cards.Add(new PersonSongCard
            {
                SongId = core.SongId,
                SongUrl = core.SongUrl,
                Title = core.Title,
                SeriesTitle = core.SeriesTitle,
                SeriesUrl = core.SeriesUrl,
                SeriesStartYearLabel = core.SeriesStartYearLabel,
                SeriesStartDateRaw = core.SeriesStartDateRaw,
                SortRecordingId = core.SortRecordingId,
                MusicClassLabel = core.MusicClassLabel,
                BadgeClassSuffix = core.BadgeClassSuffix,
                Roles = core.Roles
                    .Select(b => new RoleBadgeView
                    {
                        Code = b.Code,
                        Label = b.Label,
                        Url = PathUtil.CreatorsRoleUrl(b.Code),
                        DisplayOrder = b.DisplayOrder
                    })
                    .ToList()
            });
        }

        // ソート：録音（recording）を共通軸にしたカタログ登場順（代表録音の recording_id 昇順）。
        // 歌は歌った録音の位置、作詞作曲は曲の初出録音の位置で並ぶ。同値は song_id で安定化。
        return cards
            .OrderBy(c => c.SortRecordingId)
            .ThenBy(c => c.SongId)
            .ToList();
    }

    /// <summary>character_alias_id からキャラ名を引く。 BuildContext.CharacterAliasById に全件辞書化済みのため同期 lookup で完結する。</summary>
    private string? ResolveCharacterName(int aliasId)
        => _ctx.CharacterAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null;

    /// <summary>company_alias_id から屋号名を引く。 BuildContext.CompanyAliasById に全件辞書化済みのため同期 lookup で完結する。</summary>
    private string? GetCompanyAliasName(int aliasId)
        => _ctx.CompanyAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null;

    // ─── テンプレ用 DTO 群 ───

    private sealed class PersonDetailModel
    {
        public PersonView Person { get; set; } = new();
        /// <summary>クレジット（フラット）。名義を横断した役職別グループ → シリーズ行。
        /// <see cref="InvolvementSections"/> が空（クレジットのある名義が 1 つだけ）のときにテンプレ側が使う。</summary>
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
        /// <summary>クレジット（名義別）。クレジットのある名義が 2 つ以上あるときだけ、
        /// 名義単位のセクション（初登場順）に分ける。各セクション内は役職別グループ → シリーズ行。
        /// 1 つ以下のときは空（テンプレ側は <see cref="InvolvementGroups"/> のフラット表示にフォールバック）。</summary>
        public IReadOnlyList<AliasInvolvementSection> InvolvementSections { get; set; } = Array.Empty<AliasInvolvementSection>();
        /// <summary>クレジットセクション見出し横に出す合計担当話数（TV 系シリーズ横断）。</summary>
        public int CreditEpisodeCountTotal { get; set; }
        /// <summary>クレジットセクション見出し横に出す合計担当本数（映画系シリーズ横断）。</summary>
        public int CreditMovieCountTotal { get; set; }
        /// <summary>構造化エントリ（song_credits / song_recording_singers）由来の担当楽曲カード行群。</summary>
        public IReadOnlyList<PersonSongCard> SongCards { get; set; } = Array.Empty<PersonSongCard>();
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の h1 ブロック直後に独立段落で表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class PersonView
    {
        public int PersonId { get; set; }
        public string DisplayName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string FullNameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>誕生日表記（「YYYY年M月D日」または「M月D日」、未設定時は空文字）。</summary>
        public string Birthday { get; set; } = "";
        /// <summary>事務所等の公式ページ URL。詳細ページ末尾「外部リンク」セクションに出す。 Wikipedia は内部値として保持はするがサイト UI からはリンクしない方針なので、 ここでは敢えて出していない。</summary>
        public string OfficialUrl { get; set; } = "";
        public string XUrl { get; set; } = "";
        public string InstagramUrl { get; set; } = "";
        public string YoutubeUrl { get; set; } = "";
    }

    /// <summary>担当楽曲カード 1 行。1 曲につき 1 行で、複数役職は <see cref="Roles"/> に並べる。</summary>
    private sealed class PersonSongCard
    {
        public int SongId { get; set; }
        public string SongUrl { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>代表 recording 由来の出典シリーズ名（解決できない場合は空文字）。</summary>
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        /// <summary>出典シリーズの開始年（4 桁、未解決時は空文字）。テンプレで「(2004)」のように添える。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>並び替え用のシリーズ開始日原値（テンプレでは未参照・旧ソート用に残置）。</summary>
        public DateOnly? SeriesStartDateRaw { get; set; }
        /// <summary>並び替え用：代表録音の recording_id（テンプレでは未参照）。歌唱を含む曲は歌った録音、作詞作曲のみは曲の最古録音の id。</summary>
        public int SortRecordingId { get; set; }
        /// <summary>楽曲種別ラベル（OP / ED / イメージソング 等。代表録音の music_class_code 由来。未設定なら空文字）。</summary>
        public string MusicClassLabel { get; set; } = "";
        /// <summary>楽曲種別バッジ用クラス末尾（"op" / "movie-ed" 等。CSS の .songs-badge-{ここ} / .cat-{ここ} に対応。未設定なら空文字）。</summary>
        public string BadgeClassSuffix { get; set; } = "";
        /// <summary>当該曲での担当役職バッジ群（role_map.display_order 昇順）。</summary>
        public IReadOnlyList<RoleBadgeView> Roles { get; set; } = Array.Empty<RoleBadgeView>();
    }

    /// <summary>役職バッジ 1 個。役職コード（CSS の <c>data-role-code</c> に渡す）、表示ラベル、 役職統計ページ URL を持つ。担当楽曲カードのほか、必要に応じて他セクションでも流用できる素直な DTO。</summary>
    private sealed class RoleBadgeView
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>並び替え用の表示順（role_map.display_order）。テンプレでは未参照。</summary>
        public int DisplayOrder { get; set; }
    }

    private sealed class PersonAliasView
    {
        public int AliasId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string ValidFrom { get; set; } = "";
        public string ValidTo { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}