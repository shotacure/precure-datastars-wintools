namespace PrecureDataStars.Data.Models;

/// <summary>
/// entity_sources テーブルに対応するエンティティモデル（PK: source_id）。
/// 人物または企業のプロフィール（読み・誕生日など）の出典 1 件（1 対象 × 1 ページ）。
/// <see cref="PersonId"/> と <see cref="CompanyId"/> のどちらか一方だけを持つ。
/// ページタイトル・見出し・発行者はどれもページの表記を一字一句そのまま持つ。
/// サイトの人物・企業の詳細ページでは、<see cref="Supports"/> の項目に脚注の番号を付け、ページの一番下の脚注に
/// ページタイトル・見出し・発行者・URL・確認日を出す。
/// </summary>
public sealed class EntitySource
{
    /// <summary>主キー（AUTO_INCREMENT）。</summary>
    public int SourceId { get; set; }

    /// <summary>対象の人物（企業の出典なら null）。</summary>
    public int? PersonId { get; set; }

    /// <summary>対象の企業（人物の出典なら null）。</summary>
    public int? CompanyId { get; set; }

    /// <summary>対象ごとの並び順（小さい順。同じなら source_id 順）。脚注の番号の順になる。</summary>
    public int DisplayOrder { get; set; }

    /// <summary>この出典で裏付ける項目（<see cref="EntitySourceFields"/> の値をカンマでつないだもの。MySQL の SET 型）。空なら本文に番号を付けない。</summary>
    public string Supports { get; set; } = "";

    /// <summary>ページタイトル（ページの &lt;title&gt; を一字一句そのまま）。</summary>
    public string PageTitle { get; set; } = "";

    /// <summary>情報が載っている欄の見出し（ページの表記どおり）。無ければ null。</summary>
    public string? SectionTitle { get; set; }

    /// <summary>発行者（ページの著作権表記の名前どおり）。</summary>
    public string Publisher { get; set; } = "";

    /// <summary>開いたページの URL（見出しへの # 付きでもよい）。</summary>
    public string Url { get; set; } = "";

    /// <summary>そのページで確かめた日。</summary>
    public DateTime CheckedOn { get; set; }

    /// <summary>備考（内部用。サイトには出さない）。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary><see cref="Supports"/> を項目コードの並びに分ける。</summary>
    public IReadOnlyList<string> SupportCodes()
        => Supports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// 出典で裏付ける項目（<c>entity_sources.supports</c> の SET の値）。サイトの本文でその項目に脚注の番号を付ける。
/// </summary>
public static class EntitySourceFields
{
    /// <summary>読み（人物・企業）。見出しの下の読みの行。</summary>
    public const string Kana = "KANA";
    /// <summary>誕生日（人物）。</summary>
    public const string Birth = "BIRTH";
    /// <summary>没年（人物）。</summary>
    public const string Death = "DEATH";
    /// <summary>設立（企業）。</summary>
    public const string Founded = "FOUNDED";
    /// <summary>解散（企業）。</summary>
    public const string Dissolved = "DISSOLVED";
    /// <summary>メンバー（企業）。「所属としてクレジットされた方々」の見出し。</summary>
    public const string Members = "MEMBERS";

    /// <summary>人物の出典で選べる項目（コード, 表示名）。</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> ForPerson = new[]
    {
        (Kana, "読み"), (Birth, "誕生日"), (Death, "没年"),
    };

    /// <summary>企業の出典で選べる項目（コード, 表示名）。</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> ForCompany = new[]
    {
        (Kana, "読み"), (Founded, "設立"), (Dissolved, "解散"), (Members, "メンバー"),
    };
}
