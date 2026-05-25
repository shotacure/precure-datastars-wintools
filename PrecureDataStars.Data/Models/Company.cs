namespace PrecureDataStars.Data.Models;

/// <summary>companies テーブルに対応するエンティティモデル（PK: company_id）。 「同一企業としての同一性」を持たせる単位。屋号変更や社名変更は <c>company_aliases</c> 側で管理し、本テーブルは「企業」一意の器として機能する。 分社化等で別企業として扱う場合は新規レコードを立て、屋号側の前後リンク （predecessor / successor）で系譜を辿る。</summary>
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

    /// <summary>X (Twitter) アカウント URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? XUrl { get; set; }

    /// <summary>Instagram アカウント URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? InstagramUrl { get; set; }

    /// <summary>YouTube チャンネル URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? YoutubeUrl { get; set; }

    /// <summary>Wikipedia 記事 URL（任意・内部メモ）。 サイト UI からはリンクしない方針で、将来 JSON-LD の sameAs 等での裏付け用途に温存する。</summary>
    public string? WikipediaUrl { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
