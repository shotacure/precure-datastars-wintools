namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_recording_bgm_assignments テーブルに対応するエンティティ
/// （複合 PK: song_recording_id + song_part_variant_code + bgm_series_id + bgm_m_no_detail）。
/// <para>
/// 1 つの <c>song_recordings</c>（特定のアレンジ・テイク・歌唱者構成での録音）が、
/// 歌として収録された上で「劇伴としても扱う」二重性質を持つ場合に、
/// 当該録音と劇伴 cue（<c>bgm_cues</c>）の N:M 関係を表現する。
/// </para>
/// <para>
/// tracks 本体の <c>content_kind_code</c> は 'SONG' / 'BGM' を排他選択する仕様
/// （<c>trg_tracks_bi/bu_fk_consistency</c> で強制）のまま据置。
/// この中間テーブルは SONG なのに BGM 性も併せ持つ追加の関係だけを表現する。
/// </para>
/// <para>
/// <see cref="SongPartVariantCode"/> はパート違いで紐付く M ナンバーが変わるケース
/// （VOCAL 版と INST 版で別 M ナンバー、など）に対応するための副キー列。
/// NOT NULL 必須で、実パート（'VOCAL' / 'INST' / 'KARAOKE' 等）または sentinel '_ANY'
/// （パート区別なく適用したい場合）を入れる。tracks 側 song_part_variant_code が NULL の
/// トラック（パート未登録）は中間テーブルとマッチしない。
/// </para>
/// <para>
/// 表示側：
///   - 劇伴詳細 <c>/bgms/{slug}/</c> の cue カード収録盤リストに SONG トラックも含まれる
///   - 商品詳細 <c>/products/{catalog}/</c> の SONG トラックカードで歌のクレジット行下に
///     「シリーズ略記 + Mナンバー [メニュー]」が追加される
///   - トラックカードの円バッジが SONG 赤 + BGM 緑の斜め分割塗りに変わる
/// </para>
/// </summary>
public sealed class SongRecordingBgmAssignment
{
    /// <summary>参照先録音 ID（→ song_recordings）。複合 PK の第 1 列。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>
    /// 適用パートコード（→ song_part_variants.variant_code）。NOT NULL。
    /// 実パートコード（'VOCAL' / 'INST' / 'KARAOKE' 等）または sentinel '_ANY'（パート区別なく適用）を入れる。
    /// 複合 PK の第 2 列。
    /// </summary>
    public string SongPartVariantCode { get; set; } = "";

    /// <summary>参照先 cue のシリーズ ID（→ bgm_cues.series_id）。複合 PK の第 3 列。</summary>
    public int BgmSeriesId { get; set; }

    /// <summary>参照先 cue の M 番号詳細表記（→ bgm_cues.m_no_detail）。複合 PK の第 4 列。</summary>
    public string BgmMNoDetail { get; set; } = "";

    /// <summary>レコード作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>レコード更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成者識別子（任意の文字列。NULL 可）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新者識別子（任意の文字列。NULL 可）。</summary>
    public string? UpdatedBy { get; set; }
}
