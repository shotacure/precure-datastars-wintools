using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クレジット階層を 1 度だけ走査して、「ある名義 (alias) / ロゴがどのエピソードのどの役職で
/// 登場したか」を逆引きできるインデックスを構築する。
/// 人物詳細ページ (<see cref="Generators.PersonsGenerator"/>) と企業詳細ページ
/// (<see cref="Generators.CompaniesGenerator"/>) の両方が「全エピソード横断のクレジット集計」を
/// 必要とする。それぞれが独立に同じ走査をすると DB アクセス量が倍になるため、ビルド開始時に
/// 1 回だけ全クレジットを舐めてインデックス化し、各ジェネレータはこのインデックスを参照する形にする。
/// 集計対象のクレジット階層上の参照点:
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
/// テキストフィールド（楽曲の lyricist_name 等）は仕様により対象外。マスタ駆動の堅実な
/// 紐付けのみを集計する。
/// </summary>
public sealed class CreditInvolvementIndex
{
    /// <summary>person_alias_id → 関与レコード列。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByPersonAlias { get; }

    /// <summary>company_alias_id → 関与レコード列。 COMPANY エントリ直接参照と、ブロック先頭の <c>leading_company_alias_id</c> による参照の両方を含む。 LOGO エントリ経由の関与はここには入れず <see cref="ByLogo"/> 側に格納する （企業詳細ページが「自社配下のロゴ」を後から選ぶため）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByCompanyAlias { get; }

    /// <summary>logo_id → 関与レコード列（LOGO エントリ）。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<Involvement>> ByLogo { get; }

    /// <summary>character_alias_id → 関与レコード列（CHARACTER_VOICE エントリ経由）。 当該キャラ名義として声優がクレジットされた事実を逆引きするための辞書。 プリキュア詳細・キャラクター詳細ページで「このキャラを誰が、いつ演じたか」を引くのに使う。</summary>
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

