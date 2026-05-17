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
    /// キャラクター区分。
    /// <para>
    /// "MAIN"    ... 主役級（プリキュア当人など）<br/>
    /// "SUPPORT" ... 準主役<br/>
    /// "GUEST"   ... ゲスト（数話のみ登場）<br/>
    /// "MOB"     ... モブ・チョイ役<br/>
    /// "OTHER"   ... その他（ナレーション等）
    /// </para>
    /// 既定値は "MAIN"。
    /// </summary>
    public string CharacterKind { get; set; } = "MAIN";

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