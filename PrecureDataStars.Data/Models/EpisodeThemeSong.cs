namespace PrecureDataStars.Data.Models;

/// <summary>
/// episode_theme_songs テーブルに対応するエンティティモデル（PK: episode_id, theme_kind, insert_seq）。
/// <para>
/// 各エピソードに紐づく OP 主題歌（最大 1）／ED 主題歌（最大 1）／挿入歌（複数可）の
/// レコード。クレジットの THEME_SONG ロールエントリは、このテーブルから歌情報を引いて
/// レンダリングする想定。
/// </para>
/// <para>
/// <see cref="ThemeKind"/> = "OP"/"ED" のときは <see cref="InsertSeq"/> = 0 の 1 行のみ、
/// <see cref="ThemeKind"/> = "INSERT" のときは <see cref="InsertSeq"/> = 1, 2, ... と複数行が立つ。
/// この排他は DB 側 CHECK 制約 <c>ck_ets_op_ed_no_insert_seq</c> でも担保。
/// </para>
/// </summary>
public sealed class EpisodeThemeSong
{
    /// <summary>エピソード ID（PK 構成、→ episodes.episode_id）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>主題歌区分（PK 構成、"OP"/"ED"/"INSERT"）。</summary>
    public string ThemeKind { get; set; } = "OP";

    /// <summary>挿入歌内の通番（PK 構成、OP/ED は 0 固定、INSERT は 1, 2, ...）。</summary>
    public byte InsertSeq { get; set; }

    /// <summary>歌録音 ID（必須、→ song_recordings.song_recording_id）。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>レーベル（販売／制作）の企業名義 ID（任意、→ company_aliases.alias_id）。</summary>
    public int? LabelCompanyAliasId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
