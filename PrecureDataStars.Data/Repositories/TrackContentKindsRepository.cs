using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>track_content_kinds テーブル（トラック内容種別マスタ）の読み取りリポジトリ。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class TrackContentKindsRepository : SimpleCodeMasterRepository<TrackContentKind>
{
    /// <summary><see cref="TrackContentKindsRepository"/> の新しいインスタンスを生成する。</summary>
    public TrackContentKindsRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "track_content_kinds";
    protected override string CodeColumn => "kind_code";
    protected override string CodeProperty => nameof(TrackContentKind.KindCode);
}
