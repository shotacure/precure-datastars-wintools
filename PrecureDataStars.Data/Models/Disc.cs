namespace PrecureDataStars.Data.Models;

/// <summary>
/// discs テーブルに対応するエンティティモデル（PK: catalog_no）。
/// <para>
/// 物理ディスク（CD / BD / DVD / DL 等）を表す。品番（catalog_no）を主キーとし、
/// 商品（products）への所属を <see cref="ProductCatalogNo"/>（代表品番）で示す。
/// 複数枚組の場合は <see cref="DiscNoInSet"/> に 1, 2, 3… を格納する。単品は NULL。
/// </para>
/// <remarks>
/// CDAnalyzer が取得する MCN・TOC・CD-Text 系の情報を格納する各カラムも併せ持つ。
/// </remarks>
/// </summary>
public sealed class Disc
{
    // ── 主キー・外部キー ──

    /// <summary>品番（PK、例: "COCX-12345"）。</summary>
    public string CatalogNo { get; set; } = "";

    /// <summary>所属する商品の代表品番（→ products.product_catalog_no）。</summary>
    public string ProductCatalogNo { get; set; } = "";

    // ── 表題（商品と別タイトルを持つ場合のみ使用） ──

    /// <summary>ディスク個別タイトル（単品時は NULL でよい）。</summary>
    public string? Title { get; set; }

    /// <summary>ディスク個別タイトルの略称。</summary>
    public string? TitleShort { get; set; }

    /// <summary>ディスク個別英語タイトル。</summary>
    public string? TitleEn { get; set; }

    // ── セット内位置 ──

    /// <summary>セット内でのディスク番号。単品は NULL、複数枚組は 1, 2, 3…。</summary>
    public uint? DiscNoInSet { get; set; }

    // ── 分類 ──

    /// <summary>ディスク用途種別（→ disc_kinds）。本編／特典／カラオケ等。</summary>
    public string? DiscKindCode { get; set; }

    /// <summary>物理メディアフォーマット（"CD", "CD_ROM", "DVD", "BD", "DL", "OTHER"）。</summary>
    public string MediaFormat { get; set; } = "CD";

    // ── CD-DA 物理情報（CDAnalyzer 取得系） ──

    /// <summary>メディアカタログ番号（EAN/JAN 13 桁バーコード）。</summary>
    public string? Mcn { get; set; }

    /// <summary>総トラック数。</summary>
    public byte? TotalTracks { get; set; }

    /// <summary>ディスク総尺（フレーム数。CD-DA は 1 フレーム=1/75秒、BD/DVD は秒×75 に換算）。</summary>
    public uint? TotalLengthFrames { get; set; }

    /// <summary>チャプター数（BD/DVD のみ使用。CD では TotalTracks と同値を入れてよい）。</summary>
    public ushort? NumChapters { get; set; }

    /// <summary>ボリュームラベル（BD/DVD のファイルシステム上のラベル）。</summary>
    public string? VolumeLabel { get; set; }

    // ── CD-Text アルバム情報（生データ） ──

    /// <summary>CD-Text のアルバムタイトル。</summary>
    public string? CdTextAlbumTitle { get; set; }

    /// <summary>CD-Text のアルバム演奏者。</summary>
    public string? CdTextAlbumPerformer { get; set; }

    /// <summary>CD-Text のアルバム作詞者。</summary>
    public string? CdTextAlbumSongwriter { get; set; }

    /// <summary>CD-Text のアルバム作曲者。</summary>
    public string? CdTextAlbumComposer { get; set; }

    /// <summary>CD-Text のアルバム編曲者。</summary>
    public string? CdTextAlbumArranger { get; set; }

    /// <summary>CD-Text のアルバムメッセージ。</summary>
    public string? CdTextAlbumMessage { get; set; }

    /// <summary>CD-Text 内の Disc ID フィールド。</summary>
    public string? CdTextDiscId { get; set; }

    /// <summary>CD-Text のジャンル。</summary>
    public string? CdTextGenre { get; set; }

    // ── 計算済み識別子 ──

    /// <summary>freedb 互換の Disc ID（TOC ハッシュ、8 桁 HEX）。</summary>
    public string? CddbDiscId { get; set; }

    /// <summary>MusicBrainz の Disc ID。</summary>
    public string? MusicbrainzDiscId { get; set; }

    // ── 最終読み取り ──

    /// <summary>CDAnalyzer/BDAnalyzer が最後にこのディスクを読んだ日時。</summary>
    public DateTime? LastReadAt { get; set; }

    // ── 備考 ──

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザー。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザー。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
