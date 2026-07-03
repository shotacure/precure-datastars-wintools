using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>product_kinds テーブル（商品種別マスタ）の読み取りリポジトリ。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class ProductKindsRepository : SimpleCodeMasterRepository<ProductKind>
{
    /// <summary><see cref="ProductKindsRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public ProductKindsRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "product_kinds";
    protected override string CodeColumn => "kind_code";
    protected override string CodeProperty => nameof(ProductKind.KindCode);
}
