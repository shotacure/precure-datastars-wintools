namespace PrecureDataStars.Data.Models;

/// <summary>
/// characters テーブルに対応するエンティティモデル（PK: character_id）。
/// <para>
/// キャラクターマスタ。全プリキュアを通じて統一的に管理する設計のため <c>series_id</c> は持たない。
/// All Stars・春映画・コラボ等で複数シリーズに登場するキャラは同一 <see cref="CharacterId"/> を共有する。
/// 表記揺れ（"美墨なぎさ" / "キュアブラック" / "ブラック" 等）は <c>character_aliases</c> 側で管理する。
/// </para>
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

    /// <summary>
    /// キャラクター区分（<c>character_kinds</c> マスタを参照する FK 値）。
    /// <para>
    /// "PRECURE"    ... プリキュア<br/>
    /// "ALLY"       ... 仲間たち<br/>
    /// "VILLAIN"    ... 敵<br/>
    /// "SUPPORTING" ... とりまく人々
    /// </para>
    /// 既定値は "PRECURE"。区分は運用者が <c>character_kinds</c> マスタで追加・改名できる。
    /// </summary>
    public string CharacterKind { get; set; } = "PRECURE";

    /// <summary>
    /// 生年（西暦、任意）。判明していれば値を保持する（<see cref="BirthYearVisibility"/> が
    /// <c>PRIVATE</c> でも値の保持自体は可）。不明なら <c>null</c>。
    /// </summary>
    public ushort? BirthYear { get; set; }

    /// <summary>
    /// 生年の公開可否。<c>"PUBLIC"</c>（サイト生成に生年・年齢を出す）または
    /// <c>"PRIVATE"</c>（生年・年齢を生成に出さない）。既定は <c>"PUBLIC"</c>。
    /// 誕生月日は本値に関わらず常にカレンダー／記念日の対象。
    /// </summary>
    public string BirthYearVisibility { get; set; } = "PUBLIC";

    /// <summary>誕生月（1-12、任意）。</summary>
    public byte? BirthMonth { get; set; }

    /// <summary>誕生日（1-31、任意）。<see cref="BirthMonth"/> が無いとき本値も持てない。</summary>
    public byte? BirthDay { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}