using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クレジット階層を 1 度だけ走査して、「ある名義 (alias) / ロゴがどのエピソードのどの役職で
/// 登場したか」を逆引きできるインデックスを構築する。
/// <para>
/// 人物詳細ページ (<see cref="Generators.PersonsGenerator"/>) と企業詳細ページ
/// (<see cref="Generators.CompaniesGenerator"/>) の両方が「全エピソード横断のクレジット集計」を
/// 必要とする。それぞれが独立に同じ走査をすると DB アクセス量が倍になるため、ビルド開始時に
/// 1 回だけ全クレジットを舐めてインデックス化し、各ジェネレータはこのインデックスを参照する形にする。
/// </para>
/// <para>
/// 集計対象のクレジット階層上の参照点:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <c>credit_block_entries.person_alias_id</c> ─ 名義として直接登場
///       （PERSON エントリ、CHARACTER_VOICE エントリ、CASTING_COOPERATION 等）
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>credit_block_entries.company_alias_id</c> ─ 屋号として直接登場（COMPANY エントリ）
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>credit_block_entries.logo_id</c> ─ ロゴエントリ。LOGO 経由の屋号関与は別途
///       <see cref="ByLogo"/> として持ち、企業詳細ページ側で「自社配下のロゴ」を選んで取り出す。
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>credit_role_blocks.leading_company_alias_id</c> ─ ブロック先頭企業
///       （配下に COMPANY エントリが無くても屋号関与として記録）
///     </description>
///   </item>
/// </list>
/// <para>
/// テキストフィールド（楽曲の lyricist_name 等）は仕様により対象外。マスタ駆動の堅実な
/// 紐付けのみを集計する。
/// </para>
/// </summary>
public sealed class CreditInvolvementIndex
{
    /// <summary>person_alias_id → 関与レコード列。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByPersonAlias { get; }

    /// <summary>
    /// company_alias_id → 関与レコード列。
    /// COMPANY エントリ直接参照と、ブロック先頭の <c>leading_company_alias_id</c> による参照の両方を含む。
    /// LOGO エントリ経由の関与はここには入れず <see cref="ByLogo"/> 側に格納する
    /// （企業詳細ページが「自社配下のロゴ」を後から選ぶため）。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByCompanyAlias { get; }

    /// <summary>logo_id → 関与レコード列（LOGO エントリ）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByLogo { get; }

    /// <summary>
    /// character_alias_id → 関与レコード列（CHARACTER_VOICE エントリ経由）。
    /// 当該キャラ名義として声優がクレジットされた事実を逆引きするための辞書。
    /// プリキュア詳細・キャラクター詳細ページで「このキャラを誰が、いつ演じたか」を引くのに使う。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByCharacterAlias { get; }

    private CreditInvolvementIndex(
        IReadOnlyDictionary<int, IReadOnlyList<Involvement>> byPerson,
        IReadOnlyDictionary<int, IReadOnlyList<Involvement>> byCompany,
        IReadOnlyDictionary<int, IReadOnlyList<Involvement>> byLogo,
        IReadOnlyDictionary<int, IReadOnlyList<Involvement>> byCharacter)
    {
        ByPersonAlias = byPerson;
        ByCompanyAlias = byCompany;
        ByLogo = byLogo;
        ByCharacterAlias = byCharacter;
    }

