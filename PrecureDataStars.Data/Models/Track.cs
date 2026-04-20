namespace PrecureDataStars.Data.Models;

/// <summary>
/// tracks テーブルに対応するエンティティモデル（複合 PK: catalog_no + track_no + sub_order）。
/// <para>
/// ディスクの物理トラック（CD のオーディオトラック、BD/DVD のチャプター）を表す。
/// トラックの内容種別（<see cref="ContentKindCode"/>）に応じて、
/// <see cref="SongRecordingId"/> または (<see cref="BgmSeriesId"/>, <see cref="BgmMNoDetail"/>) に紐付ける。
/// ドラマ／ラジオ／ジングル等はタイトルを <see cref="TrackTitleOverride"/> に持つだけで
/// 実体テーブルは参照しない。
/// </para>
/// <para>
/// 1 トラックに複数の曲が入っているケース（メドレー、ボーナストラックの複数曲構成、
/// BGM の前後半分割 等）は、同じ (<see cref="CatalogNo"/>, <see cref="TrackNo"/>) の下に
/// 複数の <see cref="SubOrder"/> 行を持たせて表現する。
/// 物理情報（<see cref="StartLba"/>, <see cref="LengthFrames"/>, <see cref="Isrc"/>,
/// <see cref="IsDataTrack"/>, <see cref="HasPreEmphasis"/>, <see cref="IsCopyPermitted"/>,
/// <see cref="CdTextTitle"/>, <see cref="CdTextPerformer"/>）は <see cref="SubOrder"/>=0 の
/// 親行にだけ持ち、<see cref="SubOrder"/>&gt;0 の子行では NULL/0 固定（DB トリガーで検証）。
/// 同じトラック内の全 sub_order 行の <see cref="ContentKindCode"/> は一致していなければならない
/// （SONG と BGM の混在は禁止、DB トリガーで検証）。
/// </para>
/// </summary>
public sealed class Track
{
    // ── 主キー ──

    /// <summary>所属ディスクの品番（→ discs.catalog_no）。</summary>
    public string CatalogNo { get; set; } = "";

    /// <summary>ディスク内のトラック番号（1 始まり）。</summary>
    public byte TrackNo { get; set; }

    /// <summary>
    /// トラック内順序。通常のトラックでは 0（既定値）。
    /// 1 トラックに複数の曲が入っている場合のみ 1, 2, 3, ... と振って複数行で表現する。
    /// </summary>
    public byte SubOrder { get; set; } = 0;

    // ── 内容種別と多態参照 ──

    /// <summary>
    /// トラック内容種別コード（→ track_content_kinds）。
    /// "SONG" / "BGM" / "DRAMA" / "RADIO" / "JINGLE" / "CHAPTER" / "OTHER"。
    /// </summary>
    public string ContentKindCode { get; set; } = "OTHER";

    /// <summary>歌トラック時に参照する録音 ID（→ song_recordings）。</summary>
    public int? SongRecordingId { get; set; }

    /// <summary>
    /// 歌トラック時のサイズ種別コード（→ song_size_variants）。
    /// フルサイズ・TVサイズ・映画サイズ等。SONG 以外では NULL。
    /// </summary>
    public string? SongSizeVariantCode { get; set; }

    /// <summary>
    /// 歌トラック時のパート種別コード（→ song_part_variants）。
    /// 通常歌入り・オリジナル・カラオケ・コーラス入り等。SONG 以外では NULL。
    /// </summary>
    public string? SongPartVariantCode { get; set; }

    /// <summary>
    /// 劇伴トラック時に参照する <c>bgm_cues</c> の第 1 列（シリーズ ID）。
    /// BGM 以外のトラックでは NULL。2 列セット（<see cref="BgmSeriesId"/>,
    /// <see cref="BgmMNoDetail"/>）は同時に NOT NULL または同時に NULL の 2 状態のみ許容される
    /// （DB トリガーで検証）。
    /// </summary>
    public int? BgmSeriesId { get; set; }

    /// <summary>劇伴トラック時に参照する <c>bgm_cues</c> の第 2 列（M 番号詳細）。</summary>
    public string? BgmMNoDetail { get; set; }

    /// <summary>
    /// ディスク固有のトラックタイトル上書き。
    /// ドラマ／ラジオ等の実体テーブルを持たないトラックのタイトル表示にも利用する。
    /// </summary>
    public string? TrackTitleOverride { get; set; }

    // ── 物理情報 ──

    /// <summary>開始 LBA（Logical Block Address）。CD-DA のみ利用。</summary>
    public uint? StartLba { get; set; }

    /// <summary>尺（フレーム数。CD-DA は 1/75 秒単位、BD/DVD は秒×75 に換算して格納）。</summary>
    public uint? LengthFrames { get; set; }

    /// <summary>ISRC（International Standard Recording Code、12 文字）。</summary>
    public string? Isrc { get; set; }

    /// <summary>データトラックか（Control bit 0x04）。</summary>
    public bool IsDataTrack { get; set; }

    /// <summary>プリエンファシスが掛かっているか（Control bit 0x01）。</summary>
    public bool HasPreEmphasis { get; set; }

    /// <summary>デジタルコピー許可か（Control bit 0x08）。</summary>
    public bool IsCopyPermitted { get; set; }

    // ── CD-Text（生データ） ──

    /// <summary>CD-Text のトラックタイトル。</summary>
    public string? CdTextTitle { get; set; }

    /// <summary>CD-Text のトラック演奏者。</summary>
    public string? CdTextPerformer { get; set; }

    // ── 備考・監査 ──

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザー。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザー。</summary>
    public string? UpdatedBy { get; set; }
}
