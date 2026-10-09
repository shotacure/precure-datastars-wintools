namespace PrecureDataStars.Data.Models;

/// <summary>
/// Blu-ray の BDMV 管理ファイル（暗号化されていない範囲）から読んだものをひとまとめにした器（bd_* テーブル群）。
/// 鍵はディスクから取れる <see cref="BdDisc.DiscId"/> で、商品・盤の登録を前提にしない。
/// BDAnalyzer が組み立て、<see cref="Repositories.BdStructureRepository.ReplaceAllAsync"/> が盤単位で置き換える。
/// </summary>
public sealed class BdStructure
{
    public BdDisc Disc { get; set; } = new();
    public List<BdTitle> Titles { get; } = new();
    public List<BdMovieObject> MovieObjects { get; } = new();
    public List<BdMovieObjectCommand> MovieObjectCommands { get; } = new();
    public List<BdPlaylist> Playlists { get; } = new();
    public List<BdPlayItem> PlayItems { get; } = new();
    public List<BdPlaylistMark> Marks { get; } = new();
    public List<BdChapter> Chapters { get; } = new();
    public List<BdSubPath> SubPaths { get; } = new();
    public List<BdClip> Clips { get; } = new();
    public List<BdClipStream> ClipStreams { get; } = new();

    /// <summary>全行の disc_id を <see cref="Disc"/> のものにそろえる（登録の直前に呼ぶ）。</summary>
    public void PropagateDiscId()
    {
        string id = Disc.DiscId;
        foreach (var t in Titles) t.DiscId = id;
        foreach (var m in MovieObjects) m.DiscId = id;
        foreach (var c in MovieObjectCommands) c.DiscId = id;
        foreach (var p in Playlists) p.DiscId = id;
        foreach (var i in PlayItems) i.DiscId = id;
        foreach (var m in Marks) m.DiscId = id;
        foreach (var c in Chapters) c.DiscId = id;
        foreach (var s in SubPaths) s.DiscId = id;
        foreach (var c in Clips) c.DiscId = id;
        foreach (var s in ClipStreams) s.DiscId = id;
    }
}

