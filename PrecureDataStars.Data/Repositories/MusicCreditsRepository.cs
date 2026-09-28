using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// music_credits テーブル（音盤のブックレットに載る音楽クレジット）の読み取りリポジトリ。
/// 投入は画像を元にした SQL の直接投入で行うため、書き込み系のメソッドは持たない。
/// </summary>
public sealed class MusicCreditsRepository : RepositoryBase
{
    public MusicCreditsRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>全行を取得する（紐付け先ごとに credit_seq 順）。SiteBuilder が起動時に 1 度だけ呼んで辞書化する。</summary>
    public async Task<IReadOnlyList<MusicCredit>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              music_credit_id              AS MusicCreditId,
              target_kind                  AS TargetKind,
              song_id                      AS SongId,
              song_recording_id            AS SongRecordingId,
              bgm_series_id                AS BgmSeriesId,
              bgm_session_no               AS BgmSessionNo,
              product_catalog_no           AS ProductCatalogNo,
              role_code                    AS RoleCode,
              credit_seq                   AS CreditSeq,
              entry_kind                   AS EntryKind,
              person_alias_id              AS PersonAliasId,
              character_alias_id           AS CharacterAliasId,
              company_alias_id             AS CompanyAliasId,
              raw_text                     AS RawText,
              printed_text                 AS PrintedText,
              is_misprint                  AS IsMisprint,
              role_label_text              AS RoleLabelText,
              ensemble_note                AS EnsembleNote,
              affiliation_company_alias_id AS AffiliationCompanyAliasId,
              affiliation_text             AS AffiliationText,
              preceding_separator          AS PrecedingSeparator,
              source_product_catalog_no    AS SourceProductCatalogNo,
              notes                        AS Notes,
              created_at                   AS CreatedAt,
              updated_at                   AS UpdatedAt,
              created_by                   AS CreatedBy,
              updated_by                   AS UpdatedBy
            FROM music_credits
            ORDER BY target_kind, song_id, song_recording_id, bgm_series_id, bgm_session_no, product_catalog_no, credit_seq, music_credit_id;
            """;

        return await QueryListAsync<MusicCredit>(sql, ct: ct).ConfigureAwait(false);
    }
}
