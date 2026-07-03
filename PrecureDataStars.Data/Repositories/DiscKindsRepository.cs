using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>disc_kinds テーブル（ディスク用途種別マスタ）の読み取りリポジトリ。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class DiscKindsRepository : SimpleCodeMasterRepository<DiscKind>
{
    /// <summary><see cref="DiscKindsRepository"/> の新しいインスタンスを生成する。</summary>
    public DiscKindsRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "disc_kinds";
    protected override string CodeColumn => "kind_code";
    protected override string CodeProperty => nameof(DiscKind.KindCode);
}
