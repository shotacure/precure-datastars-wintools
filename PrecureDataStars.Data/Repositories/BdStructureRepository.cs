using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// Blu-ray の bd_* テーブル群（<see cref="BdStructure"/>）のリポジトリ。鍵はディスク ID。
/// BDAnalyzer が読むたびに盤単位で「全削除 → 一括挿入」する（bd_discs を消すと子はすべて消える）。
/// 当てた作品・結びつけた品番・最初に読んだ日時は、呼び出し側が既存の行（<see cref="GetDiscAsync"/>）から引き継いで渡す。
/// </summary>
public sealed class BdStructureRepository : RepositoryBase
{
    public BdStructureRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>ディスク ID の盤の行。無ければ null。</summary>
    public async Task<BdDisc?> GetDiscAsync(string discId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT disc_id AS DiscId, disc_id_source AS DiscIdSource, org_id AS OrgId, volume_label AS VolumeLabel,
                   bdmt_name AS BdmtName, series_id AS SeriesId, catalog_no AS CatalogNo,
                   first_read_at AS FirstReadAt, last_read_at AS LastReadAt
              FROM bd_discs WHERE disc_id = @discId;
            """;
        var rows = await QueryListAsync<BdDisc>(sql, new { discId }, ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>ディスク ID の盤の、記録済みのチャプターの当て方（種別・話・パート）。再読み取りで引き継ぐ。</summary>
    public async Task<IReadOnlyList<BdChapter>> GetChaptersAsync(string discId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT disc_id AS DiscId, playlist_file AS PlaylistFile, chapter_no AS ChapterNo, start_time_ms AS StartTimeMs,
                   duration_ms AS DurationMs, chapter_kind AS ChapterKind, episode_id AS EpisodeId, episode_seq AS EpisodeSeq
              FROM bd_chapters WHERE disc_id = @discId ORDER BY playlist_file, chapter_no;
            """;
        return await QueryListAsync<BdChapter>(sql, new { discId }, ct).ConfigureAwait(false);
    }

