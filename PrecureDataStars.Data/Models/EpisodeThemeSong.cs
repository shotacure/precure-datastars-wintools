namespace PrecureDataStars.Data.Models;

/// <summary>
/// episode_theme_songs テーブルに対応するエンティティモデル
/// （PK: episode_id, is_broadcast_only, theme_kind, insert_seq）。
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
/// <para>
/// v1.2.0 工程 B' で <see cref="IsBroadcastOnly"/> を導入。
/// 本放送・Blu-ray・配信は基本的に同じ主題歌を共有するため、ほとんどの行は
/// is_broadcast_only=0（全媒体共通）で済む。OP もしくは ED のみ本放送だけ
/// 例外的に異なる場合に限り、is_broadcast_only=1 の追加行を別途立てて表現する。
/// </para>
/// </summary>
public sealed class EpisodeThemeSong
{
    /// <summary>エピソード ID（PK 構成、→ episodes.episode_id）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>
    /// 本放送限定フラグ（PK 構成、v1.2.0 工程 B' 追加）。
    /// false (0) = 本放送・Blu-ray・配信ともに共通（既定）。
    /// true (1) = 本放送限定の例外行（同 episode/theme_kind の 0 行と並立する）。
    /// </summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>主題歌区分（PK 構成、"OP"/"ED"/"INSERT"）。</summary>
    public string ThemeKind { get; set; } = "OP";

    /// <summary>挿入歌内の通番（PK 構成、OP/ED は 0 固定、INSERT は 1, 2, ...）。</summary>
    public byte InsertSeq { get; set; }

    /// <summary>歌録音 ID（必須、→ song_recordings.song_recording_id）。</summary>
    public int SongRecordingId { get; set; }

    // LabelCompanyAliasId は v1.2.0 工程 H 補修で撤去（episode_theme_songs は楽曲の事実だけを
    // 保持する設計に整理。レーベル名は credit_block_entries の COMPANY エントリで持つ運用）。

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