/// <summary>bd_discs の 1 行（盤全体の情報）。</summary>
public sealed class BdDisc
{
    /// <summary>ディスク ID（CERTIFICATE/id.bdmv の 16 バイトの 16 進。無い盤は管理ファイルのハッシュ）。</summary>
    public string DiscId { get; set; } = "";
    /// <summary>ID_BDMV / HASH。</summary>
    public string DiscIdSource { get; set; } = "ID_BDMV";
    public string? OrgId { get; set; }
    public string? VolumeLabel { get; set; }
    public string? BdmtName { get; set; }
    public string? BdmtLanguage { get; set; }
    public byte? BdmtThumbnailCount { get; set; }
    public byte? IndexVideoFormat { get; set; }
    public byte? IndexFrameRate { get; set; }
    /// <summary>NONE / HDMV / BDJ。</summary>
    public string? FirstPlaybackKind { get; set; }
    public string? TopMenuKind { get; set; }
    public ushort? TitleCount { get; set; }
    public ushort? PlaylistCount { get; set; }
    public ushort? ClipCount { get; set; }
    public ulong? M2tsTotalBytes { get; set; }
    public bool HasAacs { get; set; }
    public bool HasBdj { get; set; }
    public ushort? BdjoCount { get; set; }
    public ushort? JarCount { get; set; }
    public ushort? SoundEffectCount { get; set; }
    /// <summary>当てた作品（→ series）。</summary>
    public int? SeriesId { get; set; }
    /// <summary>結びつけた盤の品番（→ discs。任意）。</summary>
    public string? CatalogNo { get; set; }
    public DateTime? FirstReadAt { get; set; }
    public DateTime? LastReadAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>bd_titles の 1 行（index.bdmv のタイトル。0=最初の再生、65535=トップメニュー、1〜=タイトル）。</summary>
public sealed class BdTitle
{
    public string DiscId { get; set; } = "";
    public ushort TitleNo { get; set; }
    /// <summary>HDMV / BDJ。</summary>
    public string ObjectKind { get; set; } = "HDMV";
    public byte? AccessType { get; set; }
    public byte? PlaybackType { get; set; }
    public ushort? MobjNo { get; set; }
    public string? BdjoFile { get; set; }
    public string? PlaylistFile { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>bd_movie_objects の 1 行。</summary>
public sealed class BdMovieObject
{
    public string DiscId { get; set; } = "";
    public ushort MobjNo { get; set; }
    public bool ResumeIntention { get; set; }
    public bool MenuCallMask { get; set; }
    public bool TitleSearchMask { get; set; }
    public ushort CommandCount { get; set; }
    public string? PlaylistFile { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>bd_movie_object_commands の 1 行（ナビゲーション命令の生データ）。</summary>
public sealed class BdMovieObjectCommand
{
    public string DiscId { get; set; } = "";
    public ushort MobjNo { get; set; }
    public ushort CmdSeq { get; set; }
    public string OpcodeHex { get; set; } = "";
    public uint DstOperand { get; set; }
    public uint SrcOperand { get; set; }
}

/// <summary>bd_playlists の 1 行。</summary>
public sealed class BdPlaylist
{
    public string DiscId { get; set; } = "";
    public string PlaylistFile { get; set; } = "";
    public ulong DurationMs { get; set; }
    public ushort PlayItemCount { get; set; }
    public ushort SubPathCount { get; set; }
    public ushort MarkCount { get; set; }
    public byte? PlaybackType { get; set; }
    public ulong? UoMask { get; set; }
    /// <summary>EPISODE / PLAY_ALL / BONUS / MENU / OTHER。NULL=未判定。</summary>
    public string? PlaylistKind { get; set; }
    public int? EpisodeId { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>bd_play_items の 1 行。</summary>
public sealed class BdPlayItem
{
    public string DiscId { get; set; } = "";
    public string PlaylistFile { get; set; } = "";
    public ushort ItemSeq { get; set; }
    public string ClipFile { get; set; } = "";
    public string CodecId { get; set; } = "M2TS";
    public ulong InTimeMs { get; set; }
    public ulong OutTimeMs { get; set; }
    public ulong PlaylistOffsetMs { get; set; }
    public byte ConnectionCondition { get; set; } = 1;
    public byte StcId { get; set; }
}

/// <summary>bd_playlist_marks の 1 行。</summary>
public sealed class BdPlaylistMark
{
    public string DiscId { get; set; } = "";
    public string PlaylistFile { get; set; } = "";
    public ushort MarkSeq { get; set; }
    public byte MarkType { get; set; }
    public ushort PlayItemRef { get; set; }
    public ulong TimeMs { get; set; }
    public ushort? EntryEsPid { get; set; }
    public ulong DurationMs { get; set; }
}

/// <summary>bd_chapters の 1 行（Entry マークで区切った生の区間と、当てた話のパート）。</summary>
public sealed class BdChapter
{
    public string DiscId { get; set; } = "";
    public string PlaylistFile { get; set; } = "";
    public ushort ChapterNo { get; set; }
    /// <summary>プレイリスト時間軸での開始時刻（ミリ秒。生の値）。</summary>
    public ulong StartTimeMs { get; set; }
    /// <summary>尺（ミリ秒。生の値。話の最後のチャプターには 1 秒の余白が付く）。</summary>
    public ulong DurationMs { get; set; }
    /// <summary>EPISODE_PART / BLANK / BONUS / OTHER。NULL=未判定。</summary>
    public string? ChapterKind { get; set; }
    public int? EpisodeId { get; set; }
    public byte? EpisodeSeq { get; set; }
}

/// <summary>bd_sub_paths の 1 行。</summary>
public sealed class BdSubPath
{
    public string DiscId { get; set; } = "";
    public string PlaylistFile { get; set; } = "";
    public ushort SubPathSeq { get; set; }
    public byte SubPathType { get; set; }
    public bool IsRepeat { get; set; }
    public ushort SubPlayItemCount { get; set; }
    public string? FirstClipFile { get; set; }
}

/// <summary>bd_clips の 1 行。</summary>
public sealed class BdClip
{
    public string DiscId { get; set; } = "";
    public string ClipFile { get; set; } = "";
    public ulong? PresentationStartMs { get; set; }
    public ulong? PresentationEndMs { get; set; }
    public uint? TsRecordingRate { get; set; }
    public uint? SourcePackets { get; set; }
    public byte? ApplicationType { get; set; }
    public byte? ClipStreamType { get; set; }
    public ulong? FileSizeBytes { get; set; }
}

/// <summary>bd_clip_streams の 1 行。コード値は規格のまま持つ。</summary>
public sealed class BdClipStream
{
    public string DiscId { get; set; } = "";
    public string ClipFile { get; set; } = "";
    public ushort StreamPid { get; set; }
    public string StreamKind { get; set; } = "OTHER";
    public byte CodingType { get; set; }
    public byte? VideoFormat { get; set; }
    public byte? FrameRate { get; set; }
    public byte? AudioPresentation { get; set; }
    public byte? SamplingRate { get; set; }
    public string? Language { get; set; }
}
