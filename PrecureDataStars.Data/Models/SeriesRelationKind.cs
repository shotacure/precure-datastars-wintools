namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_relation_kinds テーブルに対応するマスタモデル（PK: relation_code）。
/// <para>
/// シリーズ間の親子関係の種別を定義する。
/// 例: "COFEATURE"（併映）、"SEGMENT"（分割放送の一区画）等。
/// </para>
/// <para>
/// 各レコードは「子 → 親」方向の表示名 (<see cref="NameJa"/> / <see cref="NameEn"/>) と、
/// 逆向き「親 → 子」方向の表示名 (<see cref="NameJaReverse"/> / <see cref="NameEnReverse"/>)
/// の 2 組を保持する（v1.3.1 追加）。
/// 例: SEQUEL の <see cref="NameJa"/>「続編」⇔ <see cref="NameJaReverse"/>「前作」。
/// COFEATURE のように対称関係の場合は両者を同じ値（"併映"）にする。
/// </para>
/// </summary>
public sealed class SeriesRelationKind
{
    /// <summary>関係種別コード（PK、例: "COFEATURE", "SEGMENT"）。</summary>
    public string RelationCode { get; set; } = "";

    /// <summary>
    /// 日本語表示名（子 → 親方向、例: "続編"・"映画"・"パート作品"）。
    /// 子作品ページから親作品を参照する文脈、または親作品ページの子作品リストで
    /// 子側に付けるバッジテキストとして使う。
    /// </summary>
    public string NameJa { get; set; } = "";

    /// <summary>
    /// 日本語表示名（逆向き＝親 → 子方向、例: "前作"・"TVシリーズ"・"セット作品"、v1.3.1 追加）。
    /// 親作品ページから子作品を見るときに「親側」が文脈になる場面で使う。
    /// 例：子「映画 MH」のページに親「無印 MH」を載せるとき、親側のバッジを "TVシリーズ" にする等。
    /// 既定値は空文字。空文字のときの取り扱いは利用側 (Site 側) でフォールバックを定義する想定。
    /// </summary>
    public string NameJaReverse { get; set; } = "";

    /// <summary>英語表示名（子 → 親方向、任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// 英語表示名（逆向き＝親 → 子方向、v1.3.1 追加）。
    /// <see cref="NameJaReverse"/> の英語版。同様に空のときは利用側でフォールバック想定。
    /// </summary>
    public string? NameEnReverse { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}
