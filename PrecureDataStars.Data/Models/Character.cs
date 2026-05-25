namespace PrecureDataStars.Data.Models;

/// <summary>
/// characters テーブルに対応するエンティティモデル（PK: character_id）。
/// キャラクターマスタ。全プリキュアを通じて統一的に管理する設計のため <c>series_id</c> は持たない。
/// All Stars・春映画・コラボ等で複数シリーズに登場するキャラは同一 <see cref="CharacterId"/> を共有する。
/// 表記揺れ（"美墨なぎさ" / "キュアブラック" / "ブラック" 等）は <c>character_aliases</c> 側で管理する。
/// </summary>
public sealed class Character
{
    /// <summary>キャラクターの主キー（AUTO_INCREMENT）。</summary>
    public int CharacterId { get; set; }

    /// <summary>正式名称（必須、例: "美墨なぎさ"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>正式名称の読み。</summary>
    public string? NameKana { get; set; }

    /// <summary>正式名称の英語表記。英文クレジット出力で使用。</summary>
    public string? NameEn { get; set; }

    /// <summary>キャラクター区分（character_kinds マスタを参照する FK 値）。</summary>
    public string CharacterKind { get; set; } = "PRECURE";

    /// <summary>生年（西暦、任意）。判明していれば値を保持する（<see cref="BirthYearVisibility"/> が <c>PRIVATE</c> でも値の保持自体は可）。不明なら <c>null</c>。</summary>
    public ushort? BirthYear { get; set; }

    /// <summary>生年の公開可否。</summary>
    public string BirthYearVisibility { get; set; } = "PUBLIC";

    /// <summary>誕生月（1-12、任意）。</summary>
    public byte? BirthMonth { get; set; }

    /// <summary>誕生日（1-31、任意）。<see cref="BirthMonth"/> が無いとき本値も持てない。</summary>
    public byte? BirthDay { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 外部リンク（詳細ページの末尾「外部リンク」セクションに出る） ──

    /// <summary>キャラクター公式ページ URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? OfficialUrl { get; set; }

    /// <summary>Wikipedia 記事 URL（任意・内部メモ）。 サイト UI からはリンクしない方針で、将来 JSON-LD の sameAs 等での裏付け用途に温存する。</summary>
    public string? WikipediaUrl { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
