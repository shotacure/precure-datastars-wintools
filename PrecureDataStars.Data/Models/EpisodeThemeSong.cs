namespace PrecureDataStars.Data.Models;

/// <summary>
/// episode_theme_songs テーブルに対応するエンティティモデル
/// （PK: episode_id, is_broadcast_only, theme_kind, seq）。
/// <para>
/// 各エピソードに紐づく OP 主題歌・ED 主題歌・挿入歌のレコード。
/// クレジットの THEME_SONG ロールエントリは、このテーブルから歌情報を引いて
/// レンダリングする想定。
/// </para>
/// <para>
/// <see cref="Seq"/> はエピソード内での「劇中で流れる順序」を表す通番（1, 2, 3, ...）。
/// OP/ED と INSERT を区別せず、OP が冒頭にあるとは限らない作品にも対応する。
/// 数値そのものに OP=1 / ED=末尾 のような決まりは無い。
/// </para>
/// <para>
/// <see cref="IsBroadcastOnly"/> は本放送限定の例外行を表すフラグ。
/// 本放送・Blu-ray・配信は基本的に同じ主題歌を共有するため、ほとんどの行は
/// is_broadcast_only=0（全媒体共通）で済む。OP もしくは ED のみ本放送だけ
/// 例外的に異なる場合に限り、is_broadcast_only=1 の追加行を別途立てて表現する。
/// </para>
/// <para>
/// 「クレジットされていないが実際には流れた」「クレジットされているが実際には流れていない」
/// という乖離を表現する 3 値の使用実態フラグ。<see cref="IsBroadcastOnly"/>
/// （TV 放送版限定の主題歌差し替え）とは別軸の概念で、両者は組み合わせ可能。
/// </para>
/// </summary>
public sealed class EpisodeThemeSong
{
    /// <summary>エピソード ID（PK 構成、→ episodes.episode_id）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>
    /// 本放送限定フラグ（PK 構成）。
    /// false (0) = 本放送・Blu-ray・配信ともに共通（既定）。
    /// true (1) = 本放送限定の例外行（同 episode/theme_kind の 0 行と並立する）。
    /// </summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>主題歌区分（PK 構成、"OP"/"ED"/"INSERT"）。</summary>
    public string ThemeKind { get; set; } = "OP";

    /// <summary>
    /// エピソード内での劇中順（PK 構成、1, 2, 3, ...）。
    /// <para>
    /// OP/ED と INSERT を区別せず「劇中で流れる順序」を表す。OP が冒頭にあるとは
    /// 限らない作品にも対応するため、数値そのものに OP=1 / ED=末尾 のような決まりは無い。
    /// 同一 (episode_id, is_broadcast_only) 内では PK の一部としてユニーク。
    /// </para>
    /// </summary>
    public byte Seq { get; set; }

    /// <summary>
    /// 使用実態フラグ。
    /// クレジットと実際の使用の乖離を表現する 3 値：
    /// <list type="bullet">
    ///   <item><c>NORMAL</c> — クレジット通り、実際に流れた（既定）</item>
    ///   <item><c>BROADCAST_NOT_CREDITED</c> — クレジットされていないが確かに流れた
    ///     （クレジットページには表示せず、エピソード主題歌・挿入歌セクションには表示）</item>
    ///   <item><c>CREDITED_NOT_BROADCAST</c> — クレジットされているが実際には流れていない
    ///     （クレジットページには「実際には不使用」注記付きで表示、エピソード主題歌・挿入歌セクションには表示しない）</item>
    /// </list>
    /// 文字列表現は DB の ENUM 値と一致させる。
    /// </summary>
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

/// <summary>
/// episode_theme_songs.usage_actuality の値を表す定数群。
/// </summary>
public static class EpisodeThemeSongUsageActualities
{
    /// <summary>クレジット通り、実際に流れた（既定）。</summary>
    public const string Normal = "NORMAL";
    /// <summary>クレジットされていないが確かに流れた。</summary>
    public const string BroadcastNotCredited = "BROADCAST_NOT_CREDITED";
    /// <summary>クレジットされているが実際には流れていない。</summary>
    public const string CreditedNotBroadcast = "CREDITED_NOT_BROADCAST";
}