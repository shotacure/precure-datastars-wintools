namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_role_blocks テーブルに対応するエンティティモデル（PK: block_id）。
/// 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロックだが、
/// 「役職ヘッダーを共有しつつ複数の段組にエントリを並べる」場合に複数行が立つ。
/// <see cref="ColCount"/> はブロック内エントリを「何カラムで並べるか」の表示意図。
/// 列名は 旧 <c>Rows</c> / <c>Cols</c> から
/// <c>RowCount</c> / <see cref="ColCount"/> にリネーム。MySQL 8.0 で
/// <c>ROWS</c> がウィンドウ関数用の予約語に追加されたため、SELECT 等で
/// バッククォート漏れによる構文エラーが起きやすかったための恒久対応。
/// 行数はカラム数とエントリ数の従属関係で実行時に決まるため、独立した
/// <c>RowCount</c> プロパティは持たない。
/// 独立して持つと「row_count と実エントリ数の不整合」という不正状態を生む余地があるため。
/// <see cref="LeadingCompanyAliasId"/> はブロック先頭に企業名を出すケースの企業名義を入れる。
/// 「(株)○○ 　脚本: A 　演出: B」のように先頭企業名を伴うブロック構成で使用。
/// <see cref="HeadingSeriesId"/> / <see cref="HeadingText"/> はブロック先頭に出す見出し（作品名・「特別出演」など）。
/// 見出しは屋号よりも上に出る。
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

    /// <summary>
    /// ブロック先頭の見出しにする作品 ID（→ series.series_id、任意）。
    /// 複数の作品のキャラクターが並ぶ映画の声の出演で、作品ごとのまとまりの頭に出る作品名を表す。
    /// 表示文字は <see cref="HeadingText"/> があればそれ、無ければ作品の正式タイトル。
    /// </summary>
    public int? HeadingSeriesId { get; set; }

    /// <summary>
    /// ブロック先頭の見出しの文字（任意、画面の表記どおり）。
    /// 作品を指す見出しで画面の表記が正式タイトルと違うときの表示文字、または作品ではない見出し（「特別出演」など）。
    /// </summary>
    public string? HeadingText { get; set; }

    /// <summary>
    /// ブロック先頭の見出しを斜体で出すか（画面どおり。プリキュアオールスターズDX の声の出演の作品名など）。
    /// 見出しが無いブロックでは意味を持たない。
    /// </summary>
    public bool HeadingItalic { get; set; }

    /// <summary>見出し（作品または文字）を持つか。</summary>
    public bool HasHeading => HeadingSeriesId is not null || !string.IsNullOrEmpty(HeadingText);

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
