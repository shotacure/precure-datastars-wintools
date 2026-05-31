using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// クレジット編集テキストエリアの「役職コンテキストごとの最近使用名義候補」を引くための
/// 専用リポジトリ。指定役職クラスタ（role_successions 連結成分）に過去出現した
/// person_alias / company_alias と、そのときの掲載文脈（series_id・on_air_at・entry_seq）を
/// まとめて取得する。スコアリング（指数減衰 × シリーズブースト × ブロック内位置一致）は呼び出し側で行う。
/// </summary>
public sealed partial class RoleAliasUsageRepository
{
    private readonly IConnectionFactory _factory;

    public RoleAliasUsageRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleAliasUsage>(
            new CommandDefinition(sql,
                new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
                cancellationToken: ct));
        return rows.ToList();
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleAliasUsage>(
            new CommandDefinition(sql,
                new { RoleCodes = roleCodes, AnchorDate = anchorDate, LookbackDays = lookbackDays },
                cancellationToken: ct));
        return rows.ToList();
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleAliasCoOccurrence>(
            new CommandDefinition(sql,
                new
                {
                    RoleCodes = roleCodes,
                    ExistingIds = existingPersonAliasIds,
                    AnchorDate = anchorDate,
                    LookbackDays = lookbackDays
                },
                cancellationToken: ct));
        return rows.ToList();
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleAliasUsage>(
            new CommandDefinition(sql,
                new
                {
                    RoleCodes = roleCodes,
                    LeadingCompanyAliasId = leadingCompanyAliasId,
                    AnchorDate = anchorDate,
                    LookbackDays = lookbackDays
                },
                cancellationToken: ct));
        return rows.ToList();
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RoleAliasCoOccurrence>(
            new CommandDefinition(sql,
                new
                {
                    RoleCodes = roleCodes,
                    ExistingIds = existingCompanyAliasIds,
                    AnchorDate = anchorDate,
                    LookbackDays = lookbackDays
                },
                cancellationToken: ct));
        return rows.ToList();
    }
}
