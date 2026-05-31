namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_theme_songs テーブルに対応するエンティティモデル
/// （PK: series_id, is_broadcast_only, theme_kind, seq）。
/// 映画系列（series_kinds.credit_attach_to='SERIES'）の主題歌・OP 主題歌・挿入歌を
/// シリーズ単位で持つ。<see cref="EpisodeThemeSong"/> のミラー構造で、
/// episode_id を <see cref="SeriesId"/> に置き換えただけ。
/// 役職テンプレ DSL の <c>{SONG_TITLE}</c> / <c>{LYRICIST}</c> 等の auto-expand は、
/// SERIES スコープのクレジットでこのテーブルから引き当てる。
/// </summary>
public sealed class SeriesThemeSong
{
    /// <summary>シリーズ ID（PK 構成、→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>本放送限定フラグ（PK 構成）。 false (0) = 本放送・Blu-ray・配信ともに共通（既定）。 true (1) = 本放送限定の例外行（同 series/theme_kind の 0 行と並立する）。</summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>主題歌区分（PK 構成、"OP"/"ED"/"INSERT"）。</summary>
    public string ThemeKind { get; set; } = "OP";

    /// <summary>シリーズ内での劇中順（PK 構成、1, 2, 3, ...）。 OP/ED と INSERT を区別せず「劇中で流れる順序」を表す。 数値そのものに OP=1 / ED=末尾 のような決まりは無い。</summary>
    public byte Seq { get; set; }

    /// <summary>使用実態フラグ（NORMAL / BROADCAST_NOT_CREDITED / CREDITED_NOT_BROADCAST）。</summary>
    public string UsageActuality { get; set; } = "NORMAL";

    /// <summary>歌録音 ID（必須、→ song_recordings.song_recording_id）。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
