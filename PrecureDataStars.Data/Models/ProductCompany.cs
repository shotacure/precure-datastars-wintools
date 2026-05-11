namespace PrecureDataStars.Data.Models;

/// <summary>
/// product_companies テーブルに対応するエンティティモデル（PK: product_company_id）。
/// <para>
/// 商品の流通元（レーベル名・販売元名）を表す、クレジット非依存の社名マスタ。
/// <see cref="Product.LabelProductCompanyId"/> および
/// <see cref="Product.DistributorProductCompanyId"/> から FK で参照される。
/// </para>
/// <para>
/// 本マスタはあくまで「商品のメタ情報としてレコードに記載される社名スナップショット」を
/// 表すためのもので、クレジット系のマスタ（<see cref="Company"/> /
/// <see cref="CompanyAlias"/>）とは独立している。屋号系譜（前任/後任）や CI バージョンの
/// 概念は持たず、1 社 = 1 行のシンプル構造で和名・かな・英名のみ保持する。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ stage 20 で新設。同 stage 確定版で
/// <see cref="IsDefaultLabel"/> / <see cref="IsDefaultDistributor"/> フラグ列を追加した。
/// 新規商品作成時の既定社を 1 行だけ指定するためのフラグで、排他性（最大 1 行）は
/// アプリ側（<see cref="Repositories.ProductCompaniesRepository.InsertAsync"/> /
/// <see cref="Repositories.ProductCompaniesRepository.UpdateAsync"/> のトランザクション内で
/// 他の行を 0 に落としてからセット）で担保する。
/// </para>
/// </summary>
public sealed class ProductCompany
{
    /// <summary>主キー（AUTO_INCREMENT）。</summary>
    public int ProductCompanyId { get; set; }

    /// <summary>社名（日本語）。必須。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>社名読み（ひらがな）。任意。</summary>
    public string? NameKana { get; set; }

    /// <summary>社名（英語）。任意。</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// 新規商品作成時のレーベル既定にする社かどうか。マスタ全体で最大 1 行のみ true となる
    /// （排他性は <see cref="Repositories.ProductCompaniesRepository"/> 内のトランザクションで担保）。
    /// </summary>
    public bool IsDefaultLabel { get; set; }

    /// <summary>
    /// 新規商品作成時の販売元既定にする社かどうか。マスタ全体で最大 1 行のみ true となる
    /// （排他性は <see cref="Repositories.ProductCompaniesRepository"/> 内のトランザクションで担保）。
    /// </summary>
    public bool IsDefaultDistributor { get; set; }

    /// <summary>備考。任意。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

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
