using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>song_music_classes テーブル（曲の音楽種別マスタ）の読み取りリポジトリ。 CRUD 実体は <see cref="SimpleCodeMasterRepository{T}"/>（単純コード型マスタ共通基底）を参照。</summary>
public sealed class SongMusicClassesRepository : SimpleCodeMasterRepository<SongMusicClass>
{
    /// <summary><see cref="SongMusicClassesRepository"/> の新しいインスタンスを生成する。</summary>
    public SongMusicClassesRepository(IConnectionFactory factory) : base(factory) { }

    protected override string Table => "song_music_classes";
    protected override string CodeColumn => "class_code";
    protected override string CodeProperty => nameof(SongMusicClass.ClassCode);
}
