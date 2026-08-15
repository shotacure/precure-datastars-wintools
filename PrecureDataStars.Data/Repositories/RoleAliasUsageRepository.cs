using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// クレジット編集テキストエリアの「役職コンテキストごとの最近使用名義候補」を引くための
/// 専用リポジトリ。指定役職クラスタ（role_successions 連結成分）に過去出現した
/// person_alias / company_alias と、そのときの掲載文脈（series_id・on_air_at・entry_seq）を
/// まとめて取得する。スコアリング（指数減衰 × シリーズブースト × ブロック内位置一致）は呼び出し側で行う。
/// </summary>
public sealed partial class RoleAliasUsageRepository : RepositoryBase
{
    public RoleAliasUsageRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>役職クラスタに過去出現した PERSON エントリの「使用履歴」を集約済みで返す。
    /// 1 行 = 1 (alias, 使用時刻, シリーズ, ブロック内位置) のサンプル点。
    /// 呼び出し側でグルーピングして指数減衰スコアを合算する。
    /// <paramref name="anchorDate"/> は「現在編集中のクレジットの放送日／シリーズ開始日」で、
    /// その前後 <paramref name="lookbackDays"/> 日以内に放送（または公開）されたクレジットだけを履歴対象とする。
    /// これにより「初代プリキュアを編集してるときは初代周辺の制作陣だけが候補に出る」状態になる。</summary>
    public async Task<IReadOnlyList<RoleAliasUsage>> GetRecentPersonAliasUsagesAsync(
        IReadOnlyList<string> roleCodes,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleAliasUsage>();

        // credit_block_entries → blocks → card_roles → groups → tiers → cards → credits → episodes の
        // 階層を辿って「いつ・どのシリーズで・ブロック内何番目に」その人物名義が使われたかを集める。
        // 履歴側の時刻 t_i は episode.on_air_at（EPISODE スコープ）または series.start_date（SERIES スコープ）
        // を採用する。これにより「同時代の作品で同じ役職に誰が入っていたか」が決定的に取れる。
        // anchor との差が ±lookbackDays 以内のものだけ返す。
        const string sql = """
            SELECT
              e.person_alias_id   AS AliasId,
              pa.name             AS Name,
              COALESCE(ep.on_air_at, ser.start_date) AS UsedAt,
              COALESCE(ep.series_id, c.series_id)    AS SeriesId,
              e.entry_seq         AS EntrySeq
            FROM credit_block_entries e
            JOIN person_aliases       pa ON pa.alias_id      = e.person_alias_id
            JOIN credit_role_blocks   rb ON rb.block_id      = e.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e.entry_kind = 'PERSON'
              AND e.person_alias_id IS NOT NULL
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
              AND pa.is_deleted = 0;
            """;

        return await QueryListAsync<RoleAliasUsage>(
            sql,
            new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
            ct).ConfigureAwait(false);
    }

    /// <summary>役職クラスタに過去出現した COMPANY エントリの「使用履歴」を集約済みで返す。
    /// 仕様は PERSON 版と同じ（参照列のみ person_alias_id → company_alias_id に差し替え）。</summary>
    public async Task<IReadOnlyList<RoleAliasUsage>> GetRecentCompanyAliasUsagesAsync(
        IReadOnlyList<string> roleCodes,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleAliasUsage>();

        const string sql = """
            SELECT
              e.company_alias_id  AS AliasId,
              ca.name             AS Name,
              COALESCE(ep.on_air_at, ser.start_date) AS UsedAt,
              COALESCE(ep.series_id, c.series_id)    AS SeriesId,
              e.entry_seq         AS EntrySeq
            FROM credit_block_entries e
            JOIN company_aliases      ca ON ca.alias_id      = e.company_alias_id
            JOIN credit_role_blocks   rb ON rb.block_id      = e.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e.entry_kind = 'COMPANY'
              AND e.company_alias_id IS NOT NULL
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
              AND ca.is_deleted = 0;
            """;

        return await QueryListAsync<RoleAliasUsage>(
            sql,
            new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
            ct).ConfigureAwait(false);
    }

