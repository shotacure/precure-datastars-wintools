namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_sessions テーブルに対応するエンティティモデル（複合 PK: series_id + session_no）。
/// 劇伴の録音セッションマスタ。シリーズごとに <see cref="SessionNo"/> 0, 1, 2, ... と採番する。
/// <see cref="SessionNo"/> = 0 は「未設定」用の既定値で、各シリーズに 1 件ずつ初期投入される。
/// </summary>
public sealed class BgmSession
{
    /// <summary>所属シリーズ ID（→ series）。複合 PK の第 1 列。</summary>
    public int SeriesId { get; set; }

    /// <summary>セッション番号。シリーズごとに 0, 1, 2, ... と採番。複合 PK の第 2 列。</summary>
    public byte SessionNo { get; set; } = 0;

    /// <summary>セッション名（例: "1st Recording 2004/03", "(未設定)"）。</summary>
    public string SessionName { get; set; } = "";

    /// <summary>
    /// 公開サイトのセッション見出し横に小さく添える補足説明の自由テキスト。
    /// 録音日・スタジオ名などを想定しているが書式は運用側に委ねる。
    /// NULL のときは劇伴詳細ページの見出しに caption 自体が出力されない。
    /// <see cref="Notes"/> が内部メモ用途で公開 UI に出ないのに対し、
    /// 本プロパティは閲覧 UI への表示が前提という違いがある。
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
