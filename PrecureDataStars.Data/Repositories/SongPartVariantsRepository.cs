using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>song_part_variants テーブル（曲のパート種別マスタ）の読み取り・UPSERT リポジトリ。 通常歌入り・カラオケ・コーラス入り・ガイドメロディ入り等のパート種別を扱う。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class SongPartVariantsRepository : SimpleCodeMasterRepository<SongPartVariant>
{
    /// <summary><see cref="SongPartVariantsRepository"/> の新しいインスタンスを生成する。</summary>
    public SongPartVariantsRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "song_part_variants";
    protected override string CodeColumn => "variant_code";
    protected override string CodeProperty => nameof(SongPartVariant.VariantCode);
}