    /// <summary>
    /// 全シリーズの全エピソードのクレジット階層を走査して、関与インデックスを構築する。
    /// 1 回限りの起動コスト処理。N+1 だが各テーブルの主キー INDEX を踏むため許容範囲。
    /// </summary>
    public static async Task<CreditInvolvementIndex> BuildAsync(
        BuildContext ctx,
        IConnectionFactory factory,
        CancellationToken ct = default)
    {
        var creditsRepo = new CreditsRepository(factory);
        var cardsRepo = new CreditCardsRepository(factory);
        var tiersRepo = new CreditCardTiersRepository(factory);
        var groupsRepo = new CreditCardGroupsRepository(factory);
        var cardRolesRepo = new CreditCardRolesRepository(factory);
        var blocksRepo = new CreditRoleBlocksRepository(factory);
        var entriesRepo = new CreditBlockEntriesRepository(factory);

        var personIdx = new Dictionary<int, List<Involvement>>();
        var companyIdx = new Dictionary<int, List<Involvement>>();
        var logoIdx = new Dictionary<int, List<Involvement>>();
        // CHARACTER_VOICE エントリで character_alias_id が設定されている場合、当該キャラ名義への
        // 関与として記録する。プリキュア詳細・キャラクター詳細ページから声の出演履歴を引くのに使う。
        var characterIdx = new Dictionary<int, List<Involvement>>();

        void AddPerson(int aliasId, Involvement v)
        {
            if (!personIdx.TryGetValue(aliasId, out var list))
            {
                list = new List<Involvement>();
                personIdx[aliasId] = list;
            }
            list.Add(v);
        }
        void AddCompany(int aliasId, Involvement v)
        {
            if (!companyIdx.TryGetValue(aliasId, out var list))
            {
                list = new List<Involvement>();
                companyIdx[aliasId] = list;
            }
            list.Add(v);
        }
        void AddLogo(int logoId, Involvement v)
        {
            if (!logoIdx.TryGetValue(logoId, out var list))
            {
                list = new List<Involvement>();
                logoIdx[logoId] = list;
            }
            list.Add(v);
        }
        void AddCharacter(int aliasId, Involvement v)
        {
            if (!characterIdx.TryGetValue(aliasId, out var list))
            {
                list = new List<Involvement>();
                characterIdx[aliasId] = list;
            }
            list.Add(v);
        }

        ctx.Logger.Section("Building credit involvement index");
        int totalEntries = 0;

        // SeriesGenerator や EpisodeGenerator のクレジット走査と同じ階層を辿る。
        // EpisodeId は SERIES スコープ（Credit.ScopeKind="SERIES"）のとき null で記録する。
        foreach (var (seriesId, eps) in ctx.EpisodesBySeries)
        {
            if (!ctx.SeriesById.TryGetValue(seriesId, out _)) continue;

            foreach (var ep in eps)
            {
                var credits = (await creditsRepo.GetByEpisodeAsync(ep.EpisodeId, ct).ConfigureAwait(false))
                    .Where(c => !c.IsDeleted)
                    .ToList();

                foreach (var credit in credits)
                {
                    int? scopeEpisodeId = string.Equals(credit.ScopeKind, "SERIES", StringComparison.Ordinal)
                        ? null
                        : credit.EpisodeId;
                    int seriesIdForCredit = credit.SeriesId ?? seriesId;

                    var cards = (await cardsRepo.GetByCreditAsync(credit.CreditId, ct).ConfigureAwait(false))
                        .OrderBy(c => c.CardSeq);
                    foreach (var card in cards)
                    {
                        var tiers = (await tiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                            .OrderBy(t => t.TierNo);
                        foreach (var tier in tiers)
                        {
                            var groups = (await groupsRepo.GetByTierAsync(tier.CardTierId, ct).ConfigureAwait(false))
                                .OrderBy(g => g.GroupNo);
                            foreach (var grp in groups)
                            {
                                var cardRoles = (await cardRolesRepo.GetByGroupAsync(grp.CardGroupId, ct).ConfigureAwait(false))
                                    .OrderBy(r => r.OrderInGroup);
                                foreach (var cr in cardRoles)
                                {
                                    string roleCode = cr.RoleCode ?? "";
                                    var blocks = (await blocksRepo.GetByCardRoleAsync(cr.CardRoleId, ct).ConfigureAwait(false))
                                        .OrderBy(b => b.BlockSeq).ToList();

                                    foreach (var b in blocks)
                                    {
                                        // ブロック先頭企業（leading_company_alias_id）は屋号関与として記録。
                                        if (b.LeadingCompanyAliasId is int leadId)
                                        {
                                            AddCompany(leadId, new Involvement
                                            {
                                                SeriesId = seriesIdForCredit,
                                                EpisodeId = scopeEpisodeId,
                                                CreditKind = credit.CreditKind,
                                                RoleCode = roleCode,
                                                Kind = InvolvementKind.LeadingCompany,
                                                IsBroadcastOnly = false
                                            });
                                        }

                                        var entries = (await entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                            .OrderBy(e => e.EntrySeq);
                                        foreach (var e in entries)
                                        {
                                            totalEntries++;

                                            // 人物名義参照（PERSON / CHARACTER_VOICE のどちらも person_alias_id を持つ）。
                                            if (e.PersonAliasId is int paid)
                                            {
                                                var kind = string.Equals(e.EntryKind, "CHARACTER_VOICE", StringComparison.Ordinal)
                                                    ? InvolvementKind.CharacterVoice
                                                    : InvolvementKind.Person;
                                                var personInv = new Involvement
                                                {
                                                    SeriesId = seriesIdForCredit,
                                                    EpisodeId = scopeEpisodeId,
                                                    CreditKind = credit.CreditKind,
                                                    RoleCode = roleCode,
                                                    Kind = kind,
                                                    EntryKind = e.EntryKind,
                                                    PersonAliasId = paid,
                                                    CharacterAliasId = e.CharacterAliasId,
                                                    RawCharacterText = e.RawCharacterText,
                                                    // 所属屋号 ID を Involvement に持ち回す。
                                                    // 人物詳細ページ側でこの屋号 ID を参照して「(東映アニメーション)」のような所属併記を出す。
                                                    AffiliationCompanyAliasId = e.AffiliationCompanyAliasId,
                                                    IsBroadcastOnly = e.IsBroadcastOnly
                                                };
                                                AddPerson(paid, personInv);

                                                // 所属屋号が指定されている場合、屋号側からも逆引きできるよう
                                                // ByCompanyAlias に Member 種別レコードを追加する。
                                                // 企業詳細ページの「メンバー履歴」セクションが、自社屋号の Member 種別レコードを
                                                // 集めて「当該企業を所属としてクレジットされた人物名義一覧」を組み立てる。
                                                if (e.AffiliationCompanyAliasId is int affId)
                                                {
                                                    AddCompany(affId, new Involvement
                                                    {
                                                        SeriesId = seriesIdForCredit,
                                                        EpisodeId = scopeEpisodeId,
                                                        CreditKind = credit.CreditKind,
                                                        RoleCode = roleCode,
                                                        Kind = InvolvementKind.Member,
                                                        EntryKind = e.EntryKind,
                                                        PersonAliasId = paid,
                                                        CharacterAliasId = e.CharacterAliasId,
                                                        AffiliationCompanyAliasId = affId,
                                                        IsBroadcastOnly = e.IsBroadcastOnly
                                                    });
                                                }

                                                // CHARACTER_VOICE で character_alias_id があれば、キャラ名義側からも
                                                // 同じ Involvement を逆引きできるように登録する（プリキュア詳細・
                                                // キャラクター詳細から声の出演履歴を引くために必要）。
                                                if (kind == InvolvementKind.CharacterVoice
                                                    && e.CharacterAliasId is int chaId)
                                                {
                                                    AddCharacter(chaId, personInv);
                                                }
                                            }

                                            // 屋号エントリ（COMPANY）。
                                            if (e.CompanyAliasId is int caid)
                                            {
                                                AddCompany(caid, new Involvement
                                                {
                                                    SeriesId = seriesIdForCredit,
                                                    EpisodeId = scopeEpisodeId,
                                                    CreditKind = credit.CreditKind,
                                                    RoleCode = roleCode,
                                                    Kind = InvolvementKind.Company,
                                                    EntryKind = e.EntryKind,
                                                    IsBroadcastOnly = e.IsBroadcastOnly
                                                });
                                            }

                                            // ロゴエントリは ByLogo に記録（屋号への展開は CompaniesGenerator が
                                            // 配下ロゴを引いて行う）。
                                            if (e.LogoId is int lid)
                                            {
                                                AddLogo(lid, new Involvement
                                                {
                                                    SeriesId = seriesIdForCredit,
                                                    EpisodeId = scopeEpisodeId,
                                                    CreditKind = credit.CreditKind,
                                                    RoleCode = roleCode,
                                                    Kind = InvolvementKind.Logo,
                                                    EntryKind = e.EntryKind,
                                                    LogoId = lid,
                                                    IsBroadcastOnly = e.IsBroadcastOnly
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        // ── ここまでクレジット階層（credit_block_entries 等）の走査 ──
        //
        // 以下、楽曲・劇伴の構造化クレジット系を追加で走査する。
        //   1) song_credits         … 歌の作詞 / 作曲 / 編曲の連名（roles マスタ駆動）
        //   2) song_recording_singers … 録音に紐付く歌唱者（VOCALS / CHORUS、PERSON / CHARACTER_WITH_CV）
        //   3) bgm_cue_credits      … 劇伴の作曲 / 編曲の連名
        //
        // これらは credit_block_entries とは別系統の構造化クレジットで、Phase 2-A までは集計対象外だった。
        // Phase 2-B でこれを取り込むことで、人物詳細・キャラクター詳細・企業詳細の各ページから
        // 「主題歌の作詞担当としてこのエピソードに関与している」のような関与履歴が逆引きできるようになる。
        //
        // 主題歌・劇伴は曲・録音単位のマスタなので、エピソード紐付けはそれぞれ：
        //   song_credits / song_recording_singers → episode_theme_songs（song_recording_id 経由）
        //   bgm_cue_credits → bgm_cue_episode_uses（series_id+m_no_detail 経由）
        // を JOIN して解決する。
        //
        // usage_actuality（episode_theme_songs.usage_actuality, 追加）：
        //   - NORMAL                  : 通常通り集計対象（既定）
        //   - BROADCAST_NOT_CREDITED  : クレジットされていないが流れた → クレジット集計対象外
        //                                エピソード主題歌セクション側のみ表示する想定なので、
        //                                CreditInvolvementIndex には載せない。
        //   - CREDITED_NOT_BROADCAST  : クレジットされているが流れていない → クレジット側の事実として集計対象に含める
        //                                エピソード主題歌セクション側で逆に非表示にする運用。

        // ── 1) song_credits 巡回 ──
        // episode_theme_songs を介してエピソード紐付けを解決し、(song_id, role_code) ごとの person_alias_id を集計。
        // BROADCAST_NOT_CREDITED は除外、CREDITED_NOT_BROADCAST は含める。
        // 同じ song_id が複数エピソード（複数話で使われる主題歌）の場合は、各エピソードに 1 件ずつ Involvement を作る。
        const string sqlSongCredits = """
            SELECT
              ets.episode_id              AS EpisodeId,
              ep.series_id                AS SeriesId,
              ets.is_broadcast_only       AS IsBroadcastOnly,
              ets.usage_actuality         AS UsageActuality,
              ets.theme_kind              AS ThemeKind,
              sc.credit_role              AS CreditRole,
              sc.person_alias_id          AS PersonAliasId
            FROM song_credits sc
            JOIN song_recordings sr ON sr.song_id = sc.song_id
            JOIN episode_theme_songs ets ON ets.song_recording_id = sr.song_recording_id
            JOIN episodes ep ON ep.episode_id = ets.episode_id
            WHERE ets.usage_actuality <> 'BROADCAST_NOT_CREDITED';
            """;

        await using (var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            int songCreditCount = 0;
            var rows = await conn.QueryAsync<SongCreditInvRow>(new CommandDefinition(sqlSongCredits, cancellationToken: ct));
            foreach (var r in rows)
            {
                AddPerson(r.PersonAliasId, new Involvement
                {
                    SeriesId = r.SeriesId,
                    EpisodeId = r.EpisodeId,
                    // 主題歌系の関与は CreditKind=THEME_SONG として既存の OP/ED/ED_CARD と区別する。
                    // クレジット階層由来の OP / ED とは別の、構造化主題歌マスタ由来の関与を意味する。
                    CreditKind = "THEME_SONG",
                    RoleCode = r.CreditRole,
                    Kind = InvolvementKind.Person,
                    EntryKind = "SONG_CREDIT",
                    PersonAliasId = r.PersonAliasId,
                    IsBroadcastOnly = r.IsBroadcastOnly != 0,
                    // 主題歌種別（OP / ED / INSERT）を保持。人物詳細クレジット履歴で
                    // 「オープニング主題歌 作曲」「エンディング主題歌 編曲」のようにグループ分けするための情報源。
                    ThemeKind = r.ThemeKind
                });
                songCreditCount++;
            }
            ctx.Logger.Info($"  song_credits scanned: {songCreditCount} involvements (excluded BROADCAST_NOT_CREDITED)");
        }

        // ── 2) song_recording_singers 巡回 ──
        // 録音に紐付く歌唱者連名（VOCALS / CHORUS）を集計。BillingKind に応じて：
        //   - PERSON            : person_alias_id を ByPersonAlias に
        //                          スラッシュ並列の slash_person_alias_id があればそれも
        //   - CHARACTER_WITH_CV : voice_person_alias_id を ByPersonAlias に（声優として）
        //                          character_alias_id を ByCharacterAlias に（キャラクター名義として）
        //                          スラッシュ相方 (slash_character_alias_id) があれば
        //                          それも ByCharacterAlias に
        // usage_actuality フィルタは song_credits と同じ。
        const string sqlSingers = """
            SELECT
              ets.episode_id              AS EpisodeId,
              ep.series_id                AS SeriesId,
              ets.is_broadcast_only       AS IsBroadcastOnly,
              ets.usage_actuality         AS UsageActuality,
              ets.theme_kind              AS ThemeKind,
              srs.role_code               AS RoleCode,
              srs.billing_kind            AS BillingKind,
              srs.person_alias_id         AS PersonAliasId,
              srs.character_alias_id      AS CharacterAliasId,
              srs.voice_person_alias_id   AS VoicePersonAliasId,
              srs.slash_person_alias_id   AS SlashPersonAliasId,
              srs.slash_character_alias_id AS SlashCharacterAliasId
            FROM song_recording_singers srs
            JOIN episode_theme_songs ets ON ets.song_recording_id = srs.song_recording_id
            JOIN episodes ep ON ep.episode_id = ets.episode_id
            WHERE ets.usage_actuality <> 'BROADCAST_NOT_CREDITED';
            """;

        await using (var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            int singerCount = 0;
            var rows = await conn.QueryAsync<SingerInvRow>(new CommandDefinition(sqlSingers, cancellationToken: ct));
            foreach (var r in rows)
            {
                bool isPerson = string.Equals(r.BillingKind, "PERSON", StringComparison.Ordinal);
                bool isBroadcastOnly = r.IsBroadcastOnly != 0;

                if (isPerson)
                {
                    // PERSON: person_alias_id 必須、slash_person_alias_id 任意。
                    if (r.PersonAliasId is int paid)
                    {
                        AddPerson(paid, new Involvement
                        {
                            SeriesId = r.SeriesId,
                            EpisodeId = r.EpisodeId,
                            CreditKind = "THEME_SONG",
                            RoleCode = r.RoleCode,
                            Kind = InvolvementKind.Person,
                            EntryKind = "RECORDING_SINGER",
                            PersonAliasId = paid,
                            IsBroadcastOnly = isBroadcastOnly,
                            // 主題歌種別を伝達。歌唱もテーマ種別ごとに分類表示する。
                            ThemeKind = r.ThemeKind
                        });
                        singerCount++;
                    }
                    if (r.SlashPersonAliasId is int spaid)
                    {
                        AddPerson(spaid, new Involvement
                        {
                            SeriesId = r.SeriesId,
                            EpisodeId = r.EpisodeId,
                            CreditKind = "THEME_SONG",
                            RoleCode = r.RoleCode,
                            Kind = InvolvementKind.Person,
                            EntryKind = "RECORDING_SINGER",
                            PersonAliasId = spaid,
                            IsBroadcastOnly = isBroadcastOnly,
                            ThemeKind = r.ThemeKind
                        });
                        singerCount++;
                    }
                }
                else // CHARACTER_WITH_CV
                {
                    // 声優を ByPersonAlias に。CharacterVoice 種別で、関連キャラ名義を CharacterAliasId に保持。
                    if (r.VoicePersonAliasId is int vpaid)
                    {
                        var inv = new Involvement
                        {
                            SeriesId = r.SeriesId,
                            EpisodeId = r.EpisodeId,
                            CreditKind = "THEME_SONG",
                            RoleCode = r.RoleCode,
                            Kind = InvolvementKind.CharacterVoice,
                            EntryKind = "RECORDING_SINGER",
                            PersonAliasId = vpaid,
                            CharacterAliasId = r.CharacterAliasId,
                            IsBroadcastOnly = isBroadcastOnly,
                            ThemeKind = r.ThemeKind
                        };
                        AddPerson(vpaid, inv);
                        // キャラ側の逆引きにも同じ Involvement を載せる（CHARACTER_VOICE 系と同じ運用）。
                        if (r.CharacterAliasId is int chaId)
                        {
                            AddCharacter(chaId, inv);
                        }
                        singerCount++;
                    }
                    // スラッシュ並列キャラがある場合（同 CV で複数キャラを兼務する形式の表記）。
                    // 声優は同じ voice_person_alias_id なのでキャラ側だけを別途追加する。
                    if (r.SlashCharacterAliasId is int schaId && r.VoicePersonAliasId is int vpaid2)
                    {
                        var inv = new Involvement
                        {
                            SeriesId = r.SeriesId,
                            EpisodeId = r.EpisodeId,
                            CreditKind = "THEME_SONG",
                            RoleCode = r.RoleCode,
                            Kind = InvolvementKind.CharacterVoice,
                            EntryKind = "RECORDING_SINGER",
                            PersonAliasId = vpaid2,
                            CharacterAliasId = schaId,
                            IsBroadcastOnly = isBroadcastOnly,
                            ThemeKind = r.ThemeKind
                        };
                        AddCharacter(schaId, inv);
                        singerCount++;
                    }
                }
            }
            ctx.Logger.Info($"  song_recording_singers scanned: {singerCount} involvements (excluded BROADCAST_NOT_CREDITED)");
        }

        // ── 3) bgm_cue_credits 巡回 ──
        // 劇伴の作曲 / 編曲の連名を集計。エピソード紐付けは episode_uses 経由
        // （旧 bgm_cue_episode_uses は episode_uses に統合された。
        //  episode_uses は SONG / BGM / DRAMA / RADIO / JINGLE / OTHER の各種コンテンツ
        //  の使用記録を 1 テーブルにまとめており、BGM のレコードは content_kind_code='BGM' で識別する）。
        // bgm 系には usage_actuality 概念は無い（episode_theme_songs だけが持つ）。
        // bgm_cues の PK は (series_id, m_no_detail) なので、cue ごとに紐付くエピソード全件にバラして集計。
        const string sqlBgmCueCredits = """
            SELECT
              eu.episode_id              AS EpisodeId,
              ep.series_id               AS SeriesId,
              eu.is_broadcast_only       AS IsBroadcastOnly,
              bcc.credit_role            AS CreditRole,
              bcc.person_alias_id        AS PersonAliasId
            FROM bgm_cue_credits bcc
            JOIN episode_uses eu
              ON eu.bgm_series_id   = bcc.series_id
             AND eu.bgm_m_no_detail = bcc.m_no_detail
            JOIN episodes ep ON ep.episode_id = eu.episode_id
            WHERE eu.content_kind_code = 'BGM';
            """;

        await using (var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            int bgmCueCount = 0;
            var rows = await conn.QueryAsync<BgmCueCreditInvRow>(new CommandDefinition(sqlBgmCueCredits, cancellationToken: ct));
            foreach (var r in rows)
            {
                AddPerson(r.PersonAliasId, new Involvement
                {
                    SeriesId = r.SeriesId,
                    EpisodeId = r.EpisodeId,
                    // bgm_cue_credits 由来は CreditKind=BGM として識別できるようにする。
                    // 主題歌系（THEME_SONG）と区別したい呼び出し側がいる場合に役立つ。
                    CreditKind = "BGM",
                    RoleCode = r.CreditRole,
                    Kind = InvolvementKind.Person,
                    EntryKind = "BGM_CUE_CREDIT",
                    PersonAliasId = r.PersonAliasId,
                    IsBroadcastOnly = r.IsBroadcastOnly != 0
                });
                bgmCueCount++;
            }
            ctx.Logger.Info($"  bgm_cue_credits scanned: {bgmCueCount} involvements");
        }

        // ── 集計サマリ ──
        ctx.Logger.Info($"  scanned: {totalEntries} entries → person aliases={personIdx.Count}, company aliases={companyIdx.Count}, logos={logoIdx.Count}, character aliases={characterIdx.Count}");

        return new CreditInvolvementIndex(
            personIdx.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Involvement>)kv.Value),
            companyIdx.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Involvement>)kv.Value),
            logoIdx.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Involvement>)kv.Value),
            characterIdx.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Involvement>)kv.Value));
    }
}

/// <summary>
/// 関与レコード 1 件（人物・企業共用）。
/// クレジット階層上の 1 つの参照点に対応する。
/// </summary>
public sealed class Involvement
{
    /// <summary>関与しているシリーズ ID。</summary>
    public int SeriesId { get; init; }

    /// <summary>関与しているエピソード ID。SERIES スコープのクレジットでは null。</summary>
    public int? EpisodeId { get; init; }

    /// <summary>クレジット種別（OP / ED など）。</summary>
    public string CreditKind { get; init; } = "";

    /// <summary>役職コード（例: SCREENPLAY、EPISODE_DIRECTOR、VOICE CAST、PRODUCTION）。</summary>
    public string RoleCode { get; init; } = "";

    /// <summary>関与の種別（人物 / 声優 / 屋号 / ロゴ / ブロック先頭企業）。</summary>
    public InvolvementKind Kind { get; init; }

    /// <summary>クレジットエントリ種別（PERSON / CHARACTER_VOICE / COMPANY / LOGO 等）。
    /// LeadingCompany 由来のレコードでは未設定。</summary>
    public string EntryKind { get; init; } = "";

    /// <summary>
    /// PERSON / CHARACTER_VOICE エントリの person_alias_id。
    /// キャラクター名義側からの逆引き（<see cref="CreditInvolvementIndex.ByCharacterAlias"/>）の
    /// 結果から声優を取り出すために使う。
    /// </summary>
    public int? PersonAliasId { get; init; }

    /// <summary>CHARACTER_VOICE のとき演じたキャラクター名義 ID（任意）。</summary>
    public int? CharacterAliasId { get; init; }

    /// <summary>CHARACTER_VOICE で raw_character_text を使っている場合の生テキスト。</summary>
    public string? RawCharacterText { get; init; }

    /// <summary>Logo 種別のときのロゴ ID。</summary>
    public int? LogoId { get; init; }

    /// <summary>
    /// PERSON / CHARACTER_VOICE エントリにクレジットされた所属屋号（任意）。
    /// 「○○（東映アニメーション）」のような所属付きクレジット表記の屋号 ID 側。
    /// 人物詳細ページのクレジット履歴で所属併記を出すために、また企業詳細ページの
    /// 「メンバー履歴」で「当該企業屋号を所属としてクレジットされた人物名義」を逆引きするために使う。
    /// 屋号未指定（フリーテキスト所属または所属なし）のケースでは null。
    /// </summary>
    public int? AffiliationCompanyAliasId { get; init; }

    /// <summary>本放送限定エントリかどうか（is_broadcast_only=1 のレコードを示す）。</summary>
    public bool IsBroadcastOnly { get; init; }

    /// <summary>
    /// 主題歌種別。
    /// <see cref="EntryKind"/> が <c>SONG_CREDIT</c> または <c>RECORDING_SINGER</c> のときに
    /// <c>episode_theme_songs.theme_kind</c> の値（<c>OP</c> / <c>ED</c> / <c>INSERT</c>）が入る。
    /// 主題歌・録音歌唱以外の関与（credit_block_entries 由来や bgm_cue_credits 由来）では <c>null</c>。
    /// <para>
    /// 人物詳細のクレジット履歴で、主題歌の作詞・作曲・編曲・歌唱を OP / ED / 挿入歌で別グループに
    /// 分けて見せるために使う。ラベル展開時は <c>song_music_classes</c> マスタの <c>name_ja</c> を
    /// 引いて「オープニング主題歌 作曲」「エンディング主題歌 編曲」のような自然な日本語に展開する。
    /// </para>
    /// </summary>
    public string? ThemeKind { get; init; }
}

/// <summary>関与レコードの種別。</summary>
public enum InvolvementKind
{
    /// <summary>PERSON エントリ（脚本／演出／作画監督などの一般スタッフ参照）。</summary>
    Person = 0,
    /// <summary>CHARACTER_VOICE エントリ（声優としての出演）。</summary>
    CharacterVoice = 1,
    /// <summary>COMPANY エントリ（屋号への直接参照）。</summary>
    Company = 2,
    /// <summary>LOGO エントリ（ロゴ → 屋号への間接参照）。</summary>
    Logo = 3,
    /// <summary>credit_role_blocks.leading_company_alias_id（ブロック先頭の屋号）。</summary>
    LeadingCompany = 4,
    /// <summary>
    /// PERSON / CHARACTER_VOICE エントリの所属屋号としての参照。
    /// 「○○（東映アニメーション）」のような所属付きクレジット表記で、
    /// 屋号側から「当該屋号に所属していた人物名義」を逆引きできるようにする。
    /// 企業詳細ページの「メンバー履歴」セクションがこの種別で <see cref="CreditInvolvementIndex.ByCompanyAlias"/> を絞り込んで使う。
    /// </summary>
    Member = 5
}

// ─────────────────────────────────────────────────────────────────────
// 追加した SQL クエリ用 DTO 群。
// CreditInvolvementIndex.BuildAsync 内の主題歌系・劇伴系巡回で Dapper が直接マップする受け皿。
// 内部利用のみなので internal sealed class。
// ─────────────────────────────────────────────────────────────────────

/// <summary>song_credits 巡回 SQL の受け取り DTO。</summary>
internal sealed class SongCreditInvRow
{
    public int EpisodeId { get; set; }
    public int SeriesId { get; set; }
    public byte IsBroadcastOnly { get; set; }
    public string UsageActuality { get; set; } = "NORMAL";
    public string CreditRole { get; set; } = "";
    public int PersonAliasId { get; set; }
    /// <summary>
    /// 主題歌種別（OP / ED / INSERT）。
    /// episode_theme_songs.theme_kind から SELECT する。人物詳細クレジット履歴で
    /// 主題歌の作詞・作曲・編曲を OP / ED / 挿入歌のサブグループに分けるための分類軸。
    /// </summary>
    public string ThemeKind { get; set; } = "";
}

/// <summary>song_recording_singers 巡回 SQL の受け取り DTO。</summary>
internal sealed class SingerInvRow
{
    public int EpisodeId { get; set; }
    public int SeriesId { get; set; }
    public byte IsBroadcastOnly { get; set; }
    public string UsageActuality { get; set; } = "NORMAL";
    public string RoleCode { get; set; } = "VOCALS";
    public string BillingKind { get; set; } = "PERSON";
    public int? PersonAliasId { get; set; }
    public int? CharacterAliasId { get; set; }
    public int? VoicePersonAliasId { get; set; }
    public int? SlashPersonAliasId { get; set; }
    public int? SlashCharacterAliasId { get; set; }
    /// <summary>
    /// 主題歌種別（OP / ED / INSERT）。
    /// 歌唱クレジット（VOCALS / CHORUS）も主題歌のテーマ種別ごとに分類できるよう、
    /// episode_theme_songs.theme_kind を伝達する。
    /// </summary>
    public string ThemeKind { get; set; } = "";
}

/// <summary>bgm_cue_credits 巡回 SQL の受け取り DTO。</summary>
internal sealed class BgmCueCreditInvRow
{
    public int EpisodeId { get; set; }
    public int SeriesId { get; set; }
    public byte IsBroadcastOnly { get; set; }
    public string CreditRole { get; set; } = "";
    public int PersonAliasId { get; set; }
}