using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 歌の作家連名（<see cref="SongCredit"/>）の名義に添える所属（「岡部 啓一（MONACA）」の「（MONACA）」）を組み立てる。
/// <list type="bullet">
///   <item>所属は会社名義（<see cref="SongCredit.AffiliationCompanyAliasId"/>）を優先し、無ければ自由記述（<see cref="SongCredit.AffiliationText"/>）。</item>
///   <item>連名の中で同じ所属が続くときは、続きの最後の名義のあとにだけ添える（盤スタッフの所属と同じ畳み方）。</item>
///   <item>HTML は盤スタッフの所属と同じ見た目（<c>music-credit-affiliation muted</c>）で、会社名義に紐付いていれば企業詳細へリンクする。</item>
/// </list>
/// 役職テンプレ（各話のクレジット画面の主題歌カード）では使わない（画面の表記どおりにするため）。
/// </summary>
public static class SongCreditAffiliation
{
    /// <summary>所属の表示名。会社名義があればその名義、無ければ自由記述、どちらも無ければ空文字。</summary>
    public static string Name(SongCredit row, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        if (row.AffiliationCompanyAliasId is int a && companyAliasMap.TryGetValue(a, out var ca)) return ca.Name ?? "";
        return row.AffiliationText?.Trim() ?? "";
    }

    /// <summary>
    /// 連名 <paramref name="rows"/>（credit_seq 順）の <paramref name="index"/> 番目の名義のあとに添える所属の HTML。
    /// 所属が無い、または次の名義と同じ所属が続くときは空文字。
    /// </summary>
    public static string SuffixHtml(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = SuffixName(rows, index, companyAliasMap);
        if (aff.Length == 0) return "";
        var row = rows[index];
        string inner = row.AffiliationCompanyAliasId is int a && companyAliasMap.TryGetValue(a, out var ca) && ca.CompanyId > 0
            ? $"<a class=\"staff-name\" href=\"{HtmlUtil.Escape(PathUtil.CompanyUrl(ca.CompanyId))}\">{HtmlUtil.Escape(aff)}</a>"
            : HtmlUtil.Escape(aff);
        return $"<span class=\"music-credit-affiliation muted\">（{inner}）</span>";
    }

    /// <summary><see cref="SuffixHtml"/> の平文版（「（MONACA）」）。meta description・OGP カードなどに使う。</summary>
    public static string SuffixText(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = SuffixName(rows, index, companyAliasMap);
        return aff.Length == 0 ? "" : $"（{aff}）";
    }

    /// <summary>添えるべき所属名。所属が無いか、次の名義と同じ所属が続くときは空文字。</summary>
    private static string SuffixName(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = Name(rows[index], companyAliasMap);
        if (aff.Length == 0) return "";
        string nextAff = index + 1 < rows.Count ? Name(rows[index + 1], companyAliasMap) : "";
        return string.Equals(aff, nextAff, StringComparison.Ordinal) ? "" : aff;
    }
}