    /// <summary>全シリーズの全エピソードのクレジット階層を走査して、関与インデックスを構築する。
    /// 1 回限りの起動コスト処理。クレジット行・階層 6 段とも SiteDataLoader が事前展開した
    /// BuildContext（CreditsByEpisode / CreditsBySeries / CreditTree / RoleByCode）への
    /// 同期 lookup で走査が完結する（旧実装は per-credit の階層別 GetBy*Async N+1 で
    /// 累積数千クエリの DB 往復が走っていた）。DB を直接引くのは末尾の楽曲・劇伴
    /// 構造化クレジット 3 系統（JOIN 一括 SELECT × 3 本）のみ。</summary>
    public static async Task<CreditInvolvementIndex> BuildAsync(
        BuildContext ctx,
        IConnectionFactory factory,
        CancellationToken ct = default)
    {
        // role_format_kind='THEME_SONG' の role_code 集合。
        // クレジット階層上、主題歌は「THEME_SONG 形式の役職ブロック」として
        // ある位置に現れる（ブロックの entry は人物ではなく曲を指すため、
        // 作詞・作曲・歌唱などの個人は song_credits / song_recording_singers から
        // 補完される）。その個人関与に「クレジット内の主題歌ロールの位置」を
        // 与えるため、走査中に当該ブロックの出現連番を控えておく。
        var themeSongRoleCodes = ctx.RoleByCode.Values
            .Where(r => string.Equals(r.RoleFormatKind, "THEME_SONG", StringComparison.Ordinal))
            .Select(r => r.RoleCode)
            .ToHashSet(StringComparer.Ordinal);

        // 主題歌ブロックのクレジット内出現位置の記録。
        var themeBlockSeqByContext = new Dictionary<(int EpKey, string CreditKind), int>();
        // エピソードごとの「最初の主題歌ブロック位置」フォールバック（INSERT 等、
        // 親 CreditKind が OP/ED と一致しないケースで使う）。
        var firstThemeBlockSeqByEp = new Dictionary<int, int>();
        // エピソードごとに到達した最大 creditSeq（主題歌ブロックが全く無い場合に
        // 主題歌スタッフを末尾送りするためのフォールバック基準）。
        var maxSeqByEp = new Dictionary<int, int>();

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

        // クレジット 1 件の card/tier/group/role/block/entry を 6 段ループで走査し、
        // 人物・企業・キャラ・ロゴ各インデックスに Involvement を積むローカル関数。
        // 「EPISODE 紐付け（episode_id != null）」と「SERIES 紐付け（episode_id IS NULL、movie 等）」
        // の両経路から呼ぶため共通化（旧コードは EPISODE 経路だけが走り、映画系列の SERIES-attached
        // クレジットが一切インデックス化されない不具合があった）。
        // 階層 6 段は BuildContext.CreditTree の事前展開スナップショットを同期で辿る
        // （旧実装は階層別 GetBy*Async を per-key で発火する N+1 だった）。スナップショット各層は
        // per-id 取得時と同一の並び順を保持しているが、旧経路と同じ明示ソートを保険として残す。
        // <param name="credit">対象 credit 行。</param>
        // <param name="seriesIdContext">呼び出し側が把握しているシリーズ ID。credit.SeriesId が null のとき
        // のフォールバックに使う（既存挙動を保つ）。</param>
        // <param name="creditSeqStart">現スコープ内で既に到達している creditSeq。本関数で消費した分を
        // 加算した値を戻り値で返す。</param>
        // <returns>関数終了時点での creditSeq（次の credit の起点）。</returns>
        int ProcessCredit(Credit credit, int seriesIdContext, int creditSeqStart)
        {
            int? scopeEpisodeId = string.Equals(credit.ScopeKind, "SERIES", StringComparison.Ordinal)
                ? null
                : credit.EpisodeId;
            int seriesIdForCredit = credit.SeriesId ?? seriesIdContext;
            int creditSeqInEpisode = creditSeqStart;

            if (!ctx.CreditTree.CardsByCreditId.TryGetValue(credit.CreditId, out var cardSnapshots))
                return creditSeqInEpisode;
            foreach (var cardSnap in cardSnapshots.OrderBy(c => c.Card.CardSeq))
            {
                foreach (var tierSnap in cardSnap.Tiers.OrderBy(t => t.Tier.TierNo))
                {
                    foreach (var grpSnap in tierSnap.Groups.OrderBy(g => g.Group.GroupNo))
                    {
                        foreach (var crSnap in grpSnap.Roles.OrderBy(r => r.Role.OrderInGroup))
                        {
                            string roleCode = crSnap.Role.RoleCode ?? "";
                            var blocks = crSnap.Blocks.OrderBy(b => b.Block.BlockSeq).ToList();

                            // この役職が主題歌（THEME_SONG 形式）なら、いま到達している
                            if (themeSongRoleCodes.Contains(roleCode))
                            {
                                int epKey = scopeEpisodeId ?? -seriesIdForCredit;
                                var ctxKey = (epKey, credit.CreditKind);
                                if (!themeBlockSeqByContext.ContainsKey(ctxKey))
                                    themeBlockSeqByContext[ctxKey] = creditSeqInEpisode;
                                if (!firstThemeBlockSeqByEp.ContainsKey(epKey))
                                    firstThemeBlockSeqByEp[epKey] = creditSeqInEpisode;
                            }

                            foreach (var blockSnap in blocks)
                            {
                                var b = blockSnap.Block;
                                // ブロック先頭企業（leading_company_alias_id）は屋号関与として記録。
                                if (b.LeadingCompanyAliasId is int leadId)
                                {
                                    int leadSeq = creditSeqInEpisode++;
                                    AddCompany(leadId, new Involvement
                                    {
                                        SeriesId = seriesIdForCredit,
                                        EpisodeId = scopeEpisodeId,
                                        CreditKind = credit.CreditKind,
                                        RoleCode = roleCode,
                                        Kind = InvolvementKind.LeadingCompany,
                                        IsBroadcastOnly = false,
                                        CreditSeq = leadSeq
                                    });
                                }

                                var entries = blockSnap.Entries.OrderBy(e => e.EntrySeq);
                                foreach (var e in entries)
                                {
                                    totalEntries++;

                                    // 当該エントリの、エピソード内クレジット表示順での出現位置。
                                    int entrySeq = creditSeqInEpisode++;

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
                                            IsBroadcastOnly = e.IsBroadcastOnly,
                                            CreditSeq = entrySeq
                                        };
                                        AddPerson(paid, personInv);

                                        // 所属屋号が指定されている場合、屋号側からも逆引きできるよう
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
                                                IsBroadcastOnly = e.IsBroadcastOnly,
                                                CreditSeq = entrySeq
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
                                            IsBroadcastOnly = e.IsBroadcastOnly,
                                            CreditSeq = entrySeq
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
                                            IsBroadcastOnly = e.IsBroadcastOnly,
                                            CreditSeq = entrySeq
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return creditSeqInEpisode;
        }

        // SeriesGenerator や EpisodeGenerator のクレジット走査と同じ階層を辿る。
        // EpisodeId は SERIES スコープ（Credit.ScopeKind="SERIES"）のとき null で記録する。
        foreach (var (seriesId, eps) in ctx.EpisodesBySeries)
        {
            if (!ctx.SeriesById.TryGetValue(seriesId, out _)) continue;

            foreach (var ep in eps)
            {
                // 同一エピソード内のクレジット表示順での出現位置（0 始まり）。
                int creditSeqInEpisode = 0;

                // credits は明示順序カラム credit_seq を持つ（同一スコープ内 1 始まり、
                // 運用者がクレジット編集画面で並べ替える）。BuildContext.CreditsByEpisode は既に
                // credit_seq, credit_id 昇順（is_deleted=0 のみ）で保持しているが、ここでも保険として
                // 明示ソートし、creditSeqInEpisode の採番が credit_seq 序列を厳密に反映するようにする。
                var credits = (ctx.CreditsByEpisode.TryGetValue(ep.EpisodeId, out var epCredits)
                        ? epCredits
                        : (IReadOnlyList<Credit>)Array.Empty<Credit>())
                    .Where(c => !c.IsDeleted)
                    .OrderBy(c => c.CreditSeq)
                    .ThenBy(c => c.CreditId)
                    .ToList();

                foreach (var credit in credits)
                {
                    creditSeqInEpisode = ProcessCredit(credit, seriesId, creditSeqInEpisode);
                }

                // 当該エピソードで到達した最大 creditSeq を控える。
                // 主題歌ブロックが階層に存在しないエピソードで、song_credits 由来の
                // 主題歌スタッフをクレジット末尾相当に送るためのフォールバック基準。
                // SERIES スコープのみのクレジットは負数キーで別管理される。
                if (creditSeqInEpisode > 0)
                {
                    int epK = ep.EpisodeId;
                    if (!maxSeqByEp.TryGetValue(epK, out var cur) || creditSeqInEpisode - 1 > cur)
                        maxSeqByEp[epK] = creditSeqInEpisode - 1;
                }
            }
        }

        // SERIES-attached クレジット走査（episode_id IS NULL）。
        // 映画系列（series_kinds.credit_attach_to='SERIES' の MOVIE / MOVIE_SHORT / SPRING / EVENT）は
        // エピソードを持たず credits が series_id 直付けで存在する。上のエピソード loop が
        // creditsRepo.GetByEpisodeAsync 経由で credits を引いているため、これらの SERIES-attached
        // クレジットは旧コードでは一切インデックス化されていなかった（人物・企業詳細ページに
        // 映画クレジット由来の関与が出ない原因）。本 loop が補う。
        // creditSeqInSeries は当該シリーズ内 SERIES-attached クレジット間で連続採番する
        // （複数 credit ＝ OP / ED / INSERT / SOUND_TRACK が同一映画の同一カードに乗るケース対応）。
        foreach (var seriesIdOnly in ctx.SeriesById.Keys)
        {
            var seriesCredits = (ctx.CreditsBySeries.TryGetValue(seriesIdOnly, out var rawSeriesCredits)
                    ? rawSeriesCredits
                    : (IReadOnlyList<Credit>)Array.Empty<Credit>())
                .Where(c => !c.IsDeleted
                            && c.EpisodeId == null
                            && string.Equals(c.ScopeKind, "SERIES", StringComparison.Ordinal))
                .OrderBy(c => c.CreditSeq)
                .ThenBy(c => c.CreditId)
                .ToList();
            if (seriesCredits.Count == 0) continue;

            int creditSeqInSeries = 0;
            foreach (var credit in seriesCredits)
            {
                creditSeqInSeries = ProcessCredit(credit, seriesIdOnly, creditSeqInSeries);
            }
        }
        // ── ここまでクレジット階層（credit_block_entries 等）の走査 ──

        // 主題歌・劇伴スタッフ（song_credits / song_recording_singers /
        // bgm_cue_credits 由来）に「クレジット内の主題歌ロールの位置」を与える。
        // クレジット階層に当該エピソードの主題歌ブロックがあれば、その位置の
        // creditSeq を継承する。主題歌種別（OP/ED/INSERT）と親 credit の
        // CreditKind を突き合わせ、OP→OP・ED→ED を優先。一致が無い（INSERT 等）
        // 場合はそのエピソードの最初の主題歌ブロック位置にフォールバックし、
        // 主題歌ブロックが一切無ければクレジット末尾相当（最大 seq の次）に置く。
        // これにより初参加順・役職順・キャラクター順などクレジット順ソートで
        // 主題歌スタッフが主題歌の位置に正しく並ぶ。
        int ResolveThemeCreditSeq(int? episodeId, int seriesIdForRow, string? themeKind)
        {
            int epKey = episodeId ?? -seriesIdForRow;

            // themeKind（OP/ED/INSERT/MOVIE 等）を credit_kinds（OP/ED）へ寄せる。
            // OP/ED はそのまま。それ以外（INSERT/MOVIE/null）は特定の親 CreditKind に
            // 結び付けられないため、エピソード単位のフォールバックに委ねる。
            if (themeKind is not null
                && themeBlockSeqByContext.TryGetValue((epKey, themeKind), out var exact))
                return exact;

            if (firstThemeBlockSeqByEp.TryGetValue(epKey, out var firstTheme))
                return firstTheme;

            // 主題歌ブロックが階層に無い場合はクレジット末尾相当に送る。
            if (episodeId is int eid && maxSeqByEp.TryGetValue(eid, out var maxSeq))
                return maxSeq + 1;

            // 位置情報が全く取れないときは int.MaxValue で「最も後ろ」に。
            return int.MaxValue;
        }

        // 主題歌ブロック内の副順序を算出する。
        // 同一の主題歌ロールブロック（同一 CreditSeq）に作詞・作曲・編曲が
        // ぶら下がるため、その内部順序を：
        //   役割順 LYRICS(0) → COMPOSITION(1) → ARRANGEMENT(2) → その他(3)
        //   を上位、同一役割内は song_credits.credit_seq（連名順, 1始まり）を下位
        // にして単一 int に畳む。roles マスタに並び順カラムは無いため、
        // この役割順は SongCreditsRepository と同じ FIELD() 順を踏襲する。
        // 歌唱（song_recording_singers）は作家連名の後段に置くため役割順 4 とする。
        const int ConnoteStride = 1000; // 1 役割あたりの連名上限の余裕
        static int SongCreditSubSeq(string creditRole, int connoteSeq)
        {
            int roleOrder = creditRole switch
            {
                "LYRICS" => 0,
                "COMPOSITION" => 1,
                "ARRANGEMENT" => 2,
                _ => 3
            };
            return roleOrder * ConnoteStride + connoteSeq;
        }
        // 歌唱（song_recording_singers）は作家連名（役割順 0..3）の後段に置く。
        // 歌唱内は VOCALS → BACKING_VOCALS（=コーラス）、同一役割内は singer_seq（1始まり）。
        static int SingerSubSeq(string roleCode, int singerSeq)
        {
            int roleOrder = roleCode switch
            {
                "VOCALS" => 4,
                "BACKING_VOCALS" => 5,
                _ => 6
            };
            return roleOrder * ConnoteStride + singerSeq;
        }
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
        const string sqlSongCredits = """
            SELECT
              ets.episode_id              AS EpisodeId,
              ep.series_id                AS SeriesId,
              ets.is_broadcast_only       AS IsBroadcastOnly,
              ets.usage_actuality         AS UsageActuality,
              ets.theme_kind              AS ThemeKind,
              sc.credit_role              AS CreditRole,
              sc.credit_seq               AS ConnoteSeq,
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
                    ThemeKind = r.ThemeKind,
                    // クレジット階層上の主題歌ロールの位置を継承（初参加順・
                    // 役職順・キャラクター順などクレジット順ソートで主題歌の位置に並ぶ）。
                    CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, r.ThemeKind),
                    // 主題歌ブロック内は作詞→作曲→編曲→（連名順）で並べる。
                    CreditSubSeq = SongCreditSubSeq(r.CreditRole, r.ConnoteSeq)
                });
                songCreditCount++;
            }
            ctx.Logger.Info($"  song_credits scanned: {songCreditCount} involvements (excluded BROADCAST_NOT_CREDITED)");
        }

        // ── 2) song_recording_singers 巡回 ──
        const string sqlSingers = """
            SELECT
              ets.episode_id              AS EpisodeId,
              ep.series_id                AS SeriesId,
              ets.is_broadcast_only       AS IsBroadcastOnly,
              ets.usage_actuality         AS UsageActuality,
              ets.theme_kind              AS ThemeKind,
              srs.role_code               AS RoleCode,
              srs.singer_seq              AS SingerSeq,
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
                            ThemeKind = r.ThemeKind,
                            CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, r.ThemeKind),
                            CreditSubSeq = SingerSubSeq(r.RoleCode, r.SingerSeq)
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
                            ThemeKind = r.ThemeKind,
                            CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, r.ThemeKind),
                            CreditSubSeq = SingerSubSeq(r.RoleCode, r.SingerSeq)
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
                            ThemeKind = r.ThemeKind,
                            CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, r.ThemeKind),
                            CreditSubSeq = SingerSubSeq(r.RoleCode, r.SingerSeq)
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
                            ThemeKind = r.ThemeKind,
                            CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, r.ThemeKind),
                            CreditSubSeq = SingerSubSeq(r.RoleCode, r.SingerSeq)
                        };
                        AddCharacter(schaId, inv);
                        singerCount++;
                    }
                }
            }
            ctx.Logger.Info($"  song_recording_singers scanned: {singerCount} involvements (excluded BROADCAST_NOT_CREDITED)");
        }

        // ── 3) bgm_cue_credits 巡回 ──
        // 劇伴の作曲 / 編曲の連名を集計。エピソード紐付けは episode_uses 経由。
        // episode_uses は SONG / BGM / DRAMA / RADIO / JINGLE / OTHER の各種コンテンツの使用記録を
        // 1 テーブルにまとめており、BGM のレコードは content_kind_code='BGM' で識別する。
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
                    IsBroadcastOnly = r.IsBroadcastOnly != 0,
                    // 劇伴は主題歌種別を持たないため、エピソード単位フォールバック
                    // （主題歌ブロックがあればその位置、無ければクレジット末尾相当）。
                    CreditSeq = ResolveThemeCreditSeq(r.EpisodeId, r.SeriesId, null),
                    // 劇伴は主題歌ブロック内の役割細別を持たないため副順序は 0。
                    CreditSubSeq = 0
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

