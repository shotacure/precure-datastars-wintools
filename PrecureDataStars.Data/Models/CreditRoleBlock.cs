namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_role_blocks テーブルに対応するエンティティモデル（PK: block_id）。
/// <para>
/// 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロックだが、
/// 「役職ヘッダーを共有しつつ複数の段組にエントリを並べる」場合に複数行が立つ。
/// <see cref="ColCount"/> はブロック内エントリを「何カラムで並べるか」の表示意図。
/// </para>
/// <para>
/// 列名は 旧 <c>Rows</c> / <c>Cols</c> から
/// <c>RowCount</c> / <see cref="ColCount"/> にリネーム。MySQL 8.0 で
/// <c>ROWS</c> がウィンドウ関数用の予約語に追加されたため、SELECT 等で
/// バッククォート漏れによる構文エラーが起きやすかったための恒久対応。
/// </para>
/// <para>
/// 行数はカラム数とエントリ数の従属関係で実行時に決まるため、独立した
/// <c>RowCount</c> プロパティは持たない。
/// 独立して持つと「row_count と実エントリ数の不整合」という不正状態を生む余地があるため。
/// </para>
/// <para>
/// <see cref="LeadingCompanyAliasId"/> はブロック先頭に企業名を出すケースの企業名義を入れる。
/// 「(株)○○ 　脚本: A 　演出: B」のように先頭企業名を伴うブロック構成で使用。
/// </para>
/// </summary>
public sealed class CreditRoleBlock
{
    /// <summary>ブロックの主キー（AUTO_INCREMENT）。</summary>
    public int BlockId { get; set; }

    /// <summary>所属するカード内役職 ID（→ credit_card_roles.card_role_id）。</summary>
    public int CardRoleId { get; set; }

    /// <summary>役職内ブロックの表示順（1 始まり）。</summary>
    public byte BlockSeq { get; set; }

    /// <summary>表示列数（既定 1、DB 列名 col_count）。1 ならエントリは縦並び、2 以上で横カラム表示。</summary>
    public byte ColCount { get; set; } = 1;

    /// <summary>ブロック先頭に出す企業名義 ID（→ company_aliases.alias_id、任意）。</summary>
    public int? LeadingCompanyAliasId { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
