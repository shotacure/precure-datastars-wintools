using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>song_size_variants テーブル（曲のサイズ種別マスタ）の読み取りリポジトリ。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class SongSizeVariantsRepository : SimpleCodeMasterRepository<SongSizeVariant>
{
    /// <summary><see cref="SongSizeVariantsRepository"/> の新しいインスタンスを生成する。</summary>
    public SongSizeVariantsRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "song_size_variants";
    protected override string CodeColumn => "variant_code";
    protected override string CodeProperty => nameof(SongSizeVariant.VariantCode);
}