/// <summary>関与レコード 1 件（人物・企業共用）。 クレジット階層上の 1 つの参照点に対応する。</summary>
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

    /// <summary>PERSON / CHARACTER_VOICE エントリの person_alias_id。 キャラクター名義側からの逆引き（<see cref="CreditInvolvementIndex.ByCharacterAlias"/>）の 結果から声優を取り出すために使う。</summary>
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
    /// 人物詳細のクレジット履歴で、主題歌の作詞・作曲・編曲・歌唱を OP / ED / 挿入歌で別グループに
    /// 分けて見せるために使う。ラベル展開時は <c>song_music_classes</c> マスタの <c>name_ja</c> を
    /// 引いて「オープニング主題歌 作曲」「エンディング主題歌 編曲」のような自然な日本語に展開する。
    /// </summary>
    public string? ThemeKind { get; init; }

    /// <summary>
    /// 同一エピソード内のクレジット表示順での出現位置（0 始まりの連番）。
    /// クレジット階層を表示順（credit_kind 昇順 → card_seq → tier_no → group_no →
    /// order_in_group → block_seq → entry_seq）で走査する過程で、エピソードごとに
    /// 単調増加で採番した値。<c>credit_block_entries</c>・<c>credit_role_blocks</c>
    /// （ブロック先頭企業）など、クレジット関連テーブルの並び順から導出される。
    /// 「同じ話数内では初めてクレジットされた位置の順」で人物・企業/団体・役職を
    /// 並べるためのキー。集計側は、(シリーズ放送開始日, シリーズ内話数) が同点の
    /// ときの第 3 ソートキーとして、当該エンティティ・役職がそのエピソードで
    /// 最初に出現した（最小の）本値を用いる。SERIES スコープのクレジット
    /// （<see cref="EpisodeId"/> が null）でも採番自体は行われるが、話数同点の
    /// タイブレークは実質エピソード単位でのみ意味を持つ。
    /// roles マスタの <c>display_order</c>（管理画面の表示順にすぎない）には依存しない。
    /// </summary>
    public int CreditSeq { get; init; }

    /// <summary>
    /// <see cref="CreditSeq"/> が同値（＝クレジット階層上の同一位置）になる関与の
    /// あいだでの副順序。0 始まり。
    /// クレジット階層由来の通常エントリは 1 物理位置＝1 エントリなので 0。
    /// 主題歌スタッフ（song_credits 由来の作詞・作曲・編曲）は、主題歌ロール
    /// ブロックという 1 つの位置（同一 <see cref="CreditSeq"/>）に複数人がぶら下がる
    /// ため、その内部順序をここで表す：
    /// 役割順 LYRICS→COMPOSITION→ARRANGEMENT を上位、同一役割内は
    /// <c>song_credits.credit_seq</c>（連名順）を下位にした値。
    /// 歌唱（song_recording_singers）も主題歌ブロック内の歌唱枠として
    /// 作家連名の後ろに並ぶよう採番する。
    /// ソートは常に (<see cref="CreditSeq"/>, <see cref="CreditSubSeq"/>) の
    /// 辞書順で行う。これにより「クレジット関連テーブルの並び（card_seq →
    /// tier_no → group_no → order_in_group → block_seq → entry_seq、主題歌は
    /// さらに役割順→連名順）」が厳密に再現される。
    /// </summary>
    public int CreditSubSeq { get; init; }
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
    /// <summary>PERSON / CHARACTER_VOICE エントリの所属屋号としての参照。 「○○（東映アニメーション）」のような所属付きクレジット表記で、 屋号側から「当該屋号に所属していた人物名義」を逆引きできるようにする。 企業詳細ページの「メンバー履歴」セクションがこの種別で <see cref="CreditInvolvementIndex.ByCompanyAlias"/> を絞り込んで使う。</summary>
    Member = 5
}

