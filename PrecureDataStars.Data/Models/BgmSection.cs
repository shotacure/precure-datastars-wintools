namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_sections テーブルに対応するエンティティモデル（複合 PK: series_id + session_no + section_no）。
/// 録音セッション（<see cref="BgmSession"/>）内の区分で、BGM リスト上のセクションを表す。
/// <see cref="SectionNo"/> はセッションごとに 1, 2, 3, ... と採番し、セッション内の表示順を兼ねる。
/// 音源側は <see cref="BgmCue.SectionNo"/> で所属を持つ。
/// </summary>
public sealed class BgmSection
{
    /// <summary>所属シリーズ ID（→ series）。複合 PK の第 1 列。</summary>
    public int SeriesId { get; set; }

    /// <summary>所属セッション番号（→ bgm_sessions）。複合 PK の第 2 列。</summary>
    public byte SessionNo { get; set; }

    /// <summary>セッション内のセクション番号（1 始まり）。複合 PK の第 3 列で、表示順を兼ねる。</summary>
    public byte SectionNo { get; set; }

    /// <summary>セクション名。公開サイトの劇伴詳細ページでセッション内の小見出しになる。</summary>
    public string SectionName { get; set; } = "";

    /// <summary>備考（内部メモ。公開 UI には出さない）。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