    /// <summary>ディスク ID の盤の、記録済みのプレイリストの種別と話。再読み取りで引き継ぐ。</summary>
    public async Task<IReadOnlyList<BdPlaylist>> GetPlaylistsAsync(string discId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT disc_id AS DiscId, playlist_file AS PlaylistFile, duration_ms AS DurationMs, playlist_kind AS PlaylistKind, episode_id AS EpisodeId
              FROM bd_playlists WHERE disc_id = @discId ORDER BY playlist_file;
            """;
        return await QueryListAsync<BdPlaylist>(sql, new { discId }, ct).ConfigureAwait(false);
    }

    /// <summary>品番に結びつけた盤のディスク ID（無ければ null）。</summary>
    public async Task<string?> FindDiscIdByCatalogAsync(string catalogNo, CancellationToken ct = default)
    {
        var rows = await QueryListAsync<string>("SELECT disc_id FROM bd_discs WHERE catalog_no = @catalogNo LIMIT 1;", new { catalogNo }, ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>盤に品番を結びつける（bd_discs.catalog_no）。</summary>
    public async Task LinkCatalogAsync(string discId, string catalogNo, CancellationToken ct = default)
    {
        await ExecuteAsync("UPDATE bd_discs SET catalog_no = @catalogNo WHERE disc_id = @discId;", new { discId, catalogNo }, ct).ConfigureAwait(false);
    }

    /// <summary>盤の情報を全削除してから <paramref name="structure"/> で一括登録する（1 トランザクション）。</summary>
    public async Task ReplaceAllAsync(BdStructure structure, CancellationToken ct = default)
    {
        structure.PropagateDiscId();
        string discId = structure.Disc.DiscId;

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM bd_discs WHERE disc_id = @discId;", new { discId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            async Task InsertAsync<T>(IReadOnlyList<T> rows, string sql)
            {
                if (rows.Count == 0) return;
                await conn.ExecuteAsync(new CommandDefinition(sql, rows, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO bd_discs
                  (disc_id, disc_id_source, org_id, volume_label, bdmt_name, bdmt_language, bdmt_thumbnail_count,
                   index_video_format, index_frame_rate, first_playback_kind, top_menu_kind, title_count, playlist_count, clip_count,
                   m2ts_total_bytes, has_aacs, has_bdj, bdjo_count, jar_count, sound_effect_count, series_id, catalog_no,
                   first_read_at, last_read_at, created_by, updated_by)
                VALUES
                  (@DiscId, @DiscIdSource, @OrgId, @VolumeLabel, @BdmtName, @BdmtLanguage, @BdmtThumbnailCount,
                   @IndexVideoFormat, @IndexFrameRate, @FirstPlaybackKind, @TopMenuKind, @TitleCount, @PlaylistCount, @ClipCount,
                   @M2tsTotalBytes, @HasAacs, @HasBdj, @BdjoCount, @JarCount, @SoundEffectCount, @SeriesId, @CatalogNo,
                   @FirstReadAt, @LastReadAt, @CreatedBy, @UpdatedBy);
                """, structure.Disc, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            await InsertAsync(structure.Titles, """
                INSERT INTO bd_titles (disc_id, title_no, object_kind, access_type, playback_type, mobj_no, bdjo_file, playlist_file, created_by)
                VALUES (@DiscId, @TitleNo, @ObjectKind, @AccessType, @PlaybackType, @MobjNo, @BdjoFile, @PlaylistFile, @CreatedBy);
                """);
            await InsertAsync(structure.MovieObjects, """
                INSERT INTO bd_movie_objects (disc_id, mobj_no, resume_intention, menu_call_mask, title_search_mask, command_count, playlist_file, created_by)
                VALUES (@DiscId, @MobjNo, @ResumeIntention, @MenuCallMask, @TitleSearchMask, @CommandCount, @PlaylistFile, @CreatedBy);
                """);
            await InsertAsync(structure.MovieObjectCommands, """
                INSERT INTO bd_movie_object_commands (disc_id, mobj_no, cmd_seq, opcode_hex, dst_operand, src_operand)
                VALUES (@DiscId, @MobjNo, @CmdSeq, @OpcodeHex, @DstOperand, @SrcOperand);
                """);
            await InsertAsync(structure.Playlists, """
                INSERT INTO bd_playlists (disc_id, playlist_file, duration_ms, play_item_count, sub_path_count, mark_count, playback_type, uo_mask, playlist_kind, episode_id, created_by)
                VALUES (@DiscId, @PlaylistFile, @DurationMs, @PlayItemCount, @SubPathCount, @MarkCount, @PlaybackType, @UoMask, @PlaylistKind, @EpisodeId, @CreatedBy);
                """);
            await InsertAsync(structure.PlayItems, """
                INSERT INTO bd_play_items (disc_id, playlist_file, item_seq, clip_file, codec_id, in_time_ms, out_time_ms, playlist_offset_ms, connection_condition, stc_id)
                VALUES (@DiscId, @PlaylistFile, @ItemSeq, @ClipFile, @CodecId, @InTimeMs, @OutTimeMs, @PlaylistOffsetMs, @ConnectionCondition, @StcId);
                """);
            await InsertAsync(structure.Marks, """
                INSERT INTO bd_playlist_marks (disc_id, playlist_file, mark_seq, mark_type, play_item_ref, time_ms, entry_es_pid, duration_ms)
                VALUES (@DiscId, @PlaylistFile, @MarkSeq, @MarkType, @PlayItemRef, @TimeMs, @EntryEsPid, @DurationMs);
                """);
            await InsertAsync(structure.Chapters, """
                INSERT INTO bd_chapters (disc_id, playlist_file, chapter_no, start_time_ms, duration_ms, chapter_kind, episode_id, episode_seq)
                VALUES (@DiscId, @PlaylistFile, @ChapterNo, @StartTimeMs, @DurationMs, @ChapterKind, @EpisodeId, @EpisodeSeq);
                """);
            await InsertAsync(structure.SubPaths, """
                INSERT INTO bd_sub_paths (disc_id, playlist_file, sub_path_seq, sub_path_type, is_repeat, sub_play_item_count, first_clip_file)
                VALUES (@DiscId, @PlaylistFile, @SubPathSeq, @SubPathType, @IsRepeat, @SubPlayItemCount, @FirstClipFile);
                """);
            await InsertAsync(structure.Clips, """
                INSERT INTO bd_clips (disc_id, clip_file, presentation_start_ms, presentation_end_ms, ts_recording_rate, source_packets, application_type, clip_stream_type, file_size_bytes)
                VALUES (@DiscId, @ClipFile, @PresentationStartMs, @PresentationEndMs, @TsRecordingRate, @SourcePackets, @ApplicationType, @ClipStreamType, @FileSizeBytes);
                """);
            await InsertAsync(structure.ClipStreams, """
                INSERT INTO bd_clip_streams (disc_id, clip_file, stream_pid, stream_kind, coding_type, video_format, frame_rate, audio_presentation, sampling_rate, language)
                VALUES (@DiscId, @ClipFile, @StreamPid, @StreamKind, @CodingType, @VideoFormat, @FrameRate, @AudioPresentation, @SamplingRate, @Language);
                """);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
