using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クレジット階層を 1 度だけ走査して、「ある名義 (alias) / ロゴがどのエピソードのどの役職で
/// 登場したか」を逆引きできるインデックスを構築する（v1.3.1 追加）。
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
                                                    IsBroadcastOnly = e.IsBroadcastOnly
                                                };
                                                AddPerson(paid, personInv);

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

    /// <summary>本放送限定エントリかどうか（is_broadcast_only=1 のレコードを示す）。</summary>
    public bool IsBroadcastOnly { get; init; }
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
    LeadingCompany = 4
}
