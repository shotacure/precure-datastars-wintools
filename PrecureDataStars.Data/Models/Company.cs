namespace PrecureDataStars.Data.Models;

/// <summary>companies テーブルに対応するエンティティモデル（PK: company_id）。 「同一企業としての同一性」を持たせる単位。屋号変更や社名変更は <c>company_aliases</c> 側で管理し、本テーブルは「企業」一意の器として機能する。 分社化等で別企業として扱う場合は新規レコードを立て、企業どうしの系譜（事業の引き継ぎ）と所属（親子・部署）は <c>company_relations</c> で持つ。</summary>
public sealed class Company
{
    /// <summary>企業の主キー（AUTO_INCREMENT）。</summary>
    public int CompanyId { get; set; }

    /// <summary>正式名称（必須）。</summary>
    public string Name { get; set; } = "";

    /// <summary>正式名称の読み。</summary>
    public string? NameKana { get; set; }

    /// <summary>英語表記（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>設立日（任意）。</summary>
    public DateTime? FoundedDate { get; set; }

    /// <summary>解散日（任意）。</summary>
    public DateTime? DissolvedDate { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 外部リンク（詳細ページの末尾「外部リンク」セクションに出る） ──

    /// <summary>公式ページ URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? OfficialUrl { get; set; }

    /// <summary>リンク集のページ URL（任意。lit.link・linktr.ee など）。詳細ページにアイコン付きで表示。</summary>
    public string? LinkCollectionUrl { get; set; }

    /// <summary>X (Twitter) アカウント URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? XUrl { get; set; }

    /// <summary>2 つ目の X (Twitter) アカウント URL（任意。言語違いのアカウントなど）。詳細ページにアイコン付きで表示。</summary>
    public string? XSubUrl { get; set; }

    /// <summary>Bluesky アカウント URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? BlueskyUrl { get; set; }

    /// <summary>Instagram アカウント URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? InstagramUrl { get; set; }

    /// <summary>YouTube チャンネル URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? YoutubeUrl { get; set; }

    /// <summary>ブログ URL（任意。note・tumblr・アメーバブログなど）。詳細ページにアイコン付きで表示。</summary>
    public string? BlogUrl { get; set; }

    /// <summary>pixiv ユーザーページ URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? PixivUrl { get; set; }

    /// <summary>作品集のページ URL（任意。ArtStation・Behance・carrd など）。詳細ページにアイコン付きで表示。</summary>
    public string? PortfolioUrl { get; set; }

    /// <summary>Wikipedia 記事 URL（任意）。企業の記事。詳細ページの「外部リンク」の最後に出す。</summary>
    public string? WikipediaUrl { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
