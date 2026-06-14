namespace PrecureDataStars.Data.Models;

/// <summary>
/// role_templates テーブルに対応するマスタモデル（PK: template_id, UNIQUE: (role_code, series_id)）。
/// 役職テンプレート。既定テンプレ（全シリーズ共通）とシリーズ別上書きを
/// 単一テーブルで管理する設計：
/// <list type="bullet">
///   <item><description><see cref="SeriesId"/> = null ：既定テンプレ（全シリーズ共通）</description></item>
///   <item><description><see cref="SeriesId"/> != null：そのシリーズ専用のテンプレ</description></item>
/// </list>
/// 解決ロジック：(role_code, series_id) で検索 → 無ければ (role_code, NULL) にフォールバック。
/// 期間制限（valid_from/valid_to）は当面持たない（シンプルさを優先）。
/// </summary>
public sealed class RoleTemplate
{
    /// <summary>テンプレ ID（PK、AUTO_INCREMENT）。</summary>
    public int TemplateId { get; set; }

    /// <summary>役職コード（roles.role_code への FK）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>シリーズ ID（series.series_id への FK、NULL = 既定テンプレ）。</summary>
    public int? SeriesId { get; set; }

    /// <summary>書式テンプレ文字列。Mustache 風 DSL（<c>{ROLE_NAME}</c>、<c>{COMPANIES:wrap="「」"}</c>、 <c>{#BLOCKS:first}...{/BLOCKS:first}</c>、<c>{?LEADING_COMPANY}...{/?LEADING_COMPANY}</c> 等）を含む。 改行を含むため TEXT 型で保持する。</summary>
    public string FormatTemplate { get; set; } = "";

    /// <summary>
    /// コンテンツ領域に出すヘッダ文字列。左カラム役職名の代替として、役職詳細ページへの
    /// リンク付き太字でレンダラが出力する。非 NULL のとき：
    /// <list type="bullet">
    ///   <item><description>役職ラッパ <c>&lt;div class="role"&gt;</c> 直下に
    ///   <c>&lt;div class="role-content-header"&gt;&lt;strong&gt;&lt;a href="/creators/roles/{role_code}/"&gt;{header}&lt;/a&gt;&lt;/strong&gt;&lt;/div&gt;</c>
    ///   を出力（VOICE_CAST 役職は <c>/creators/voice-cast/</c> へ）。</description></item>
    ///   <item><description>その下の本体描画は <see cref="FormatTemplate"/> があればテンプレ展開、
    ///   無ければフォールバック描画を行うが、フォールバックの左カラム役職名は空表示にする
    ///   （見出しはコンテンツヘッダ側で出し済みのため）。</description></item>
    /// </list>
    /// シリーズ別「役職名の表示テキスト・位置だけ変えたい、本体描画は触らない」ユースケース用。
    /// 例：シリーズ 3 の「製作委員会」を「映画ふたりはプリキュアM製作委員会」と出すケース。
    /// NULL のとき従来挙動と完全互換（左カラム役職名で表示）。
    /// </summary>
    public string? ContentHeaderOverride { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