    /// <summary>役職クラスタ（通常は「声の出演」系）に過去出現した CHARACTER_VOICE エントリの
    /// 「使用履歴」を集約済みで返す。1 行 = 1 (キャラ, 声優, 使用時刻, シリーズ, ブロック内位置) のサンプル点。
    /// PERSON 版と異なり、候補は「キャラ + 声優」のペア単位で扱う（呼び出し側で複合キーによりグルーピング）。
    /// 仕様（絞り込み条件・列の意味）は PERSON 版と同じ。</summary>
    public async Task<IReadOnlyList<RoleCharacterVoiceUsage>> GetRecentCharacterVoiceUsagesAsync(
        IReadOnlyList<string> roleCodes,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleCharacterVoiceUsage>();

        const string sql = """
            SELECT
              e.character_alias_id AS CharacterAliasId,
              ca.name               AS CharacterName,
              e.person_alias_id     AS VoicePersonAliasId,
              pa.name                AS VoiceName,
              COALESCE(ep.on_air_at, ser.start_date) AS UsedAt,
              COALESCE(ep.series_id, c.series_id)    AS SeriesId,
              e.block_id             AS BlockId,
              e.entry_seq            AS EntrySeq
            FROM credit_block_entries e
            JOIN character_aliases    ca ON ca.alias_id      = e.character_alias_id
            JOIN person_aliases       pa ON pa.alias_id      = e.person_alias_id
            JOIN credit_role_blocks   rb ON rb.block_id      = e.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e.entry_kind = 'CHARACTER_VOICE'
              AND e.character_alias_id IS NOT NULL
              AND e.person_alias_id IS NOT NULL
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
              AND ca.is_deleted = 0
              AND pa.is_deleted = 0;
            """;

        return await QueryListAsync<RoleCharacterVoiceUsage>(
            sql,
            new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
            ct).ConfigureAwait(false);
    }

    /// <summary>役職クラスタに過去出現した LOGO エントリの「使用履歴」を集約済みで返す。
    /// 1 行 = 1 (ロゴ, 使用時刻, シリーズ, ブロック内位置) のサンプル点。COMPANY（entry_kind='COMPANY'）
    /// とは別種別（CI バージョン付き屋号）なので候補生成も別クエリで行う。挿入形式が
    /// <c>[屋号名#company_alias_id#CIラベル]</c> と company_alias_id を要求するため、logo_id に加えて
    /// company_alias_id / CI ラベルも一緒に返す。仕様（絞り込み条件・列の意味）は PERSON 版と同じ。</summary>
    public async Task<IReadOnlyList<RoleLogoUsage>> GetRecentLogoUsagesAsync(
        IReadOnlyList<string> roleCodes,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleLogoUsage>();

        const string sql = """
            SELECT
              e.logo_id            AS LogoId,
              l.company_alias_id   AS CompanyAliasId,
              ca.name               AS CompanyName,
              l.ci_version_label    AS CiVersionLabel,
              COALESCE(ep.on_air_at, ser.start_date) AS UsedAt,
              COALESCE(ep.series_id, c.series_id)    AS SeriesId,
              e.entry_seq            AS EntrySeq
            FROM credit_block_entries e
            JOIN logos                l  ON l.logo_id         = e.logo_id
            JOIN company_aliases      ca ON ca.alias_id       = l.company_alias_id
            JOIN credit_role_blocks   rb ON rb.block_id      = e.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e.entry_kind = 'LOGO'
              AND e.logo_id IS NOT NULL
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
              AND l.is_deleted = 0
              AND ca.is_deleted = 0;
            """;

        return await QueryListAsync<RoleLogoUsage>(
            sql,
            new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
            ct).ConfigureAwait(false);
    }
}

/// <summary>役職クラスタに出現した名義 1 サンプル点（使用履歴 1 行）。
/// 呼び出し側で alias_id でグルーピングしてスコア合算する。</summary>
public sealed class RoleAliasUsage
{
    /// <summary>person_alias_id または company_alias_id。</summary>
    public int AliasId { get; init; }

    /// <summary>名義表示名（候補メニューのラベル用）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>使用時刻（episode.on_air_at 優先、無ければ credit.updated_at）。</summary>
    public DateTime UsedAt { get; init; }

    /// <summary>使用時シリーズ ID（episode 経由 / credit 経由のいずれか）。シリーズブースト判定用。</summary>
    public int? SeriesId { get; init; }

    /// <summary>ブロック内エントリ位置（1 始まり）。ブロック内位置一致スコアに使用。</summary>
    public int EntrySeq { get; init; }
}

/// <summary>役職クラスタに出現した「キャラ + 声優」ペア 1 サンプル点（CHARACTER_VOICE の使用履歴 1 行）。
/// 呼び出し側で (CharacterAliasId, VoicePersonAliasId) の複合キーでグルーピングしてスコア合算する。</summary>
public sealed class RoleCharacterVoiceUsage
{
    /// <summary>character_aliases.alias_id。</summary>
    public int CharacterAliasId { get; init; }

    /// <summary>キャラ表示名（候補メニューのラベル用）。</summary>
    public string CharacterName { get; init; } = string.Empty;

    /// <summary>person_aliases.alias_id（声優側）。</summary>
    public int VoicePersonAliasId { get; init; }

    /// <summary>声優表示名（候補メニューのラベル用）。</summary>
    public string VoiceName { get; init; } = string.Empty;

    /// <summary>使用時刻（episode.on_air_at 優先、無ければ series.start_date）。</summary>
    public DateTime UsedAt { get; init; }

