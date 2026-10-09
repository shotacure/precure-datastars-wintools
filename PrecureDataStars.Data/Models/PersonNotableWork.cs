namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_notable_works テーブルに対応するエンティティモデル（PK: work_id）。
/// 人物の「代表作（プリキュアを除く）」1 件（1 人物 × 1 作品 × 1 役職）。
/// 作品はプリキュアシリーズの外のものなので、作品名・役職はテキストで持つ。
/// </summary>
public sealed class PersonNotableWork
{
    /// <summary>主キー（AUTO_INCREMENT）。</summary>
    public int WorkId { get; set; }

    /// <summary>人物。</summary>
    public int PersonId { get; set; }

    /// <summary>人物ごとの並び順（小さい順。同じなら <see cref="YearFrom"/> 順）。</summary>
    public int DisplayOrder { get; set; }

    /// <summary>作品名。</summary>
    public string WorkTitle { get; set; } = "";

    /// <summary>役職。公式のスタッフ表の表記どおり（「監督」「シリーズディレクター」「キャラクターデザイン」「キャラクターコンセプトデザイン」「シリーズ構成」「プロデューサー」など）。</summary>
    public string RoleLabel { get; set; } = "";

    /// <summary>時期の始まりの年（任意）。</summary>
    public int? YearFrom { get; set; }

    /// <summary>時期の終わりの年（任意）。1 年だけなら NULL。</summary>
    public int? YearTo { get; set; }

    /// <summary>作品の公式サイト（サイトでリンクする先）。</summary>
    public string? OfficialUrl { get; set; }

    /// <summary>true なら <see cref="OfficialUrl"/> は閉鎖済みの公式サイトのアーカイブ（Internet Archive）。</summary>
    public bool OfficialUrlIsArchive { get; set; }

    /// <summary>裏取りに使ったスタッフ表のページ（内部用。サイトには出さない）。</summary>
    public string? SourceUrl { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>代表作として載せる役職の言い回し（入力の候補）。これ以外も入れられる。</summary>
public static class PersonNotableWorkRoles
{
    /// <summary>入力画面の候補に出す順。</summary>
    public static readonly IReadOnlyList<string> Suggestions = new[]
    {
        "監督",
        "総監督",
        "シリーズ監督",
        "シリーズディレクター",
        "チーフディレクター",
        "キャラクターデザイン",
        "キャラクター原案",
        "キャラクターコンセプトデザイン",
        "シリーズ構成",
        "プロデューサー",
    };
}
