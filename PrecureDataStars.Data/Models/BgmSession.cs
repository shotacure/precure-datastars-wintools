namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_sessions テーブルに対応するエンティティモデル（複合 PK: series_id + session_no）。
/// <para>
/// 劇伴の録音セッションマスタ。シリーズごとに <see cref="SessionNo"/> 0, 1, 2, ... と採番する。
/// <see cref="SessionNo"/> = 0 は「未設定」用の既定値で、各シリーズに 1 件ずつ初期投入される。
/// </para>
/// <para>
/// 将来的に録音日・スタジオ名等の属性を追加するための器。現在は <see cref="SessionName"/> のみ保持する。
/// </para>
/// </summary>
public sealed class BgmSession
{
    /// <summary>所属シリーズ ID（→ series）。複合 PK の第 1 列。</summary>
    public int SeriesId { get; set; }

    /// <summary>セッション番号。シリーズごとに 0, 1, 2, ... と採番。複合 PK の第 2 列。</summary>
    public byte SessionNo { get; set; } = 0;

    /// <summary>セッション名（例: "1st Recording 2004/03", "(未設定)"）。</summary>
    public string SessionName { get; set; } = "";

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
