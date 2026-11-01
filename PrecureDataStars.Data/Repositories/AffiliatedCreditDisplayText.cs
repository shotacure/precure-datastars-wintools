using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// 所属を添えられる作家連名（song_credits / bgm_cue_credits）の 1 役職分を、表示用の 1 行文字列に畳む共通処理。
/// 所属は出し方（affiliation_display）に従い、名義のあとに「（所属）」を添えるか、所属の会社名だけを出すか
/// （同じ会社名だけの行が続くときは 1 回だけ）、名義だけを出す。次の名義と同じ所属が続くときは、
/// 最後の名義のあとにだけ添える（盤スタッフの所属と同じ畳み方）。
/// </summary>
internal static class AffiliatedCreditDisplayText
{
    /// <summary>
    /// 連名の 1 行分。<c>DisplayName</c> は名義の表示名（名義を持たない文字だけの行はその文字）、
    /// <c>Affiliation</c> は所属名（会社名義を優先し、無ければ自由記述。無ければ null）、<c>Display</c> は所属の出し方。
    /// </summary>
    internal readonly record struct Row(string? Sep, string DisplayName, string? Affiliation, string Display);

    /// <summary>credit_seq 順の連名行を 1 行の文字列に畳む。先頭は区切りなし、それ以降は区切り + 名前を連結する。</summary>
    internal static string Build(IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0) return "";

        string ShownAff(int i) => rows[i].Display == SongCreditAffiliationDisplay.NameWithCompany ? rows[i].Affiliation ?? "" : "";
        bool CompanyOnly(int i) => rows[i].Display == SongCreditAffiliationDisplay.CompanyOnly && !string.IsNullOrEmpty(rows[i].Affiliation);
        var sb = new System.Text.StringBuilder();
        string? lastCompanyOnly = null;
        for (int i = 0; i < rows.Count; i++)
        {
            bool companyOnly = CompanyOnly(i);
            if (companyOnly && string.Equals(lastCompanyOnly, rows[i].Affiliation, StringComparison.Ordinal)) continue;
            if (sb.Length > 0) sb.Append(rows[i].Sep ?? "");
            if (companyOnly)
            {
                sb.Append(rows[i].Affiliation);
                lastCompanyOnly = rows[i].Affiliation;
                continue;
            }
            lastCompanyOnly = null;
            sb.Append(rows[i].DisplayName);
            string aff = ShownAff(i);
            string nextAff = i + 1 < rows.Count ? ShownAff(i + 1) : "";
            if (aff.Length > 0 && !string.Equals(aff, nextAff, StringComparison.Ordinal)) sb.Append('（').Append(aff).Append('）');
        }
        return sb.ToString();
    }
}
