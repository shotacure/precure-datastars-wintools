namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_kinds テーブルに対応するエンティティモデル（PK: character_kind）。
/// <para>
/// キャラクター区分マスタ。
/// ユーザーの分類軸である「プリキュア / 仲間たち / 敵 / とりまく人々」の 4 類型を
/// マスタとして保持する。運用者が後から類型を追加・改名できるよう独立テーブル化されている。
/// </para>
/// <para>
/// 初期投入される 4 類型：
/// <list type="bullet">
///   <item><description><c>PRECURE</c> — プリキュア（変身ヒロイン本人）</description></item>
///   <item><description><c>ALLY</c> — 仲間たち（妖精・パートナー・家族・友人など）</description></item>
///   <item><description><c>VILLAIN</c> — 敵</description></item>
///   <item><description><c>SUPPORTING</c> — とりまく人々（モブ・端役・背景人物）</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class CharacterKind
{
    /// <summary>区分コード（PK、英数 + アンダースコア）。</summary>
    public string CharacterKindCode { get; set; } = "";

    /// <summary>日本語表示名（必須）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順（10 単位飛び番、1 始まり）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}