    /// <summary>使用時シリーズ ID（episode 経由 / credit 経由のいずれか）。シリーズブースト判定用。</summary>
    public int? SeriesId { get; init; }

    /// <summary>出現したブロックの block_id。同一ブロック内の前後関係（誰の次に出たか）を
    /// 呼び出し側で復元するために使う。</summary>
    public int BlockId { get; init; }

    /// <summary>ブロック内エントリ位置（1 始まり）。前後関係の復元と位置近接スコアに使用。</summary>
    public int EntrySeq { get; init; }
}

/// <summary>役職クラスタに出現したロゴ 1 サンプル点（LOGO の使用履歴 1 行）。
/// 呼び出し側で <see cref="LogoId"/> でグルーピングしてスコア合算する。挿入形式
/// <c>[屋号名#company_alias_id#CIラベル]</c> の組み立てに <see cref="CompanyAliasId"/> /
/// <see cref="CiVersionLabel"/> も必要なため、COMPANY 版（<see cref="RoleAliasUsage"/>）とは
/// 別の DTO として持つ。</summary>
public sealed class RoleLogoUsage
{
    /// <summary>logos.logo_id。</summary>
    public int LogoId { get; init; }

    /// <summary>company_aliases.alias_id（挿入形式の <c>#company_alias_id</c> 部分）。</summary>
    public int CompanyAliasId { get; init; }

    /// <summary>屋号表示名（候補メニューのラベル用・挿入テキストの屋号名部分）。</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>CI バージョンラベル（候補メニューのラベル用・挿入テキストの <c>#CIラベル</c> 部分）。</summary>
    public string CiVersionLabel { get; init; } = string.Empty;

    /// <summary>使用時刻（episode.on_air_at 優先、無ければ series.start_date）。</summary>
    public DateTime UsedAt { get; init; }

    /// <summary>使用時シリーズ ID（episode 経由 / credit 経由のいずれか）。シリーズブースト判定用。</summary>
    public int? SeriesId { get; init; }

    /// <summary>ブロック内エントリ位置（1 始まり）。ブロック内位置一致スコアに使用。</summary>
    public int EntrySeq { get; init; }
}

/// <summary>過去ブロックに「既入力エントリと一緒に出てきた」alias 単位の共起件数集計。
/// 共起ブースト計算のために使う。
/// 同一ブロック内に <see cref="CoAliasId"/> と「既入力 alias のいずれか」が両方含まれていた
/// 過去ブロック数（重複なし）が <see cref="CoBlockCount"/>。</summary>
public sealed class RoleAliasCoOccurrence
{
    /// <summary>共起ピアの alias_id（こちらを候補スコアにブーストする対象）。</summary>
    public int CoAliasId { get; init; }

    /// <summary>共起ブロック数（既入力 alias のいずれかと同一 block に同居した過去ブロック数、distinct）。</summary>
    public int CoBlockCount { get; init; }
}

