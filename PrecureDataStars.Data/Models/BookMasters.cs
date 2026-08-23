namespace PrecureDataStars.Data.Models;

/// <summary>
/// book_genres テーブルに対応するジャンルマスタ（PK: genre_code）。
/// 「設定資料集」「ムック」「絵本」など書籍の性格を表すコード表で、product_kinds と同じ流儀。
/// 1 冊が複数ジャンルを持てるため、書籍との対応は <see cref="BookGenreLink"/>（多対多）で表す。
/// </summary>
public sealed class BookGenre
{
    /// <summary>ジャンルコード（主キー）。</summary>
    public string GenreCode { get; set; } = "";

    /// <summary>和名表示。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英名表示（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順。索引ページのジャンルタブの並びに使う。</summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// book_credit_roles テーブルに対応する書籍役職マスタ（PK: role_code）。
/// アニメクレジットの <c>roles</c> とは別系統にしてある（roles に混ぜると
/// <c>/creators/roles/</c> の集計へ書籍役職が混入するため）。
/// </summary>
public sealed class BookCreditRole
{
    /// <summary>役職コード（主キー）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>和名表示（"著" / "監修" / "イラスト" 等）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英名表示（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// Creators API の <c>contributors[].roleType</c>（"author" / "editor" 等）に対応する値。
    /// Amazon 取り込み時の役職自動判定に使う。表示名（"著" / "編集"）より安定しているためこちらで引く。
    /// 対応する Amazon 側ロールが無い役職では NULL。
    /// </summary>
    public string? AmazonRoleType { get; set; }

    /// <summary>表示順。詳細ページのクレジット並びに使う。</summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// book_genre_links テーブルに対応する「書籍 ↔ ジャンル」の対応行。
/// <see cref="IsPrimary"/> は索引ページで代表として出すジャンルで、1 冊につき最大 1 行という
/// 排他性はアプリ側（<c>BooksRepository</c> のトランザクション）で担保する。
/// </summary>
public sealed class BookGenreLink
{
    /// <summary>書籍 ID。</summary>
    public int BookId { get; set; }

    /// <summary>ジャンルコード。</summary>
    public string GenreCode { get; set; } = "";

    /// <summary>代表ジャンルか。</summary>
    public bool IsPrimary { get; set; }
}

/// <summary>
/// book_series テーブルに対応する「書籍 ↔ シリーズ」の対応行。
/// 1 冊が複数シリーズにまたがる合同本のため多対多。行が 1 件も無い書籍は
/// オールスターズ／シリーズ横断として扱う。
/// </summary>
public sealed class BookSeriesLink
{
    /// <summary>書籍 ID。</summary>
    public int BookId { get; set; }

    /// <summary>シリーズ ID。</summary>
    public int SeriesId { get; set; }

    /// <summary>同一書籍内でのシリーズ表示順。</summary>
    public int DisplayOrder { get; set; } = 1;
}

/// <summary>
/// book_credits テーブルに対応する書籍クレジット 1 行。
/// <see cref="PersonAliasId"/>（マスタ紐付け）と <see cref="CreditText"/>（フリーテキスト）は併用可で、
/// どちらか一方は必ず入る。両方入っている場合は「マスタに紐付いているが誌面の表記が別」を意味し、
/// 表示は <see cref="CreditText"/>、リンク先は名義に取る。
/// </summary>
public sealed class BookCredit
{
    /// <summary>クレジット行の主キー。</summary>
    public int BookCreditId { get; set; }

    /// <summary>書籍 ID。</summary>
    public int BookId { get; set; }

    /// <summary>役職コード（→ book_credit_roles）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>紐付ける人物名義 ID（→ person_aliases）。マスタに無い相手なら NULL。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>フリーテキスト表記。マスタに無い相手、または誌面表記が名義と異なる場合に入れる。</summary>
    public string? CreditText { get; set; }

    /// <summary>同一書籍内での並び順。</summary>
    public int DisplayOrder { get; set; } = 1;

    /// <summary>取り込み由来の追跡用。Creators API の role / roleType の生値。</summary>
    public string? AmazonSourceRole { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザー。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザー。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
