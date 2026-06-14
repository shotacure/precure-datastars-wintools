using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>キャラクター索引（/characters/）と詳細（/characters/{character_id}/）の生成。</summary>
public sealed class CharactersGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    /// <summary>character_alias_id → (song_id, role_code)。キャラが CHARACTER_WITH_CV で歌った録音を、当該キャラの
    /// 担当として集約した「楽曲」セクション用索引。<c>GenerateAsync</c> で 1 度だけ詰めて使い回す。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>>? _charSongRolesByAlias;

    /// <summary>character_alias_id → (song_id → そのキャラが歌った録音)。出典シリーズ・版（VariantLabel）を録音から
    /// 正確に解決するための索引。同一曲を複数録音で歌っている場合は出典が最も早い録音を採る。</summary>
    private IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>>? _charSungRecordingByAlias;

    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly CharacterFamilyRelationsRepository _familyRepo;
    private readonly CharacterRelationKindsRepository _relationKindsRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    // プリキュア固有プロフィール（4 区分名義 / 学校 / 学年・組 / 家業 / 専属声優）を
    // キャラクター詳細ページ内に統合表示するための追加リポジトリ。
    // 旧来は PrecuresGenerator が同情報を `/precures/{id}/` に出していたが、PRECURE 種別の
    // キャラクター詳細ページが 100% 上位互換となるよう、本ジェネレータからも引く。
    private readonly PrecuresRepository _precuresRepo;
    private readonly PersonsRepository _personsRepo;

    public CharactersGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _charactersRepo = new CharactersRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _familyRepo = new CharacterFamilyRelationsRepository(factory);
        _relationKindsRepo = new CharacterRelationKindsRepository(factory);
        _characterKindsRepo = new CharacterKindsRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _precuresRepo = new PrecuresRepository(factory);
        _personsRepo = new PersonsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating characters");

        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allKinds = (await _characterKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allRelationKinds = (await _relationKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        // プリキュア固有プロフィールを詰めるため、precures と persons を 1 度だけロード。
        // precures は trigger により 4 名義 FK が同一 character_id を指す業務ルールを満たすので、
        // transform_alias_id から character_id を逆引きして「キャラ → 紐付く precure」辞書を組む。
        var allPrecures = (await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        var charactersById = allCharacters.ToDictionary(c => c.CharacterId);
        var aliasesByCharacter = allAliases.GroupBy(a => a.CharacterId).ToDictionary(g => g.Key, g => g.ToList());
        var aliasById = allAliases.ToDictionary(a => a.AliasId);
        var kindMap = allKinds.ToDictionary(k => k.CharacterKindCode, k => k, StringComparer.Ordinal);
        var relationKindMap = allRelationKinds.ToDictionary(r => r.RelationCode, r => r, StringComparer.Ordinal);
        var personAliasById = allPersonAliases.ToDictionary(a => a.AliasId);
        var personsById = allPersons.ToDictionary(p => p.PersonId);
        // character_id → Precure の辞書（transform_alias_id 由来の character_id でキーリング）。
        // 同一キャラに複数 precure が紐付くことは仕様上ないが、念のため最小 PrecureId を優先採用する。
        var precureByCharacter = new Dictionary<int, Precure>();
        foreach (var pr in allPrecures.OrderBy(p => p.PrecureId))
        {
            if (!aliasById.TryGetValue(pr.TransformAliasId, out var ta)) continue;
            if (!precureByCharacter.ContainsKey(ta.CharacterId))
            {
                precureByCharacter[ta.CharacterId] = pr;
            }
        }

        // 「楽曲」セクション用：character_alias → 歌った曲 / 録音 の索引を 1 度だけ前計算。
        // song_recording_singers の CHARACTER_WITH_CV（主名義 character_alias_id ＋ スラッシュ相方
        // slash_character_alias_id）から、キャラが歌った曲（role_code）と、出典・版の解決に使う「歌った録音」を集約する。
        if (_charSongRolesByAlias is null)
        {
            var rolesBucket = new Dictionary<int, List<(int SongId, string RoleCode)>>();
            var sungRecBucket = new Dictionary<int, Dictionary<int, SongRecording>>();
            void AddCharRole(int caId, int songId, string roleCode)
            {
                if (!rolesBucket.TryGetValue(caId, out var list)) { list = new List<(int, string)>(); rolesBucket[caId] = list; }
                list.Add((songId, roleCode));
            }
            void AddCharRec(int caId, SongRecording rec)
            {
                if (!sungRecBucket.TryGetValue(caId, out var bySong)) { bySong = new Dictionary<int, SongRecording>(); sungRecBucket[caId] = bySong; }
                if (!bySong.TryGetValue(rec.SongId, out var existing)
                    || RecordingSeriesStart(rec) < RecordingSeriesStart(existing))
                    bySong[rec.SongId] = rec;
            }
            foreach (var (recId, singers) in _ctx.SingersByRecording)
            {
                if (!_ctx.SongRecordingById.TryGetValue(recId, out var rec)) continue;
                foreach (var s in singers)
                {
                    if (s.CharacterAliasId.HasValue) { AddCharRole(s.CharacterAliasId.Value, rec.SongId, s.RoleCode); AddCharRec(s.CharacterAliasId.Value, rec); }
                    if (s.SlashCharacterAliasId.HasValue) { AddCharRole(s.SlashCharacterAliasId.Value, rec.SongId, s.RoleCode); AddCharRec(s.SlashCharacterAliasId.Value, rec); }
                }
            }
            _charSongRolesByAlias = rolesBucket.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(int SongId, string RoleCode)>)kv.Value);
            _charSungRecordingByAlias = sungRecBucket.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, SongRecording>)kv.Value);
        }

        // 索引ページ。
        GenerateIndex(allCharacters, aliasesByCharacter, kindMap);

        // 詳細ページ。
        foreach (var c in allCharacters)
        {
            await GenerateDetailAsync(c, aliasesByCharacter, aliasById, charactersById,
                kindMap, relationKindMap, personAliasById,
                precureByCharacter, personsById, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"characters: {allCharacters.Count + 1} ページ");
    }

    /// <summary><c>/characters/</c>（キャラクター索引）。所属シリーズ（最早登場シリーズ）で大セクションに分け、シリーズ内は種別（character_kind）サブセクション → クレジット順で並べる。所属未確定（クレジット皆無）は末尾「その他」。</summary>
    private void GenerateIndex(
        IReadOnlyList<Character> characters,
        IReadOnlyDictionary<int, List<CharacterAlias>> aliasesByCharacter,
        IReadOnlyDictionary<string, CharacterKind> kindMap)
    {
        // 各キャラの登場量を TV 系（話）と映画系（本）で分けて集計する。
        // 名義（alias）跨ぎの重複は (SeriesId, EpisodeNo) / SeriesId 単位で排除する。
        // TV 系（series_kinds.credit_attach_to='EPISODE'）：登場話数を加算。
        // 映画系（'SERIES'：MOVIE / MOVIE_SHORT / SPRING / EVENT）：当該シリーズに関与が 1 件以上で 1 本。
        (int Episode, int Movie) CountAppearances(int characterId)
        {
            if (!aliasesByCharacter.TryGetValue(characterId, out var aliases)) return (0, 0);
            var tvEpisodes = new HashSet<(int SeriesId, int EpNo)>();
            var movieSeries = new HashSet<int>();
            foreach (var a in aliases)
            {
                if (!_index.ByCharacterAlias.TryGetValue(a.AliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!_ctx.SeriesById.TryGetValue(inv.SeriesId, out var series)) continue;
                    bool isMovieKind = _ctx.SeriesKindByCode.TryGetValue(series.KindCode, out var sk)
                                       && string.Equals(sk.CreditAttachTo, "SERIES", StringComparison.Ordinal);
                    if (isMovieKind)
                    {
                        movieSeries.Add(inv.SeriesId);
                    }
                    else if (inv.EpisodeId is int eid)
                    {
                        var ep = _ctx.LookupEpisode(inv.SeriesId, eid);
                        if (ep is not null) tvEpisodes.Add((inv.SeriesId, ep.SeriesEpNo));
                    }
                }
            }
            return (tvEpisodes.Count, movieSeries.Count);
        }

        // クレジットに 1 件でも登場するか（リンクの下線シグナル分け用）。
        bool HasAnyInvolvement(int characterId) =>
            aliasesByCharacter.TryGetValue(characterId, out var aliases)
            && aliases.Any(a => _index.ByCharacterAlias.ContainsKey(a.AliasId));

        // 各キャラの「最も早くクレジットされた位置」と、その所属（＝最早登場）シリーズ ID を求める。
        // 大セクションはこの所属シリーズで束ね、シリーズ内の並びはクレジット順に統一する。
        // CreditSeq は CreditInvolvementIndex がクレジット階層の表示順
        // （credit_kind → credit_id → card_seq → … → entry_seq）で採番した 0 始まりの出現連番。
        // クレジットが一切無いキャラは SeriesId=0（末尾「その他」セクション送り）。
        (long Start, int EpNo, int Seq, int SeriesId) FirstCreditKey(int characterId)
        {
            long bestStart = long.MaxValue;
            int bestEpNo = int.MaxValue;
            int bestSeq = int.MaxValue;
            int bestSeriesId = 0;
            if (!aliasesByCharacter.TryGetValue(characterId, out var aliases))
                return (bestStart, bestEpNo, bestSeq, bestSeriesId);

            foreach (var a in aliases)
            {
                if (!_index.ByCharacterAlias.TryGetValue(a.AliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    long start = _ctx.SeriesStartDate(inv.SeriesId).DayNumber;
                    int epNo = inv.EpisodeId is int eid
                        ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
                        : 0; // シリーズスコープは最早扱い
                    int seq = inv.CreditSeq;
                    if (start < bestStart
                        || (start == bestStart && epNo < bestEpNo)
                        || (start == bestStart && epNo == bestEpNo && seq < bestSeq))
                    {
                        bestStart = start;
                        bestEpNo = epNo;
                        bestSeq = seq;
                        bestSeriesId = inv.SeriesId;
                    }
                }
            }
            return (bestStart, bestEpNo, bestSeq, bestSeriesId);
        }

        // 各キャラを 1 度だけ集計してワーキングエントリ化（重い lookup の二度引きを避ける）。
        var entries = characters
            .Select(c =>
            {
                var (ep, mv) = CountAppearances(c.CharacterId);
                return (Ch: c, Key: FirstCreditKey(c.CharacterId), Ep: ep, Mv: mv, Has: HasAnyInvolvement(c.CharacterId));
            })
            .ToList();

        byte KindOrder(string kindCode) =>
            kindMap.TryGetValue(kindCode, out var k) ? (k.DisplayOrder ?? byte.MaxValue) : byte.MaxValue;
        string KindLabel(string kindCode) =>
            kindMap.TryGetValue(kindCode, out var k) ? k.NameJa : kindCode;

        // 大セクション = 所属シリーズ（最早登場シリーズ）。放送開始日昇順、所属未確定は末尾「その他」。
        // シリーズ内は種別サブセクション（kind マスタ display_order 順）→ クレジット順で並べる。
        var sections = entries
            .GroupBy(e => e.Key.SeriesId)
            .Select(g =>
            {
                int seriesId = g.Key;
                bool isOther = seriesId == 0 || !_ctx.SeriesById.ContainsKey(seriesId);
                var series = isOther ? null : _ctx.SeriesById[seriesId];

                var kindGroups = g
                    .GroupBy(e => e.Ch.CharacterKind)
                    .OrderBy(kg => KindOrder(kg.Key))
                    .ThenBy(kg => kg.Key, StringComparer.Ordinal)
                    .Select(kg => new CharacterKindSubsection
                    {
                        KindLabel = KindLabel(kg.Key),
                        Members = kg
                            // クレジット順（最早 → 同話数内はクレジット出現位置順）。
                            // 完全同点・クレジット皆無時は読み仮名→名前で安定化する。
                            .OrderBy(x => x.Key.Start)
                            .ThenBy(x => x.Key.EpNo)
                            .ThenBy(x => x.Key.Seq)
                            .ThenBy(x => string.IsNullOrEmpty(x.Ch.NameKana) ? 1 : 0)
                            .ThenBy(x => x.Ch.NameKana ?? "", StringComparer.Ordinal)
                            .ThenBy(x => x.Ch.Name, StringComparer.Ordinal)
                            .Select(x => new CharacterIndexRow
                            {
                                CharacterId = x.Ch.CharacterId,
                                Name = x.Ch.Name,
                                NameKana = x.Ch.NameKana ?? "",
                                EpisodeCount = x.Ep,
                                MovieCount = x.Mv,
                                HasInvolvement = x.Has
                            })
                            .ToList()
                    })
                    .ToList();

                return new
                {
                    IsOther = isOther,
                    SeriesStart = isOther ? long.MaxValue : _ctx.SeriesStartDate(seriesId).DayNumber,
                    Section = new CharacterSeriesSection
                    {
                        SeriesTitle = isOther ? "その他（未登場）" : series!.Title,
                        SeriesSlug = isOther ? "" : series!.Slug,
                        SeriesStartYearLabel = isOther ? "" : series!.StartDate.Year.ToString(),
                        MemberCount = g.Count(),
                        KindGroups = kindGroups
                    }
                };
            })
            .OrderBy(s => s.IsOther ? 1 : 0)
            .ThenBy(s => s.SeriesStart)
            .Select(s => s.Section)
            .ToList();

        var content = new CharactersIndexModel
        {
            Sections = sections,
            TotalCount = characters.Count,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代キャラクター",
            MetaDescription = "プリキュアたちから妖精・敵キャラ・ゲストまで、歴代シリーズに登場するキャラクターを作品別に一覧にしました。担当声優や登場話数も確認できます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代キャラクター", Url = "" }
            }
        };
        _page.RenderAndWrite("/characters/", "characters", "characters-index.sbn", content, layout);
    }

    /// <summary><c>/characters/{character_id}/</c>（キャラクター詳細）。</summary>
    private async Task GenerateDetailAsync(
        Character character,
        IReadOnlyDictionary<int, List<CharacterAlias>> aliasesByCharacter,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Character> charactersById,
        IReadOnlyDictionary<string, CharacterKind> kindMap,
        IReadOnlyDictionary<string, CharacterRelationKind> relationKindMap,
        IReadOnlyDictionary<int, PersonAlias> personAliasById,
        IReadOnlyDictionary<int, Precure> precureByCharacter,
        IReadOnlyDictionary<int, Person> personsById,
        CancellationToken ct)
    {
        // DB アクセスはすべて事前展開された BuildContext 由来の辞書 lookup に置き換わったため
        // 本メソッド本体に await は残らないが、async シグネチャは将来の DB アクセス追加余地として温存する。
        await Task.CompletedTask;

        var aliases = aliasesByCharacter.TryGetValue(character.CharacterId, out var lst)
            ? lst
            : new List<CharacterAlias>();

        // 名義一覧。alias_id 昇順（時系列情報は持たない仕様）。
        var aliasRows = aliases
            .OrderBy(a => a.AliasId)
            .Select(a => new CharacterAliasRow
            {
                Name = a.Name,
                NameKana = a.NameKana ?? "",
                NameEn = a.NameEn ?? "",
                Notes = a.Notes ?? ""
            })
            .ToList();

        // 家族関係は BuildContext.FamilyRelationsByCharacter で全件辞書化済み。
        // per-character の GetByCharacterAsync を発火せず辞書参照で取得する。
        var fams = _ctx.FamilyRelationsByCharacter.TryGetValue(character.CharacterId, out var familyList)
            ? familyList
            : (IReadOnlyList<CharacterFamilyRelation>)Array.Empty<CharacterFamilyRelation>();
        var familyRows = fams
            .OrderBy(f => f.DisplayOrder ?? byte.MaxValue)
            .Where(f => charactersById.ContainsKey(f.RelatedCharacterId))
            .Select(f => new FamilyRelationRow
            {
                RelationLabel = relationKindMap.TryGetValue(f.RelationCode, out var rk) ? rk.NameJa : f.RelationCode,
                RelatedCharacterId = f.RelatedCharacterId,
                RelatedCharacterName = charactersById[f.RelatedCharacterId].Name,
                Notes = f.Notes ?? ""
            })
            .ToList();

        // 声の出演履歴：当該キャラの全 alias_id の Involvement を合算。
        var aliasIds = aliases.Select(a => a.AliasId).ToList();
        var voiceRows = BuildVoiceCastRows(aliasIds, aliases.ToDictionary(a => a.AliasId), personAliasById);

        string kindLabel = kindMap.TryGetValue(character.CharacterKind, out var kk) ? kk.NameJa : character.CharacterKind;

        // 誕生日表記（誕生日のあるキャラはすべて表示。生年 PUBLIC なら年付き、それ以外は月日のみ）。
        string birthday = FormatBirthday(character);

        // PRECURE 種別かつ precures に紐付くキャラのみ、プリキュア固有プロフィール（4 区分名義 /
        // 学校 / 学年・組 / 家業 / 専属声優）を追加で詰める。それ以外は null（テンプレ側で非表示）。
        PrecureProfileView? precureProfile = null;
        if (string.Equals(character.CharacterKind, "PRECURE", StringComparison.Ordinal)
            && precureByCharacter.TryGetValue(character.CharacterId, out var precure))
        {
            precureProfile = BuildPrecureProfile(precure, aliasById, personsById);
        }

        var content = new CharacterDetailModel
        {
            Character = new CharacterView
            {
                CharacterId = character.CharacterId,
                Name = character.Name,
                NameKana = character.NameKana ?? "",
                NameEn = character.NameEn ?? "",
                KindLabel = kindLabel,
                Birthday = birthday,
                Notes = character.Notes ?? "",
                OfficialUrl = character.OfficialUrl ?? ""
            },
            Aliases = aliasRows,
            FamilyRelations = familyRows,
            VoiceCastRows = voiceRows,
            // このキャラが CHARACTER_WITH_CV で歌った楽曲（録音単位で出典・版を正確に解決）。
            SongCards = BuildCharacterSongCards(aliasIds),
            PrecureProfile = precureProfile,
            CoverageLabel = _ctx.CreditCoverageLabel
        };

        // MetaDescription を実データから動的構築する。
        var metaDescription = BuildCharacterMetaDescription(character.Name, kindLabel, voiceRows);

        // キャラクター詳細の構造化データは Schema.org の Person 型で代用する。
        string baseUrl = _ctx.Config.BaseUrl;
        string characterUrl = PathUtil.CharacterUrl(character.CharacterId);
        var alternateNames = aliasRows
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, character.Name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["name"] = character.Name,
            ["description"] = metaDescription
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (!string.IsNullOrEmpty(character.NameEn)) jsonLdDict["givenName"] = character.NameEn;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + characterUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = character.Name,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代キャラクター", Url = "/characters/" },
                new BreadcrumbItem { Label = character.Name, Url = "" }
            },
            OgType = "profile",
            JsonLd = jsonLd
        };
        _page.RenderAndWrite(PathUtil.CharacterUrl(character.CharacterId), "characters", "characters-detail.sbn", content, layout);
    }

    /// <summary>
    /// キャラクター詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// 構成：「{キャラ名}は、プリキュアシリーズに登場する{キャラ種別}。CV:{声優1}、{声優2}など。{N作品}に出演。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
    /// 声優名は <see cref="VoiceCastRow.VoiceActorNames"/>（連名連結）を「、」で割って重複排除し、最大 2 名。
    /// 出演シリーズ数は <see cref="VoiceCastRow.SeriesTitle"/> の Distinct カウント。
    /// </summary>
    private static string BuildCharacterMetaDescription(
        string characterName,
        string kindLabel,
        IReadOnlyList<VoiceCastRow> voiceRows)
    {
        const int targetMaxChars = 140;
        var sb = new System.Text.StringBuilder();

        sb.Append(characterName).Append("は、プリキュアシリーズに登場");
        if (!string.IsNullOrWhiteSpace(kindLabel))
        {
            sb.Append("する").Append(kindLabel);
        }
        else
        {
            sb.Append("するキャラクター");
        }
        sb.Append("。");

        // 声優 CV（最大 2 名）。VoiceActorNames は「、」連結のフリーテキストなので分割する。
        var allVoiceActors = voiceRows
            .SelectMany(v => string.IsNullOrEmpty(v.VoiceActorNames)
                ? Array.Empty<string>()
                : v.VoiceActorNames.Split('、', StringSplitOptions.RemoveEmptyEntries))
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (allVoiceActors.Count > 0)
        {
            var pickedVoices = allVoiceActors.Take(2).ToList();
            var fragment = "CV:" + string.Join("、", pickedVoices);
            if (allVoiceActors.Count > 2) fragment += "など";
            fragment += "。";
            if (sb.Length + fragment.Length <= targetMaxChars) sb.Append(fragment);
        }

        // 出演シリーズ数（VoiceCastRow はシリーズ単位 1 行のため、行数 = 出演シリーズ数）。
        int seriesCount = voiceRows
            .Select(v => v.SeriesTitle)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (seriesCount > 0)
        {
            var fragment = $"{seriesCount}作品に出演。";
            if (sb.Length + fragment.Length <= targetMaxChars) sb.Append(fragment);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 誕生日表記を組み立てる。 BirthYearVisibility=PUBLIC かつ BirthYear ありなら「YYYY年M月D日」、
    /// 非公開もしくは未設定なら年抜きの「M月D日」。BirthMonth / BirthDay の片方でも未設定なら
    /// 空文字を返す（誕生日行を出さない）。フィクションキャラは生年を持たないことが多いので
    /// 大半は「M月D日」になる。
    /// </summary>
    private static string FormatBirthday(Character c)
    {
        if (c.BirthMonth is not byte m || c.BirthDay is not byte d) return "";
        if (c.BirthYear is ushort y
            && string.Equals(c.BirthYearVisibility, "PUBLIC", StringComparison.Ordinal))
        {
            return $"{y}年{m}月{d}日";
        }
        return $"{m}月{d}日";
    }

    /// <summary>
    /// PRECURE 種別キャラの精細プロフィール（4 区分名義 / 学校 / 学年・組 / 家業 / 専属声優）を
    /// 詰めた DTO を作る。precures テーブルは trigger により 4 名義 FK が同一 character_id を
    /// 指す業務ルールを持つので、4 alias_id をそれぞれ aliasById で解決して並べる。
    /// 専属声優は precures.voice_actor_person_id（Person ID）→ persons.full_name で解決。
    /// </summary>
    private static PrecureProfileView BuildPrecureProfile(
        Precure precure,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Person> personsById)
    {
        var aliasEntries = new List<PrecureAliasEntry>();
        AppendPrecureAlias(aliasEntries, "変身前", precure.PreTransformAliasId, aliasById);
        AppendPrecureAlias(aliasEntries, "変身後", precure.TransformAliasId, aliasById);
        if (precure.Transform2AliasId is int t2)
            AppendPrecureAlias(aliasEntries, "変身後 2", t2, aliasById);
        if (precure.AltFormAliasId is int alt)
            AppendPrecureAlias(aliasEntries, "別形態", alt, aliasById);

        string voiceActorName = "";
        int? voiceActorPersonId = precure.VoiceActorPersonId;
        if (voiceActorPersonId is int vid && personsById.TryGetValue(vid, out var vp))
        {
            voiceActorName = vp.FullName;
        }

        return new PrecureProfileView
        {
            PrecureId = precure.PrecureId,
            AliasEntries = aliasEntries,
            VoiceActorName = voiceActorName,
            VoiceActorPersonId = voiceActorPersonId,
            School = precure.School ?? "",
            SchoolClass = precure.SchoolClass ?? "",
            FamilyBusiness = precure.FamilyBusiness ?? ""
        };
    }

    private static void AppendPrecureAlias(
        List<PrecureAliasEntry> sink,
        string label,
        int aliasId,
        IReadOnlyDictionary<int, CharacterAlias> aliasById)
    {
        if (aliasById.TryGetValue(aliasId, out var a))
        {
            sink.Add(new PrecureAliasEntry
            {
                Label = label,
                Name = a.Name,
                NameKana = a.NameKana ?? ""
            });
        }
    }

    /// <summary>全名義の character_alias_id に紐付く CHARACTER_VOICE Involvement を集約。</summary>
    /// <summary>録音の出典シリーズ開始日。出典が無い録音は末尾扱い（<see cref="DateOnly.MaxValue"/>）。「歌った録音」選択に使う。</summary>
    private DateOnly RecordingSeriesStart(SongRecording rec)
        => rec.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var s) ? s.StartDate : DateOnly.MaxValue;

    /// <summary>当該キャラの名義群のうち、指定曲を歌った録音を返す（複数あれば出典シリーズが最も早いもの）。歌っていなければ null。</summary>
    private SongRecording? ResolveCharSungRecording(IReadOnlyList<int> aliasIds, int songId)
    {
        if (_charSungRecordingByAlias is null) return null;
        SongRecording? best = null;
        foreach (var aliasId in aliasIds)
        {
            if (_charSungRecordingByAlias.TryGetValue(aliasId, out var bySong)
                && bySong.TryGetValue(songId, out var rec)
                && (best is null || RecordingSeriesStart(rec) < RecordingSeriesStart(best)))
            {
                best = rec;
            }
        }
        return best;
    }

    /// <summary>キャラが CHARACTER_WITH_CV で歌った楽曲を 1 曲 = 1 カードに集約する。
    /// 出典シリーズ・タイトルは「キャラが歌った録音」から解決する（録音ごとに出典・版が異なり得るため）。
    /// 並びは「シリーズ開始年昇順 → 曲タイトル昇順」、出典不明は末尾。</summary>
    private IReadOnlyList<CharacterSongCard> BuildCharacterSongCards(IReadOnlyList<int> aliasIds)
    {
        if (aliasIds.Count == 0 || _charSongRolesByAlias is null) return Array.Empty<CharacterSongCard>();

        var rolesBySong = new Dictionary<int, HashSet<string>>();
        foreach (var aliasId in aliasIds)
        {
            if (!_charSongRolesByAlias.TryGetValue(aliasId, out var rows)) continue;
            foreach (var (songId, roleCode) in rows)
            {
                if (!rolesBySong.TryGetValue(songId, out var set)) { set = new HashSet<string>(StringComparer.Ordinal); rolesBySong[songId] = set; }
                set.Add(roleCode);
            }
        }
        if (rolesBySong.Count == 0) return Array.Empty<CharacterSongCard>();

        var cards = new List<CharacterSongCard>(rolesBySong.Count);
        foreach (var (songId, roleSet) in rolesBySong)
        {
            if (!_ctx.SongById.TryGetValue(songId, out var song)) continue;

            // 出典シリーズ・タイトルは、このキャラが歌った録音から解決する。
            var sungRec = ResolveCharSungRecording(aliasIds, songId);
            Series? series = null;
            string title = song.Title;
            if (sungRec is not null)
            {
                if (sungRec.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var s)) series = s;
                // VariantLabel は録音のフル表示タイトル（曲名＋版）。あればそれをそのまま歌った録音のタイトルにする。
                if (!string.IsNullOrEmpty(sungRec.VariantLabel)) title = sungRec.VariantLabel;
            }

            var roleBadges = roleSet
                .Select(code => new SongRoleBadge
                {
                    Code = code,
                    Label = _ctx.RoleByCode.TryGetValue(code, out var r) ? (r.NameJa ?? code) : code,
                    DisplayOrder = _ctx.RoleByCode.TryGetValue(code, out var r2) && r2.DisplayOrder is ushort d ? d : int.MaxValue
                })
                .OrderBy(b => b.DisplayOrder)
                .ThenBy(b => b.Code, StringComparer.Ordinal)
                .ToList();

            cards.Add(new CharacterSongCard
            {
                SongUrl = PathUtil.SongUrl(songId),
                Title = title,
                SeriesTitle = series?.Title ?? "",
                SeriesUrl = series is null ? "" : PathUtil.SeriesUrl(series.Slug),
                SeriesStartYearLabel = series?.StartDate.Year.ToString() ?? "",
                SeriesStartDateRaw = series?.StartDate,
                Roles = roleBadges
            });
        }

        return cards
            .OrderBy(c => c.SeriesStartDateRaw is null ? 1 : 0)
            .ThenBy(c => c.SeriesStartDateRaw)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ToList();
    }

    private List<VoiceCastRow> BuildVoiceCastRows(
        IReadOnlyList<int> aliasIds,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, PersonAlias> personAliasById)
    {
        var all = aliasIds
            .Where(_index.ByCharacterAlias.ContainsKey)
            .SelectMany(id => _index.ByCharacterAlias[id])
            .ToList();
        if (all.Count == 0) return new List<VoiceCastRow>();

        var rows = new List<VoiceCastRow>();
        foreach (var bySeries in all.GroupBy(i => i.SeriesId).OrderBy(g => _ctx.SeriesStartDate(g.Key)))
        {
            if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;

            // シリーズ内全話数（全話判定用）
            var allSeriesEpNos = _ctx.EpisodesBySeries.TryGetValue(bySeries.Key, out var allEps)
                ? allEps.Select(e => e.SeriesEpNo).ToList()
                : new List<int>();

            // エピソード単位とシリーズ全体スコープを分けて処理
            var episodeNos = new HashSet<int>();
            bool hasSeriesScope = false;

            // 声優名・キャラ名義名を集約用ハッシュ
            var actorNames = new List<string>();
            var seenActor = new HashSet<string>(StringComparer.Ordinal);
            var aliasNames = new List<string>();
            var seenAlias = new HashSet<string>(StringComparer.Ordinal);
            var seriesScopeActorNames = new List<string>();
            var seenScopeActor = new HashSet<string>(StringComparer.Ordinal);
            var seriesScopeAliasNames = new List<string>();
            var seenScopeAlias = new HashSet<string>(StringComparer.Ordinal);

            foreach (var inv in bySeries)
            {
                bool isSeriesScope = !inv.EpisodeId.HasValue;
                if (isSeriesScope)
                {
                    hasSeriesScope = true;
                }
                else
                {
                    var ep = _ctx.LookupEpisode(bySeries.Key, inv.EpisodeId!.Value);
                    if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                }

                if (inv.PersonAliasId is int paid && personAliasById.TryGetValue(paid, out var pa))
                {
                    string nm = pa.DisplayTextOverride ?? pa.Name;
                    if (!string.IsNullOrEmpty(nm))
                    {
                        if (isSeriesScope) { if (seenScopeActor.Add(nm)) seriesScopeActorNames.Add(nm); }
                        else               { if (seenActor.Add(nm))      actorNames.Add(nm); }
                    }
                }
                if (inv.CharacterAliasId is int caid && aliasById.TryGetValue(caid, out var ca))
                {
                    if (isSeriesScope) { if (seenScopeAlias.Add(ca.Name)) seriesScopeAliasNames.Add(ca.Name); }
                    else               { if (seenAlias.Add(ca.Name))      aliasNames.Add(ca.Name); }
                }
            }

            // シリーズ全体スコープ行（あれば先）
            if (hasSeriesScope)
            {
                rows.Add(new VoiceCastRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    RangeLabel = "（シリーズ全体）",
                    IsAllEpisodes = false,
                    AliasNames = string.Join("、", seriesScopeAliasNames),
                    VoiceActorNames = string.Join("、", seriesScopeActorNames)
                });
            }

            // エピソード単位の集約 1 行
            if (episodeNos.Count > 0)
            {
                bool isAll = allSeriesEpNos.Count > 0
                    && episodeNos.SetEquals(allSeriesEpNos);
                string rangeLabel = isAll
                    ? string.Empty
                    : EpisodeRangeCompressor.Compress(episodeNos);

                rows.Add(new VoiceCastRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    SeriesStartYearLabel = series.StartDate.Year.ToString(),
                    RangeLabel = rangeLabel,
                    IsAllEpisodes = isAll,
                    AliasNames = string.Join("、", aliasNames),
                    VoiceActorNames = string.Join("、", actorNames)
                });
            }
        }
        return rows;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class CharactersIndexModel
    {
        /// <summary>大セクション = 所属シリーズ（最早登場シリーズ）。放送開始日昇順、末尾に「その他（未登場）」。</summary>
        public IReadOnlyList<CharacterSeriesSection> Sections { get; set; } = Array.Empty<CharacterSeriesSection>();
        public int TotalCount { get; set; }
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の lead 段落末尾に表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>シリーズ単位の大セクション。配下に種別サブセクションをぶら下げる。</summary>
    private sealed class CharacterSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズ詳細リンク用 slug（「その他」セクションは空）。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>放送開始年の西暦 4 桁（セクションナビの年バッジ用。「その他」は空）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>当該シリーズに属するキャラ総数（種別横断、セクションナビの件数用）。</summary>
        public int MemberCount { get; set; }
        public IReadOnlyList<CharacterKindSubsection> KindGroups { get; set; } = Array.Empty<CharacterKindSubsection>();
    }

    /// <summary>シリーズ大セクション内の種別サブセクション。</summary>
    private sealed class CharacterKindSubsection
    {
        public string KindLabel { get; set; } = "";
        public IReadOnlyList<CharacterIndexRow> Members { get; set; } = Array.Empty<CharacterIndexRow>();
    }

    private sealed class CharacterIndexRow
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        /// <summary>TV 系シリーズ（credit_attach_to='EPISODE'）での登場話数合計（全作品横断）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>映画系シリーズ（credit_attach_to='SERIES'）での登場本数（1 シリーズ = 1 本、全作品横断）。</summary>
        public int MovieCount { get; set; }
        /// <summary>"登場 N 話・M 本" などの動詞つき単位表記。両方ゼロなら空文字。
        /// 「登場」を冠して、エピソードの話数（#N・第N話）と数量の「N 話」を読み分けられるようにする。</summary>
        public string CountLabel => (EpisodeCount, MovieCount) switch
        {
            ( > 0, > 0) => $"登場 {EpisodeCount} 話・{MovieCount} 本",
            ( > 0, 0)   => $"登場 {EpisodeCount} 話",
            (0,   > 0) => $"登場 {MovieCount} 本",
            _           => ""
        };
        /// <summary>登場数バッジ（📺話・🎥本のピル）の前に冠する動詞。キャラクター索引では常に「登場」。</summary>
        public string CountVerb => "登場";
        public bool HasInvolvement { get; set; }
    }

    private sealed class CharacterDetailModel
    {
        public CharacterView Character { get; set; } = new();
        public IReadOnlyList<CharacterAliasRow> Aliases { get; set; } = Array.Empty<CharacterAliasRow>();
        public IReadOnlyList<FamilyRelationRow> FamilyRelations { get; set; } = Array.Empty<FamilyRelationRow>();
        public IReadOnlyList<VoiceCastRow> VoiceCastRows { get; set; } = Array.Empty<VoiceCastRow>();
        /// <summary>このキャラが CHARACTER_WITH_CV で歌った楽曲（1 曲 = 1 カード、出典・版は歌った録音から解決）。 0 件のときはテンプレ側でセクションごと非表示。</summary>
        public IReadOnlyList<CharacterSongCard> SongCards { get; set; } = Array.Empty<CharacterSongCard>();
        /// <summary>PRECURE 種別かつ precures レコードに紐付くキャラのみ詰まる。 それ以外は null（テンプレ側でプリキュア情報セクションごと非表示）。</summary>
        public PrecureProfileView? PrecureProfile { get; set; }
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の h1 ブロック直後に独立段落で表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>キャラが歌った楽曲カード 1 件（人物詳細の <c>person-song-card</c> マークアップを共用）。</summary>
    private sealed class CharacterSongCard
    {
        public string SongUrl { get; set; } = "";
        public string Title { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>並べ替え用のシリーズ開始日（テンプレ未使用）。出典不明は null。</summary>
        public DateOnly? SeriesStartDateRaw { get; set; }
        public IReadOnlyList<SongRoleBadge> Roles { get; set; } = Array.Empty<SongRoleBadge>();
    }

    /// <summary>楽曲カードの役職バッジ（歌 / コーラス）。表示は role_code とラベルのみ（display_order は並べ替え用）。</summary>
    private sealed class SongRoleBadge
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public int DisplayOrder { get; set; }
    }

    private sealed class CharacterView
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string KindLabel { get; set; } = "";
        /// <summary>誕生日表記（「YYYY年M月D日」または「M月D日」、未設定時は空文字）。生年は BirthYearVisibility=PUBLIC のときだけ年付き表記。</summary>
        public string Birthday { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>キャラクター公式ページ URL。詳細ページ末尾「外部リンク」セクションに出す。 Wikipedia は内部値として保持はするがサイト UI からはリンクしない方針なので、 ここでは敢えて出していない。</summary>
        public string OfficialUrl { get; set; } = "";
    }

    /// <summary>PRECURE 種別キャラの精細プロフィール DTO。precures テーブル固有列（4 区分名義 / 学校 / 学年・組 / 家業 / 専属声優）をまとめて持つ。</summary>
    private sealed class PrecureProfileView
    {
        public int PrecureId { get; set; }
        public IReadOnlyList<PrecureAliasEntry> AliasEntries { get; set; } = Array.Empty<PrecureAliasEntry>();
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
        public string School { get; set; } = "";
        public string SchoolClass { get; set; } = "";
        public string FamilyBusiness { get; set; } = "";
    }

    private sealed class PrecureAliasEntry
    {
        public string Label { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
    }

    private sealed class CharacterAliasRow
    {
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class FamilyRelationRow
    {
        public string RelationLabel { get; set; } = "";
        public int RelatedCharacterId { get; set; }
        public string RelatedCharacterName { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    /// <summary>キャラ詳細の声優出演 1 行（シリーズ単位 + 話数圧縮）。</summary>
    private sealed class VoiceCastRow
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。 声の出演履歴リストでシリーズ名の隣に薄色括弧で添える表現に使う。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>話数圧縮表記。例：「#1〜4, 8」。全話のときは空文字（テンプレ側で「(全話)」マークを別表示）。 シリーズ全体スコープのときは「（シリーズ全体）」のような任意ラベルを入れる。</summary>
        public string RangeLabel { get; set; } = "";
        /// <summary>シリーズ内の全話を担当しているフラグ。テンプレで「(全話)」マークを出すかの判定。</summary>
        public bool IsAllEpisodes { get; set; }
        /// <summary>シリーズ内で使用されたキャラ名義（連名は「、」連結）。</summary>
        public string AliasNames { get; set; } = "";
        /// <summary>シリーズ内で当該キャラを演じた声優名（連名は「、」連結）。</summary>
        public string VoiceActorNames { get; set; } = "";
    }
}