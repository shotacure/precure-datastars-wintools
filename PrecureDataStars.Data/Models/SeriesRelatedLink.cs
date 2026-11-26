namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_related_links テーブルに対応するエンティティモデル（PK: link_id）。
/// 作品ごとの関連記事（外のサイトの記事など）。サイトは作品詳細の「関連記事」の節に、
/// <see cref="IsEnabled"/> が立っている行を並び順で出し、その後ろにその作品ページへリンクしているコラムを自動で並べる。
/// 行は SQL で入れる。
/// </summary>
public sealed class SeriesRelatedLink
{
    /// <summary>行の ID（PK）。</summary>
    public int LinkId { get; set; }

    /// <summary>作品 ID（→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>記事の題（表示どおり）。</summary>
    public string Title { get; set; } = "";

    /// <summary>記事の URL。</summary>
    public string Url { get; set; } = "";

    /// <summary>サイトの名前（precure.news など）。題の脇に小さく出す。</summary>
    public string SiteLabel { get; set; } = "";

    /// <summary>記事の日付。無ければ出さない。</summary>
    public DateTime? PublishedOn { get; set; }

    /// <summary>並び順（小さいほど先）。</summary>
    public ushort DisplayOrder { get; set; }

    /// <summary>false にすると出さない。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成者。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新者。</summary>
    public string? UpdatedBy { get; set; }
}
