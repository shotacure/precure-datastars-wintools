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
/// "SERIAL"       ... 連載。<see cref="DefaultFormatTemplate"/> でシリーズ別表記を切り替え / 
/// "THEME_SONG"   ... 主題歌。entry が song_recording と label company_alias を持つ / 
/// "VOICE_CAST"   ... 声の出演。entry がキャラクター名義 + 人物名義のペアを持つ / 
/// "COMPANY_ONLY" ... 企業のみが並ぶ役職（制作著作・製作協力・レーベル等） / 
/// "LOGO_ONLY"    ... ロゴのみが並ぶ役職。
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

    /// <summary>
    /// 既定の書式テンプレ。<c>{name}</c>, <c>{character}</c>, <c>{person}</c>, <c>{song}</c>, <c>{label}</c>
    /// 等のプレースホルダを書式解決時に置換する想定。シリーズ別の上書きは
    /// <c>series_role_format_overrides</c> 側で行う。
    /// </summary>
    public string? DefaultFormatTemplate { get; set; }

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
