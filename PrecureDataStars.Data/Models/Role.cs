namespace PrecureDataStars.Data.Models;

/// <summary>
/// roles テーブルに対応するマスタモデル（PK: role_code）。
/// <para>
/// クレジット内の役職を定義する。<see cref="RoleFormatKind"/> で「この役職下のエントリは
/// どのような書式・参照を取るか」を分類する。
/// </para>
/// <para>
/// <see cref="RoleFormatKind"/> の意味:
/// "NORMAL"       ... 単純な「役職: 名義列」（脚本／演出／作画監督 等） / 
/// "SERIAL"       ... 連載。書式は <c>role_templates</c> テーブルで持つ / 
/// "THEME_SONG"   ... 主題歌。entry が song_recording を持つ / 
/// "VOICE_CAST"   ... 声の出演。entry がキャラクター名義 + 人物名義のペアを持つ / 
/// "COMPANY_ONLY" ... 企業のみが並ぶ役職（制作著作・製作協力・レーベル等） / 
/// "LOGO_ONLY"    ... ロゴのみが並ぶ役職。
/// </para>
/// <para>
/// v1.2.0 工程 H-10 で <c>DefaultFormatTemplate</c> プロパティを撤去した。書式テンプレは
/// 新設の <see cref="RoleTemplate"/>（<c>role_templates</c> テーブル）で管理する。
/// </para>
/// </summary>
public sealed class Role
{
    /// <summary>役職コード（PK、例: "SCRIPT", "VOICE_CAST", "PRODUCTION_AUTH"）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>日本語表示名（例: "脚本"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>役職書式区分（"NORMAL"/"SERIAL"/"THEME_SONG"/"VOICE_CAST"/"COMPANY_ONLY"/"LOGO_ONLY"）。既定 "NORMAL"。</summary>
    public string RoleFormatKind { get; set; } = "NORMAL";

    /// <summary>表示順（小さい値ほど先頭。UNIQUE）。</summary>
    public ushort? DisplayOrder { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
