using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// precures テーブル（プリキュア本体マスタ）の CRUD リポジトリ。
/// <para>
/// 4 本の alias FK（変身前 / 変身後 / 変身後 2 / 別形態）が指す character_id は
/// すべて同一でなければならない（DB 側のトリガで強制）。アプリ側でも保存前に
/// 一致確認を行うのが望ましいが、最終ガードはトリガに委ねる設計。
/// </para>
/// </summary>
public sealed class PrecuresRepository
{
    private readonly IConnectionFactory _factory;

    public PrecuresRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // SELECT 共通列。新規列が増えたらここに 1 行足すだけで全メソッドに反映される。
    private const string SelectColumns = """
          precure_id              AS PrecureId,
          pre_transform_alias_id  AS PreTransformAliasId,
          transform_alias_id      AS TransformAliasId,
          transform2_alias_id     AS Transform2AliasId,
          alt_form_alias_id       AS AltFormAliasId,
          birth_month             AS BirthMonth,
          birth_day               AS BirthDay,
          voice_actor_person_id   AS VoiceActorPersonId,
          skin_color_h            AS SkinColorH,
          skin_color_s            AS SkinColorS,
          skin_color_l            AS SkinColorL,
          skin_color_r            AS SkinColorR,
          skin_color_g            AS SkinColorG,
          skin_color_b            AS SkinColorB,
          school                  AS School,
          school_class            AS SchoolClass,
          family_business         AS FamilyBusiness,
          notes                   AS Notes,
          created_at              AS CreatedAt,
          updated_at              AS UpdatedAt,
          created_by              AS CreatedBy,
          updated_by              AS UpdatedBy,
          is_deleted              AS IsDeleted
        """;

    /// <summary>
    /// 全件取得（precure_id 昇順）。論理削除済みは既定で除外する。
    /// </summary>
    public async Task<IReadOnlyList<Precure>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM precures
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY precure_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Precure>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（precure_id）で 1 件取得する。</summary>
    public async Task<Precure?> GetByIdAsync(int precureId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM precures
            WHERE precure_id = @precureId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Precure>(
            new CommandDefinition(sql, new { precureId }, cancellationToken: ct));
    }

    /// <summary>新規作成。AUTO_INCREMENT の precure_id を返す。</summary>
    /// <remarks>
    /// 4 本の alias FK が指す character_id の同一性検証は DB トリガに委ねる。
    /// 不整合があれば SqlException（SQLSTATE '45000'）として呼び出し側に伝播する。
    /// </remarks>
    public async Task<int> InsertAsync(Precure row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO precures
              (pre_transform_alias_id, transform_alias_id, transform2_alias_id, alt_form_alias_id,
               birth_month, birth_day, voice_actor_person_id,
               skin_color_h, skin_color_s, skin_color_l,
               skin_color_r, skin_color_g, skin_color_b,
               school, school_class, family_business, notes,
               created_by, updated_by)
            VALUES
              (@PreTransformAliasId, @TransformAliasId, @Transform2AliasId, @AltFormAliasId,
               @BirthMonth, @BirthDay, @VoiceActorPersonId,
               @SkinColorH, @SkinColorS, @SkinColorL,
               @SkinColorR, @SkinColorG, @SkinColorB,
               @School, @SchoolClass, @FamilyBusiness, @Notes,
               @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Precure row, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE precures SET
              pre_transform_alias_id = @PreTransformAliasId,
              transform_alias_id     = @TransformAliasId,
              transform2_alias_id    = @Transform2AliasId,
              alt_form_alias_id      = @AltFormAliasId,
              birth_month            = @BirthMonth,
              birth_day              = @BirthDay,
              voice_actor_person_id  = @VoiceActorPersonId,
              skin_color_h           = @SkinColorH,
              skin_color_s           = @SkinColorS,
              skin_color_l           = @SkinColorL,
              skin_color_r           = @SkinColorR,
              skin_color_g           = @SkinColorG,
              skin_color_b           = @SkinColorB,
              school                 = @School,
              school_class           = @SchoolClass,
              family_business        = @FamilyBusiness,
              notes                  = @Notes,
              updated_by             = @UpdatedBy,
              is_deleted             = @IsDeleted
            WHERE precure_id = @PrecureId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int precureId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE precures SET is_deleted = 1, updated_by = @UpdatedBy WHERE precure_id = @PrecureId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PrecureId = precureId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }

    /// <summary>物理削除（マスタ整理用、参照完全性は呼び出し側責任）。</summary>
    public async Task DeleteAsync(int precureId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM precures WHERE precure_id = @PrecureId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PrecureId = precureId }, cancellationToken: ct));
    }

    /// <summary>
    /// 一覧表示用の結合クエリ。precure 行に「変身前 / 変身後の名義文字列」と
    /// 「声優の表示名」を JOIN して返す軽量プロジェクション。
    /// マスタ管理画面のグリッド表示で使用する。
    /// </summary>
    public async Task<IReadOnlyList<PrecureListRow>> GetListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              p.precure_id              AS PrecureId,
              p.pre_transform_alias_id  AS PreTransformAliasId,
              ca_pre.name               AS PreTransformName,
              p.transform_alias_id      AS TransformAliasId,
              ca_main.name              AS TransformName,
              p.birth_month             AS BirthMonth,
              p.birth_day               AS BirthDay,
              p.voice_actor_person_id   AS VoiceActorPersonId,
              per.full_name             AS VoiceActorName,
              p.school                  AS School,
              p.school_class            AS SchoolClass,
              p.is_deleted              AS IsDeleted
            FROM precures p
            LEFT JOIN character_aliases ca_pre  ON ca_pre.alias_id  = p.pre_transform_alias_id
            LEFT JOIN character_aliases ca_main ON ca_main.alias_id = p.transform_alias_id
            LEFT JOIN persons per               ON per.person_id    = p.voice_actor_person_id
            WHERE p.is_deleted = 0
            ORDER BY p.precure_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PrecureListRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }
}

/// <summary>
/// プリキュア一覧グリッド表示用の軽量プロジェクション。
/// alias / person の表示名を結合済みで持つため、グリッドはこの型をそのままバインドできる。
/// </summary>
public sealed class PrecureListRow
{
    public int PrecureId { get; set; }
    public int PreTransformAliasId { get; set; }
    public string? PreTransformName { get; set; }
    public int TransformAliasId { get; set; }
    public string? TransformName { get; set; }
    public byte? BirthMonth { get; set; }
    public byte? BirthDay { get; set; }
    public int? VoiceActorPersonId { get; set; }
    public string? VoiceActorName { get; set; }
    public string? School { get; set; }
    public string? SchoolClass { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>誕生日の和文表示（例: "7月1日"）。月日のいずれかが NULL なら空文字。</summary>
    public string BirthdayJa
        => (BirthMonth.HasValue && BirthDay.HasValue) ? $"{BirthMonth}月{BirthDay}日" : "";
}