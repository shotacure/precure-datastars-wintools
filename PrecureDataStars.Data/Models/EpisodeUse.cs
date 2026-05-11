namespace PrecureDataStars.Data.Models;

/// <summary>
/// episode_uses テーブルに対応するエンティティモデル
/// （複合 PK: episode_id + part_kind + use_order + sub_order、v1.3.0 新設）。
/// <para>
/// エピソードのパート内で流れた音声（歌・劇伴・ドラマ・ラジオ・ジングル・その他）の
/// 使用記録を表す。<c>tracks</c>（discs 配下）と同じ構造を、ディスクとトラックではなく
/// エピソードとパート内使用順に置き換えた流儀。
/// </para>
/// <para>
/// 主キー (<see cref="EpisodeId"/>, <see cref="PartKind"/>, <see cref="UseOrder"/>, <see cref="SubOrder"/>) で
/// 「どのエピソードのどのパートの何番目の音声か」を一意に識別する。
/// メドレー的に複数曲が連続するケースは、同じ <see cref="UseOrder"/> 配下に複数の
/// <see cref="SubOrder"/> 行を置いて表現する。
/// </para>
/// <para>
/// 内容種別（<see cref="ContentKindCode"/>）に応じて参照列を切り替える：
/// </para>
/// <list type="bullet">
///   <item><description>SONG: <see cref="SongRecordingId"/> + <see cref="SongSizeVariantCode"/> + <see cref="SongPartVariantCode"/>
///     を使う。<see cref="SongRecordingId"/> は必須（DB トリガで担保）。</description></item>
///   <item><description>BGM: (<see cref="BgmSeriesId"/>, <see cref="BgmMNoDetail"/>) で <c>bgm_cues</c> を参照。
///     両方 NOT NULL が必須（DB トリガで担保）。</description></item>
///   <item><description>DRAMA / RADIO / JINGLE / OTHER: <see cref="UseTitleOverride"/> にテキストを入れる。
///     SONG / BGM 用の参照列は使わない。</description></item>
/// </list>
/// <para>
/// FK 先（<c>song_recordings</c>, <c>bgm_cues</c>）が削除された場合は <c>ON DELETE SET NULL</c> で
/// 行自体は残る（履歴としての価値があるため）。エピソード本体が削除された場合は <c>ON DELETE CASCADE</c>。
/// </para>
/// </summary>
public sealed class EpisodeUse
{
    // ── 主キー ──

    /// <summary>対象エピソード ID（→ episodes.episode_id）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>パート種別コード（→ part_types.part_type）。AVANT / PART_A / PART_B / EYE_CATCH / NEXT_EP 等。</summary>
    public string PartKind { get; set; } = "";

    /// <summary>パート内の使用順（1 始まり）。</summary>
    public byte UseOrder { get; set; }

    /// <summary>
    /// 同 <see cref="UseOrder"/> 内のサブ順。通常は 0、メドレー等で複数曲が連続するときに 1, 2, 3, ... と振る。
    /// </summary>
    public byte SubOrder { get; set; } = 0;

    // ── 内容種別と多態参照 ──

    /// <summary>
    /// 内容種別コード（→ track_content_kinds）。
    /// "SONG" / "BGM" / "DRAMA" / "RADIO" / "JINGLE" / "OTHER"。
    /// （tracks 用の "CHAPTER" は episode_uses では用途外。）
    /// </summary>
    public string ContentKindCode { get; set; } = "OTHER";

    /// <summary>SONG 時に参照する録音 ID（→ song_recordings）。SONG 以外では NULL。</summary>
    public int? SongRecordingId { get; set; }

    /// <summary>
    /// SONG 時のサイズ種別コード（→ song_size_variants）。
    /// フルサイズ・TVサイズ・映画サイズ等。SONG 以外では NULL。
    /// </summary>
    public string? SongSizeVariantCode { get; set; }

    /// <summary>
    /// SONG 時のパート種別コード（→ song_part_variants）。
    /// 通常歌入り・オリジナル・カラオケ・コーラス入り等。SONG 以外では NULL。
    /// </summary>
    public string? SongPartVariantCode { get; set; }

    /// <summary>
    /// BGM 時に参照する <c>bgm_cues</c> の第 1 列（シリーズ ID）。
    /// BGM 以外では NULL。2 列セット（<see cref="BgmSeriesId"/>, <see cref="BgmMNoDetail"/>）は
    /// 同時に NOT NULL または同時に NULL の 2 状態のみ許容（DB トリガで検証）。
    /// </summary>
    public int? BgmSeriesId { get; set; }

    /// <summary>BGM 時に参照する <c>bgm_cues</c> の第 2 列（M 番号詳細）。BGM 以外では NULL。</summary>
    public string? BgmMNoDetail { get; set; }

    /// <summary>
    /// 内容種別がテキスト系（DRAMA / RADIO / JINGLE / OTHER）のときの表示文字列。
    /// SONG / BGM では用途外（参照列で実体を引くため）。
    /// </summary>
    public string? UseTitleOverride { get; set; }

    // ── 補助情報（任意） ──

    /// <summary>使用シーンの説明（例: "ほのかとなぎさの再会"）。任意。</summary>
    public string? SceneLabel { get; set; }

    /// <summary>使用尺（秒）。任意。</summary>
    public ushort? DurationSeconds { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    /// <summary>本放送のみ使用された場合のフラグ。</summary>
    public bool IsBroadcastOnly { get; set; }

    // ── 監査 ──

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザ。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザ。</summary>
    public string? UpdatedBy { get; set; }
}
