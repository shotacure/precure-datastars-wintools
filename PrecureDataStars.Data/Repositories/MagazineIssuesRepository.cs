using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// magazine_issues テーブル（アニメ雑誌の号マスタ）の CRUD リポジトリ。
/// 複合 PK (issue_year, issue_month) で upsert する。
/// エピソード → 号の解決は「号の年月昇順の号リスト」を前提にした二分探索で
/// 呼び出し側（SiteBuilder / エディタ）が行うため、取得は常に号の年月昇順で返す
/// （発売日は繰り上げがあり掲載範囲の境界と一致しないため、並びの基準に使わない）。
/// </summary>
public sealed class MagazineIssuesRepository : RepositoryBase
{
    /// <summary><see cref="MagazineIssuesRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public MagazineIssuesRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>全号を号の年月昇順で取得する。</summary>
    public async Task<IReadOnlyList<MagazineIssue>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              issue_year   AS IssueYear,
              issue_month  AS IssueMonth,
              release_date AS ReleaseDate
            FROM magazine_issues
            ORDER BY issue_year, issue_month;
        """;

        return await QueryListAsync<MagazineIssue>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>号を upsert する。(issue_year, issue_month) が既存なら発売日を更新、無ければ新規挿入。</summary>
    /// <param name="issue">対象の号。年・月・発売日は必須。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <exception cref="ArgumentException">年・月が不正な場合。</exception>
    public async Task UpsertAsync(MagazineIssue issue, CancellationToken ct = default)
    {
        if (issue.IssueYear < 1900 || issue.IssueYear > 9999) throw new ArgumentException("IssueYear is out of range.", nameof(issue));
        if (issue.IssueMonth < 1 || issue.IssueMonth > 12) throw new ArgumentException("IssueMonth is out of range.", nameof(issue));

        const string sql = """
            INSERT INTO magazine_issues(issue_year, issue_month, release_date)
            VALUES (@IssueYear, @IssueMonth, @ReleaseDate)
            ON DUPLICATE KEY UPDATE release_date = @ReleaseDate;
        """;

        await ExecuteAsync(sql, issue, ct).ConfigureAwait(false);
    }

    /// <summary>指定した号を削除する。存在しない場合は何もしない。</summary>
    /// <param name="issueYear">号の年。</param>
    /// <param name="issueMonth">号の月。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task DeleteAsync(int issueYear, int issueMonth, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM magazine_issues
            WHERE issue_year = @issueYear AND issue_month = @issueMonth;
        """;

        await ExecuteAsync(sql, new { issueYear, issueMonth }, ct).ConfigureAwait(false);
    }
}