public sealed partial class RoleAliasUsageRepository
{
    /// <summary>「既に入力されている PERSON 名義 <paramref name="existingPersonAliasIds"/> と
    /// 過去同一ブロックに同居した PERSON 名義」の共起件数を返す。
    /// 役職クラスタ・アンカー日付窓は <see cref="GetRecentPersonAliasUsagesAsync"/> と同じ条件で絞る。
    /// 戻り値の <see cref="RoleAliasCoOccurrence.CoAliasId"/> は existingPersonAliasIds 自身を除外する。
    /// </summary>
    public async Task<IReadOnlyList<RoleAliasCoOccurrence>> GetPersonCoOccurrencesAsync(
        IReadOnlyList<string> roleCodes,
        IReadOnlyList<int> existingPersonAliasIds,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleAliasCoOccurrence>();
        if (existingPersonAliasIds is null || existingPersonAliasIds.Count == 0) return Array.Empty<RoleAliasCoOccurrence>();

        // 同一 block_id 内の e1（既入力）と e2（共起ピア）を self-join。
        // ピア側 e2 だけ階層を辿って anchor 窓・role クラスタを判定すれば十分（同 block なのでどちらも同条件）。
        const string sql = """
            SELECT
              e2.person_alias_id AS CoAliasId,
              COUNT(DISTINCT e2.block_id) AS CoBlockCount
            FROM credit_block_entries e1
            JOIN credit_block_entries e2 ON e2.block_id = e1.block_id
                                        AND e2.entry_kind = 'PERSON'
                                        AND e2.person_alias_id IS NOT NULL
                                        AND e2.person_alias_id NOT IN @ExistingIds
            JOIN credit_role_blocks   rb ON rb.block_id      = e2.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e1.entry_kind = 'PERSON'
              AND e1.person_alias_id IN @ExistingIds
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
            GROUP BY e2.person_alias_id;
            """;

        return await QueryListAsync<RoleAliasCoOccurrence>(
            sql,
            new
            {
                RoleCodes = roleCodes,
                ExistingIds = existingPersonAliasIds,
                AnchorDate = anchorDate,
                LookbackDays = lookbackDays
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>「指定 <paramref name="leadingCompanyAliasId"/> がブロックトップ屋号として設定されていた
    /// 過去ブロックの PERSON エントリ」を <see cref="RoleAliasUsage"/> サンプル点として返す。
    /// 役職クラスタ・アンカー日付窓は <see cref="GetRecentPersonAliasUsagesAsync"/> と同じ条件で絞る。
    /// 「ブロック先頭に屋号 [[X]] を入れた → 過去同屋号ブロックの所属人物だけを優先候補に出す」用途。
    /// 戻り値は呼び出し側で alias_id 単位に集約してスコアリングする（<see cref="RankUsages"/> 互換）。</summary>
    public async Task<IReadOnlyList<RoleAliasUsage>> GetPersonUsagesUnderLeadingCompanyAsync(
        IReadOnlyList<string> roleCodes,
        int leadingCompanyAliasId,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleAliasUsage>();
        if (leadingCompanyAliasId <= 0) return Array.Empty<RoleAliasUsage>();

        const string sql = """
            SELECT
              e.person_alias_id   AS AliasId,
              pa.name             AS Name,
              COALESCE(ep.on_air_at, ser.start_date) AS UsedAt,
              COALESCE(ep.series_id, c.series_id)    AS SeriesId,
              e.entry_seq         AS EntrySeq
            FROM credit_block_entries e
            JOIN person_aliases       pa ON pa.alias_id      = e.person_alias_id
            JOIN credit_role_blocks   rb ON rb.block_id      = e.block_id
                                        AND rb.leading_company_alias_id = @LeadingCompanyAliasId
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e.entry_kind = 'PERSON'
              AND e.person_alias_id IS NOT NULL
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
              AND pa.is_deleted = 0;
            """;

        return await QueryListAsync<RoleAliasUsage>(
            sql,
            new
            {
                RoleCodes = roleCodes,
                LeadingCompanyAliasId = leadingCompanyAliasId,
                AnchorDate = anchorDate,
                LookbackDays = lookbackDays
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>COMPANY 版の共起件数集計（仕様は PERSON 版と同じ）。</summary>
    public async Task<IReadOnlyList<RoleAliasCoOccurrence>> GetCompanyCoOccurrencesAsync(
        IReadOnlyList<string> roleCodes,
        IReadOnlyList<int> existingCompanyAliasIds,
        DateTime anchorDate,
        int lookbackDays,
        CancellationToken ct = default)
    {
        if (roleCodes is null || roleCodes.Count == 0) return Array.Empty<RoleAliasCoOccurrence>();
        if (existingCompanyAliasIds is null || existingCompanyAliasIds.Count == 0) return Array.Empty<RoleAliasCoOccurrence>();

        const string sql = """
            SELECT
              e2.company_alias_id AS CoAliasId,
              COUNT(DISTINCT e2.block_id) AS CoBlockCount
            FROM credit_block_entries e1
            JOIN credit_block_entries e2 ON e2.block_id = e1.block_id
                                        AND e2.entry_kind = 'COMPANY'
                                        AND e2.company_alias_id IS NOT NULL
                                        AND e2.company_alias_id NOT IN @ExistingIds
            JOIN credit_role_blocks   rb ON rb.block_id      = e2.block_id
            JOIN credit_card_roles    cr ON cr.card_role_id  = rb.card_role_id
            JOIN credit_card_groups   cg ON cg.card_group_id = cr.card_group_id
            JOIN credit_card_tiers    ct ON ct.card_tier_id  = cg.card_tier_id
            JOIN credit_cards         cd ON cd.card_id       = ct.card_id
            JOIN credits              c  ON c.credit_id      = cd.credit_id
            LEFT JOIN episodes        ep ON ep.episode_id    = c.episode_id
            LEFT JOIN series          ser ON ser.series_id   = c.series_id
            WHERE e1.entry_kind = 'COMPANY'
              AND e1.company_alias_id IN @ExistingIds
              AND cr.role_code IN @RoleCodes
              AND COALESCE(ep.on_air_at, ser.start_date) IS NOT NULL
              AND ABS(DATEDIFF(COALESCE(ep.on_air_at, ser.start_date), @AnchorDate)) <= @LookbackDays
            GROUP BY e2.company_alias_id;
            """;

        return await QueryListAsync<RoleAliasCoOccurrence>(
            sql,
            new
            {
                RoleCodes = roleCodes,
                ExistingIds = existingCompanyAliasIds,
                AnchorDate = anchorDate,
                LookbackDays = lookbackDays
            },
            ct).ConfigureAwait(false);
    }
}