// 追加した SQL クエリ用 DTO 群。

/// <summary>song_credits 巡回 SQL の受け取り DTO。</summary>
internal sealed class SongCreditInvRow
{
    public int EpisodeId { get; set; }
    public int SeriesId { get; set; }
    public byte IsBroadcastOnly { get; set; }
    public string UsageActuality { get; set; } = "NORMAL";
    public string CreditRole { get; set; } = "";
    public int PersonAliasId { get; set; }
    /// <summary>同一 credit_role 内の連名順（song_credits.credit_seq）。</summary>
    public byte ConnoteSeq { get; set; }
    /// <summary>主題歌種別（OP / ED / INSERT）。 episode_theme_songs.theme_kind から SELECT する。人物詳細クレジット履歴で 主題歌の作詞・作曲・編曲を OP / ED / 挿入歌のサブグループに分けるための分類軸。</summary>
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
    public byte SingerSeq { get; set; }
    public string BillingKind { get; set; } = "PERSON";
    public int? PersonAliasId { get; set; }
    public int? CharacterAliasId { get; set; }
    public int? VoicePersonAliasId { get; set; }
    public int? SlashPersonAliasId { get; set; }
    public int? SlashCharacterAliasId { get; set; }
    /// <summary>主題歌種別（OP / ED / INSERT）。 歌唱クレジット（VOCALS / CHORUS）も主題歌のテーマ種別ごとに分類できるよう、 episode_theme_songs.theme_kind を伝達する。</summary>
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