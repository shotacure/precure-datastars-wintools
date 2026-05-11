
namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cue_credits テーブルに対応するエンティティモデル
/// （複合 PK: series_id + m_no_detail + credit_role + credit_seq、v1.2.3 追加 / v1.3.0 ブラッシュアップ続編で型変更）。
/// <para>
/// 1 劇伴音源（<see cref="BgmCue"/>）に対する作家連名（作曲 / 編曲）を順序付きで保持する。
/// 既存の <see cref="BgmCue.ComposerName"/> / <see cref="BgmCue.ArrangerName"/> フリーテキスト列は
/// 温存しており、本テーブルに該当役の行が無い cue では従来通りフリーテキストが表示に使われる
/// （フォールバック処理は SiteBuilder 側で当面実装しない、stage 16 のスコープ判断）。
/// </para>
/// <para>
/// 親側の bgm_cues は (series_id, m_no_detail) 複合 PK のため、本テーブルもそれを含む 4 列複合 PK。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ続編で <see cref="CreditRole"/> の型を <c>BgmCueCreditRole</c>
/// enum から <c>string</c>（roles.role_code を参照する varchar(32)）に変更した。
/// 既存値はマイグレーションで COMPOSITION / ARRANGEMENT にリネーム済み。
/// </para>
/// </summary>
public sealed class BgmCueCredit
{
    /// <summary>所属シリーズ ID（→ bgm_cues.series_id、複合 PK 第 1 列）。</summary>
    public int SeriesId { get; set; }

    /// <summary>M 番号詳細（→ bgm_cues.m_no_detail、複合 PK 第 2 列）。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>
    /// クレジット役の役職コード（→ roles.role_code）。
    /// 劇伴の典型値は COMPOSITION / ARRANGEMENT。
    /// </summary>
    public string CreditRole { get; set; } = "";

    /// <summary>同役内での連名表示順（1 始まり）。</summary>
    public byte CreditSeq { get; set; }

    /// <summary>名義参照（→ person_aliases.alias_id）。</summary>
    public int PersonAliasId { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// bgm_cue_credits.credit_role の典型値を表す定数群
/// （v1.3.0 ブラッシュアップ続編で <c>BgmCueCreditRole</c> enum から差し替え）。
/// <para>
/// 旧 enum は 2 値固定だったが、現在は roles マスタの任意の role_code を受け入れる
/// ため <c>string</c> 型のフィールドに変更されている。
/// </para>
/// <para>
/// 旧 enum のメンバ名と DB 値のマッピング：
/// <list type="bullet">
///   <item>旧 COMPOSER → <see cref="Composition"/> ("COMPOSITION")</item>
///   <item>旧 ARRANGER → <see cref="Arrangement"/> ("ARRANGEMENT")</item>
/// </list>
/// </para>
/// </summary>
public static class BgmCueCreditRoles
{
    /// <summary>作曲（旧 COMPOSER）。</summary>
    public const string Composition = "COMPOSITION";
    /// <summary>編曲（旧 ARRANGER）。</summary>
    public const string Arrangement = "ARRANGEMENT";
}
