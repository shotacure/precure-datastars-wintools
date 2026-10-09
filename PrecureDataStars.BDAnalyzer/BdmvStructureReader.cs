#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// BDMV フォルダの管理ファイル（暗号化されていない範囲）から、ディスクの情報（<see cref="BdStructure"/>）を片っ端から読み取る。
    /// <list type="bullet">
    ///   <item><description>CERTIFICATE/id.bdmv：組織 ID・ディスク ID（鍵。無い盤や 0 で埋まっている盤は index.bdmv・MovieObject.bdmv・全 .mpls のハッシュで代用）</description></item>
    ///   <item><description>index.bdmv：映像形式・フレームレート、最初の再生・トップメニュー・タイトルの一覧</description></item>
    ///   <item><description>MovieObject.bdmv：ムービーオブジェクトとナビゲーション命令の生データ。PlayPL 系の命令から最初に再生するプレイリストを解く</description></item>
    ///   <item><description>PLAYLIST/*.mpls：再生種別・操作禁止マスク、PlayItem、全マーク、サブパス、Entry マークで区切ったチャプター（生の区間）</description></item>
    ///   <item><description>CLIPINF/*.clpi：クリップの属性とストリーム。STREAM/*.m2ts はサイズだけ見る</description></item>
    ///   <item><description>META/DL/bdmt_*.xml のディスク名、AUXDATA/sound.bdmv の効果音の数、AACS / BDJO / JAR の有無と数、ボリュームラベル</description></item>
    /// </list>
    /// 規格のコード値はそのまま記録し、時刻は 45 kHz tick をミリ秒に換算する。読めないファイルは飛ばし、1 つも読めなくても例外にせず返す。
    /// </summary>
    public static class BdmvStructureReader
    {
        private const int TicksPerSecond = 45000;

        /// <summary><paramref name="bdmvRoot"/>（BDMV フォルダ）配下を全走査して構造を組み立てる。</summary>
        public static BdStructure Read(string bdmvRoot)
        {
            var result = new BdStructure();
            string user = Environment.UserName;
            string? discRoot = Path.GetDirectoryName(bdmvRoot);
            var disc = result.Disc;
            disc.CreatedBy = disc.UpdatedBy = user;

            // プレイリスト（PlayItem・マーク・サブパス・チャプター）
            string playlistDir = Path.Combine(bdmvRoot, "PLAYLIST");
            if (Directory.Exists(playlistDir))
            {
                foreach (var mpls in Directory.EnumerateFiles(playlistDir, "*.mpls").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryReadMpls(mpls, out var playlist, out var items, out var marks, out var subPaths)) continue;
                    playlist.CreatedBy = user;
                    result.Playlists.Add(playlist);
                    result.PlayItems.AddRange(items);
                    result.Marks.AddRange(marks);
                    result.SubPaths.AddRange(subPaths);
                    result.Chapters.AddRange(BuildChapters(playlist, items, marks));
                }
            }

            // クリップ
            string clipDir = Path.Combine(bdmvRoot, "CLIPINF");
            string streamDir = Path.Combine(bdmvRoot, "STREAM");
            if (Directory.Exists(clipDir))
            {
                foreach (var clpi in Directory.EnumerateFiles(clipDir, "*.clpi").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryReadClpi(clpi, out var clip, out var streams)) continue;
                    clip.FileSizeBytes = FileSizeOf(Path.Combine(streamDir, clip.ClipFile));
                    result.Clips.Add(clip);
                    result.ClipStreams.AddRange(streams);
                }
            }
            // PlayItem が参照するのに CLIPINF の無いクリップは、属性が空の行として立てる（外部キーのため）。
            var known = new HashSet<string>(result.Clips.Select(c => c.ClipFile), StringComparer.OrdinalIgnoreCase);
            foreach (var clipFile in result.PlayItems.Select(i => i.ClipFile).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (known.Contains(clipFile)) continue;
                result.Clips.Add(new BdClip { ClipFile = clipFile, FileSizeBytes = FileSizeOf(Path.Combine(streamDir, clipFile)) });
            }

            // ムービーオブジェクト
            var mobjPlaylist = new Dictionary<ushort, string?>();
            if (TryReadMovieObjects(Path.Combine(bdmvRoot, "MovieObject.bdmv"), out var mobjs, out var commands))
            {
                foreach (var m in mobjs) { m.CreatedBy = user; result.MovieObjects.Add(m); mobjPlaylist[m.MobjNo] = m.PlaylistFile; }
                result.MovieObjectCommands.AddRange(commands);
            }

            // 盤全体の情報とタイトル
            if (TryReadIndex(Path.Combine(bdmvRoot, "index.bdmv"), disc, out var titles))
            {
                foreach (var t in titles)
                {
                    if (t.MobjNo is ushort mn && mobjPlaylist.TryGetValue(mn, out var pl)) t.PlaylistFile = pl;
                    t.CreatedBy = user;
                    result.Titles.Add(t);
                }
            }
            ReadBdmt(Path.Combine(bdmvRoot, "META", "DL"), disc);
            disc.SoundEffectCount = ReadSoundEffectCount(Path.Combine(bdmvRoot, "AUXDATA", "sound.bdmv"));
            disc.PlaylistCount = (ushort)Math.Min(result.Playlists.Count, ushort.MaxValue);
            disc.ClipCount = (ushort)Math.Min(result.Clips.Count, ushort.MaxValue);
            disc.M2tsTotalBytes = Directory.Exists(streamDir)
                ? (ulong)Directory.EnumerateFiles(streamDir, "*.m2ts").Sum(f => (decimal)new FileInfo(f).Length)
                : null;
            disc.BdjoCount = CountFiles(Path.Combine(bdmvRoot, "BDJO"), "*.bdjo");
            disc.JarCount = CountFiles(Path.Combine(bdmvRoot, "JAR"), "*.jar");
            disc.HasBdj = (disc.BdjoCount ?? 0) > 0;
            disc.HasAacs = discRoot is not null && Directory.Exists(Path.Combine(discRoot, "AACS"));
            disc.VolumeLabel = VolumeLabelOf(bdmvRoot);
            disc.LastReadAt = DateTime.Now;

            // 鍵：id.bdmv のディスク ID。無ければ管理ファイルのハッシュ。
            if (discRoot is null || !ReadDiscId(Path.Combine(discRoot, "CERTIFICATE", "id.bdmv"), disc))
            {
                disc.DiscId = HashDiscId(bdmvRoot);
                disc.DiscIdSource = "HASH";
            }
            return result;
        }

        /// <summary>
        /// Entry マーク（種別 1）でプレイリストを区切ったチャプター。マークの時刻（PlayItem のクリップ内の時刻）を
        /// PlayItem の開始位置（プレイリスト時間軸）に足して並べ、次のマークまで（最後はプレイリストの終わりまで）を尺にする。
        /// マークが無ければプレイリスト全体を 1 チャプターにする。尺は生の値で、1 秒引くなどの加工はしない。
        /// </summary>
        private static IEnumerable<BdChapter> BuildChapters(BdPlaylist playlist, List<BdPlayItem> items, List<BdPlaylistMark> marks)
        {
            var starts = new List<ulong>();
            foreach (var m in marks.Where(m => m.MarkType == 1).OrderBy(m => m.PlayItemRef).ThenBy(m => m.TimeMs))
            {
                if (m.PlayItemRef >= items.Count) continue;
                var pi = items[m.PlayItemRef];
                ulong rel = m.TimeMs >= pi.InTimeMs ? m.TimeMs - pi.InTimeMs : 0;
                starts.Add(pi.PlaylistOffsetMs + rel);
            }
            starts = starts.Distinct().OrderBy(s => s).ToList();
            if (starts.Count == 0 || starts[0] != 0) starts.Insert(0, 0);
            for (int i = 0; i < starts.Count; i++)
            {
                ulong end = i + 1 < starts.Count ? starts[i + 1] : playlist.DurationMs;
                if (end < starts[i]) end = starts[i];
                yield return new BdChapter
                {
                    PlaylistFile = playlist.PlaylistFile, ChapterNo = (ushort)(i + 1),
                    StartTimeMs = starts[i], DurationMs = end - starts[i],
                };
            }
        }

        // ---- MPLS ----

        /// <summary>
        /// MPLS を読む。先頭：識別子 "MPLS"(4) → 版(4) → PlayList・PlayListMark・ExtensionData の開始位置(各 4) → 予約(20)
        /// → AppInfo（長さ(4) → 予約(1) → 再生種別(1) → 再生回数(2) → 操作禁止マスク(8) → フラグ(2)）。
        /// PlayItem：長さ(2) → クリップ名(5) → 形式識別子(4) → 予約と multi-angle・connection_condition(2) → STC 番号(1) → in(4) → out(4) → …。
        /// SubPath：長さ(4) → 予約(1) → 種別(1) → 予約と is_repeat(2) → 予約(1) → サブ PlayItem 数(1) → サブ PlayItem（長さ(2) → クリップ名(5) → …）。
        /// PlayListMark：予約(1) → 種別(1) → PlayItem 番号(2) → 時刻(4) → ES の PID(2) → 尺(4)。
        /// </summary>
        private static bool TryReadMpls(string path, out BdPlaylist playlist, out List<BdPlayItem> items,
            out List<BdPlaylistMark> marks, out List<BdSubPath> subPaths)
        {
            playlist = new BdPlaylist { PlaylistFile = Path.GetFileName(path) };
            items = new(); marks = new(); subPaths = new();
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("MPLS", StringComparison.Ordinal)) return false;
                uint playListStart = ReadU32BE(br);
                uint playMarkStart = ReadU32BE(br);

                fs.Position = 40;
                ReadU32BE(br);
                br.ReadByte();
                playlist.PlaybackType = br.ReadByte();
                ReadU16BE(br);
                playlist.UoMask = ReadU64BE(br);

                fs.Position = playListStart;
                uint playListLength = ReadU32BE(br);
                br.ReadBytes(2);
                ushort playItemCount = ReadU16BE(br);
                ushort subPathCount = ReadU16BE(br);
                long bodyEnd = playListStart + 4 + playListLength;

                ulong offsetMs = 0;
                for (int i = 0; i < playItemCount; i++)
                {
                    if (fs.Position + 2 > bodyEnd) break;
                    long itemStart = fs.Position;
                    ushort len = ReadU16BE(br);
                    long itemEnd = itemStart + 2 + len;
                    if (itemEnd > fs.Length) break;

                    string clipName = Encoding.ASCII.GetString(br.ReadBytes(5));
                    string codecId = Encoding.ASCII.GetString(br.ReadBytes(4)).TrimEnd('\0', ' ');
                    br.ReadByte();
                    byte flags = br.ReadByte();
                    byte stcId = br.ReadByte();
                    uint inTicks = ReadU32BE(br);
                    uint outTicks = ReadU32BE(br);

                    ulong inMs = TicksToMs(inTicks), outMs = TicksToMs(outTicks);
                    items.Add(new BdPlayItem
                    {
                        PlaylistFile = playlist.PlaylistFile, ItemSeq = (ushort)(i + 1),
                        ClipFile = clipName + ".m2ts", CodecId = string.IsNullOrEmpty(codecId) ? "M2TS" : codecId,
                        InTimeMs = inMs, OutTimeMs = outMs, PlaylistOffsetMs = offsetMs,
                        ConnectionCondition = (byte)(flags & 0x0F), StcId = stcId,
                    });
                    if (outMs > inMs) offsetMs += outMs - inMs;
                    fs.Position = itemEnd;
                }

                for (int s = 0; s < subPathCount; s++)
                {
                    if (fs.Position + 4 > bodyEnd) break;
                    long spStart = fs.Position;
                    uint spLen = ReadU32BE(br);
                    long spEnd = spStart + 4 + spLen;
                    br.ReadByte();
                    byte spType = br.ReadByte();
                    ushort repeatBits = ReadU16BE(br);
                    br.ReadByte();
                    byte spiCount = br.ReadByte();
                    string? firstClip = null;
                    if (spiCount > 0 && fs.Position + 7 <= fs.Length)
                    {
                        ReadU16BE(br);
                        firstClip = Encoding.ASCII.GetString(br.ReadBytes(5)) + ".m2ts";
                    }
                    subPaths.Add(new BdSubPath
                    {
                        PlaylistFile = playlist.PlaylistFile, SubPathSeq = (ushort)(s + 1), SubPathType = spType,
                        IsRepeat = (repeatBits & 0x01) != 0, SubPlayItemCount = spiCount, FirstClipFile = firstClip,
                    });
                    fs.Position = Math.Min(spEnd, fs.Length);
                }

                ushort markCount = 0;
                if (playMarkStart > 0 && playMarkStart + 6 <= fs.Length)
                {
                    fs.Position = playMarkStart;
                    uint markLen = ReadU32BE(br);
                    markCount = ReadU16BE(br);
                    long markEnd = playMarkStart + 4 + markLen;
                    for (int m = 0; m < markCount; m++)
                    {
                        if (fs.Position + 14 > markEnd || fs.Position + 14 > fs.Length) break;
                        br.ReadByte();
                        byte type = br.ReadByte();
                        ushort piRef = ReadU16BE(br);
                        uint stamp = ReadU32BE(br);
                        ushort esPid = ReadU16BE(br);
                        uint dur = ReadU32BE(br);
                        marks.Add(new BdPlaylistMark
                        {
                            PlaylistFile = playlist.PlaylistFile, MarkSeq = (ushort)(m + 1), MarkType = type, PlayItemRef = piRef,
                            TimeMs = TicksToMs(stamp), EntryEsPid = esPid, DurationMs = TicksToMs(dur),
                        });
                    }
                }

                playlist.DurationMs = offsetMs;
                playlist.PlayItemCount = (ushort)items.Count;
                playlist.SubPathCount = subPathCount;
                playlist.MarkCount = markCount;
                return true;
            }
            catch { return false; }
        }

        // ---- CLPI ----

        /// <summary>
        /// CLPI を読む。先頭：識別子 "HDMV"(4) → 版(4) → SequenceInfo / ProgramInfo / CPI / ClipMark / ExtensionData の開始位置(各 4) → 予約(12)
        /// → ClipInfo（長さ(4) → 予約(2) → ストリーム種別(1) → 用途種別(1) → 予約(4) → 記録レート(4) → ソースパケット数(4)）。
        /// SequenceInfo：ATC シーケンスごとに STC シーケンス（PCR PID(2) → 開始 SPN(4) → 提示開始(4) → 提示終了(4)）。
        /// ProgramInfo：プログラムごとにストリーム（PID(2) → 符号化情報（長さ(1) → 符号化種別(1) → 種別ごとの属性））。
        /// </summary>
        private static bool TryReadClpi(string path, out BdClip clip, out List<BdClipStream> streams)
        {
            string clipFile = Path.GetFileNameWithoutExtension(path) + ".m2ts";
            clip = new BdClip { ClipFile = clipFile };
            streams = new();
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("HDMV", StringComparison.Ordinal)) return false;
                uint seqStart = ReadU32BE(br);
                uint progStart = ReadU32BE(br);
                ReadU32BE(br); ReadU32BE(br); ReadU32BE(br);
                br.ReadBytes(12);

                ReadU32BE(br);
                br.ReadBytes(2);
                clip.ClipStreamType = br.ReadByte();
                clip.ApplicationType = br.ReadByte();
                br.ReadBytes(4);
                clip.TsRecordingRate = ReadU32BE(br);
                clip.SourcePackets = ReadU32BE(br);

                if (seqStart > 0 && seqStart + 6 <= fs.Length)
                {
                    fs.Position = seqStart;
                    ReadU32BE(br);
                    br.ReadByte();
                    byte atcCount = br.ReadByte();
                    ulong? first = null, last = null;
                    for (int a = 0; a < atcCount; a++)
                    {
                        ReadU32BE(br);
                        byte stcCount = br.ReadByte();
                        br.ReadByte();
                        for (int s = 0; s < stcCount; s++)
                        {
                            ReadU16BE(br); ReadU32BE(br);
                            uint pStart = ReadU32BE(br);
                            uint pEnd = ReadU32BE(br);
                            first ??= TicksToMs(pStart);
                            last = TicksToMs(pEnd);
                        }
                    }
                    clip.PresentationStartMs = first;
                    clip.PresentationEndMs = last;
                }

                if (progStart > 0 && progStart + 6 <= fs.Length)
                {
                    fs.Position = progStart;
                    ReadU32BE(br);
                    br.ReadByte();
                    byte programCount = br.ReadByte();
                    var seenPids = new HashSet<ushort>();
                    for (int p = 0; p < programCount; p++)
                    {
                        ReadU32BE(br); ReadU16BE(br);
                        byte streamCount = br.ReadByte();
                        br.ReadByte();
                        for (int s = 0; s < streamCount; s++)
                        {
                            ushort pid = ReadU16BE(br);
                            byte infoLen = br.ReadByte();
                            long infoEnd = fs.Position + infoLen;
                            byte codingType = br.ReadByte();
                            var row = new BdClipStream { ClipFile = clipFile, StreamPid = pid, CodingType = codingType, StreamKind = KindOf(codingType) };
                            switch (row.StreamKind)
                            {
                                case "VIDEO": { byte b = br.ReadByte(); row.VideoFormat = (byte)(b >> 4); row.FrameRate = (byte)(b & 0x0F); break; }
                                case "AUDIO": { byte b = br.ReadByte(); row.AudioPresentation = (byte)(b >> 4); row.SamplingRate = (byte)(b & 0x0F); row.Language = ReadLanguage(br); break; }
                                case "PG": case "IG": row.Language = ReadLanguage(br); break;
                                case "TEXT": br.ReadByte(); row.Language = ReadLanguage(br); break;
                            }
                            if (seenPids.Add(pid)) streams.Add(row);
                            fs.Position = infoEnd;
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        // ---- index.bdmv ----

        /// <summary>
        /// index.bdmv を読む。先頭：識別子 "INDX"(4) → 版(4) → Indexes の開始位置(4) → ExtensionData の開始位置(4) → 予約(24)
        /// → AppInfo（長さ(4) → フラグ(1) → 映像形式(4 ビット)＋フレームレート(4 ビット) → ユーザーデータ(32)）。
        /// Indexes：長さ(4) → 最初の再生(12) → トップメニュー(12) → タイトル数(2) → タイトル(各 12)。
        /// 各オブジェクトは 4 バイトの頭（種別 2 ビット。タイトルはさらにアクセス種別 2 ビット）＋ 8 バイトの本体
        /// （HDMV：再生種別 2 ビット → 予約 14 ビット → ムービーオブジェクト番号(2) → 予約(4)。BD-J：再生種別 2 ビット → 予約 14 ビット → BDJO 名(5) → 予約(1)）。
        /// </summary>
        private static bool TryReadIndex(string path, BdDisc disc, out List<BdTitle> titles)
        {
            titles = new();
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("INDX", StringComparison.Ordinal)) return false;
                uint indexesStart = ReadU32BE(br);

                fs.Position = 40;
                ReadU32BE(br);
                br.ReadByte();
                byte vf = br.ReadByte();
                disc.IndexVideoFormat = (byte)(vf >> 4);
                disc.IndexFrameRate = (byte)(vf & 0x0F);

                fs.Position = indexesStart;
                ReadU32BE(br);
                var first = ReadIndexObject(br, 0, isTitle: false);
                var top = ReadIndexObject(br, 65535, isTitle: false);
                disc.FirstPlaybackKind = first?.ObjectKind ?? "NONE";
                disc.TopMenuKind = top?.ObjectKind ?? "NONE";
                if (first is not null) titles.Add(first);
                if (top is not null) titles.Add(top);
                ushort count = ReadU16BE(br);
                disc.TitleCount = count;
                for (int i = 0; i < count; i++)
                {
                    var t = ReadIndexObject(br, (ushort)(i + 1), isTitle: true);
                    if (t is not null) titles.Add(t);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>index.bdmv のオブジェクト 1 つ（12 バイト）を読む。種別が HDMV でも BD-J でもない（無い）ときは null。</summary>
        private static BdTitle? ReadIndexObject(BinaryReader br, ushort titleNo, bool isTitle)
        {
            byte head = br.ReadByte();
            br.ReadBytes(3);
            int objectType = head >> 6;
            byte? accessType = isTitle ? (byte)((head >> 4) & 0x03) : null;
            byte pb = br.ReadByte();
            br.ReadByte();
            var body = br.ReadBytes(6);
            var t = new BdTitle { TitleNo = titleNo, AccessType = accessType, PlaybackType = (byte)(pb >> 6) };
            switch (objectType)
            {
                case 1:
                    t.ObjectKind = "HDMV";
                    t.MobjNo = (ushort)((body[0] << 8) | body[1]);
                    return t;
                case 2:
                    t.ObjectKind = "BDJ";
                    t.BdjoFile = Encoding.ASCII.GetString(body, 0, 5) + ".bdjo";
                    return t;
                default:
                    return null;
            }
        }

        // ---- MovieObject.bdmv ----

        /// <summary>
        /// MovieObject.bdmv を読む。先頭：識別子 "MOBJ"(4) → 版(4) → ExtensionData の開始位置(4) → 予約(28)
        /// → 長さ(4) → 予約(4) → オブジェクト数(2) → オブジェクトごとに（フラグ(2)：再開の意図・メニュー呼び出し禁止・タイトル検索禁止 → 命令数(2) → 命令（各 12 バイト：命令(4) → 第 1 オペランド(4) → 第 2 オペランド(4)））。
        /// 命令の 1 バイト目は上位から「オペランド数(3) → グループ(2：0=分岐) → 副グループ(3：2=再生)」、2 バイト目は上位から
        /// 「第 1 オペランドが即値(1) → 第 2 オペランドが即値(1) → 予約(2) → 分岐種別(4：0=PlayPL、1=PlayPLatPlayItem、2=PlayPLatMark)」
        /// （実盤の JumpTitle = 21810000・Goto = 20810000・PlayPL = 22800000 で確かめた）。この形の最初の命令の第 1 オペランドを、最初に再生するプレイリストとする。
        /// </summary>
        private static bool TryReadMovieObjects(string path, out List<BdMovieObject> mobjs, out List<BdMovieObjectCommand> commands)
        {
            mobjs = new(); commands = new();
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("MOBJ", StringComparison.Ordinal)) return false;
                fs.Position = 40;
                ReadU32BE(br);
                ReadU32BE(br);
                ushort count = ReadU16BE(br);
                for (int i = 0; i < count; i++)
                {
                    ushort flags = ReadU16BE(br);
                    ushort cmdCount = ReadU16BE(br);
                    var mobj = new BdMovieObject
                    {
                        MobjNo = (ushort)i,
                        ResumeIntention = (flags & 0x8000) != 0,
                        MenuCallMask = (flags & 0x4000) != 0,
                        TitleSearchMask = (flags & 0x2000) != 0,
                        CommandCount = cmdCount,
                    };
                    for (int c = 0; c < cmdCount; c++)
                    {
                        var op = br.ReadBytes(4);
                        uint dst = ReadU32BE(br);
                        uint src = ReadU32BE(br);
                        if (op.Length < 4) break;
                        commands.Add(new BdMovieObjectCommand
                        {
                            MobjNo = (ushort)i, CmdSeq = (ushort)(c + 1),
                            OpcodeHex = Convert.ToHexString(op), DstOperand = dst, SrcOperand = src,
                        });
                        bool isPlayPl = ((op[0] >> 3) & 0x03) == 0 && (op[0] & 0x07) == 2 && (op[1] & 0x0F) <= 2;
                        if (isPlayPl && mobj.PlaylistFile is null) mobj.PlaylistFile = $"{dst:00000}.mpls";
                    }
                    mobjs.Add(mobj);
                }
                return true;
            }
            catch { return false; }
        }

        // ---- 識別子・META・AUXDATA・フォルダ ----

        /// <summary>CERTIFICATE/id.bdmv（識別子 "BDID"）の組織 ID（位置 40 の 4 バイト）とディスク ID（続く 16 バイト）を 16 進で読む。読めなければ false。</summary>
        private static bool ReadDiscId(string path, BdDisc disc)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(4)).StartsWith("BDID", StringComparison.Ordinal)) return false;
                fs.Position = 40;
                var org = br.ReadBytes(4);
                var id = br.ReadBytes(16);
                if (org.Length < 4 || id.Length < 16) return false;
                // ディスク ID が 0 で埋まっている盤がある。そのままでは別の盤と鍵が重なるので、ハッシュに切り替える（組織 ID は残す）。
                if (id.All(x => x == 0))
                {
                    disc.OrgId = Convert.ToHexString(org);
                    return false;
                }
                disc.OrgId = Convert.ToHexString(org);
                disc.DiscId = Convert.ToHexString(id);
                disc.DiscIdSource = "ID_BDMV";
                return true;
            }
            catch { return false; }
        }

        /// <summary>id.bdmv の無い盤の鍵：index.bdmv・MovieObject.bdmv・全 .mpls（名前順）の中身の SHA-256 の先頭 16 バイト（16 進）。</summary>
        private static string HashDiscId(string bdmvRoot)
        {
            using var sha = SHA256.Create();
            using var ms = new MemoryStream();
            foreach (var f in new[] { Path.Combine(bdmvRoot, "index.bdmv"), Path.Combine(bdmvRoot, "MovieObject.bdmv") })
                if (File.Exists(f)) ms.Write(File.ReadAllBytes(f));
            string playlistDir = Path.Combine(bdmvRoot, "PLAYLIST");
            if (Directory.Exists(playlistDir))
                foreach (var f in Directory.EnumerateFiles(playlistDir, "*.mpls").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    ms.Write(File.ReadAllBytes(f));
            return Convert.ToHexString(sha.ComputeHash(ms.ToArray()), 0, 16);
        }

        /// <summary>META/DL/bdmt_*.xml からディスクの表示名と言語、サムネイルの数を読む（日本語を優先し、無ければ最初の 1 つ）。</summary>
        private static void ReadBdmt(string metaDir, BdDisc disc)
        {
            try
            {
                if (!Directory.Exists(metaDir)) return;
                var files = Directory.EnumerateFiles(metaDir, "bdmt_*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                var pick = files.FirstOrDefault(f => Path.GetFileName(f).Equals("bdmt_jpn.xml", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
                if (pick is null) return;
                var doc = XDocument.Load(pick);
                string? name = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim();
                disc.BdmtName = string.IsNullOrEmpty(name) ? null : name;
                string stem = Path.GetFileNameWithoutExtension(pick);
                disc.BdmtLanguage = stem.Length > 5 ? stem.Substring(5) : null;
                disc.BdmtThumbnailCount = (byte)Math.Min(doc.Descendants().Count(e => e.Name.LocalName == "thumbnail"), byte.MaxValue);
            }
            catch { /* 読めなければ空のまま */ }
        }

        /// <summary>AUXDATA/sound.bdmv（識別子 "BCLV"）の効果音の数（位置 40 の長さ(4) → 予約(1) → 数(1)）。無ければ null。</summary>
        private static ushort? ReadSoundEffectCount(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                if (!Encoding.ASCII.GetString(br.ReadBytes(4)).StartsWith("BCLV", StringComparison.Ordinal)) return null;
                fs.Position = 40;
                ReadU32BE(br);
                br.ReadByte();
                return br.ReadByte();
            }
            catch { return null; }
        }

        private static string? VolumeLabelOf(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return null;
                var di = new DriveInfo(root);
                return di.IsReady && !string.IsNullOrEmpty(di.VolumeLabel) ? di.VolumeLabel : null;
            }
            catch { return null; }
        }

        private static ushort? CountFiles(string dir, string pattern)
            => Directory.Exists(dir) ? (ushort)Math.Min(Directory.EnumerateFiles(dir, pattern).Count(), ushort.MaxValue) : null;

        private static ulong? FileSizeOf(string path)
        {
            try { return File.Exists(path) ? (ulong)new FileInfo(path).Length : null; }
            catch { return null; }
        }

        private static string KindOf(byte codingType) => codingType switch
        {
            0x01 or 0x02 or 0x1B or 0x20 or 0x24 or 0xEA => "VIDEO",
            0x03 or 0x04 or 0x80 or 0x81 or 0x82 or 0x83 or 0x84 or 0x85 or 0x86 or 0xA1 or 0xA2 => "AUDIO",
            0x90 => "PG",
            0x91 => "IG",
            0x92 => "TEXT",
            _ => "OTHER"
        };

        private static string? ReadLanguage(BinaryReader br)
        {
            string s = Encoding.ASCII.GetString(br.ReadBytes(3)).Trim('\0', ' ');
            return s.Length == 0 ? null : s;
        }

        private static ulong TicksToMs(uint ticks) => (ulong)Math.Round(ticks * 1000.0 / TicksPerSecond);

        private static ushort ReadU16BE(BinaryReader br)
        {
            var b = br.ReadBytes(2);
            if (b.Length < 2) throw new EndOfStreamException();
            return (ushort)((b[0] << 8) | b[1]);
        }

        private static uint ReadU32BE(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        private static ulong ReadU64BE(BinaryReader br)
        {
            var b = br.ReadBytes(8);
            if (b.Length < 8) throw new EndOfStreamException();
            ulong v = 0;
            foreach (var x in b) v = (v << 8) | x;
            return v;
        }
    }
}
