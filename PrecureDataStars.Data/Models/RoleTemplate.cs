namespace PrecureDataStars.Data.Models;

/// <summary>
/// role_templates テーブルに対応するマスタモデル（PK: template_id, UNIQUE: (role_code, series_id)）。
/// <para>
/// 役職テンプレート。既定テンプレ（全シリーズ共通）とシリーズ別上書きを
/// 単一テーブルで管理する設計：
/// </para>
/// <list type="bullet">
///   <item><description><see cref="SeriesId"/> = null ：既定テンプレ（全シリーズ共通）</description></item>
///   <item><description><see cref="SeriesId"/> != null：そのシリーズ専用のテンプレ</description></item>
/// </list>
/// <para>
/// 解決ロジック：(role_code, series_id) で検索 → 無ければ (role_code, NULL) にフォールバック。
/// 期間制限（valid_from/valid_to）は当面持たない（シンプルさを優先）。
/// </para>
/// </summary>
public sealed class RoleTemplate
{
    /// <summary>テンプレ ID（PK、AUTO_INCREMENT）。</summary>
    public int TemplateId { get; set; }

    /// <summary>役職コード（roles.role_code への FK）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>シリーズ ID（series.series_id への FK、NULL = 既定テンプレ）。</summary>
    public int? SeriesId { get; set; }

    /// <summary>
    /// 書式テンプレ文字列。Mustache 風 DSL（<c>{ROLE_NAME}</c>、<c>{COMPANIES:wrap="「」"}</c>、
    /// <c>{#BLOCKS:first}...{/BLOCKS:first}</c>、<c>{?LEADING_COMPANY}...{/?LEADING_COMPANY}</c> 等）を含む。
    /// 改行を含むため TEXT 型で保持する。
    /// </summary>
    public string FormatTemplate { get; set; } = "";

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}