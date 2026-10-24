using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 歌の作家連名（<see cref="SongCredit"/>）の名義に添える所属（「岡部 啓一（MONACA）」の「（MONACA）」）を組み立てる。
/// <list type="bullet">
///   <item>所属は会社名義（<see cref="SongCredit.AffiliationCompanyAliasId"/>）を優先し、無ければ自由記述（<see cref="SongCredit.AffiliationText"/>）。</item>
///   <item>所属の出し方（<see cref="SongCredit.AffiliationDisplay"/>）は行ごとに選ぶ。
///     名義＋所属（既定）・所属の会社名だけ（「Nostalgic Orchestra」）・名義だけ（所属を持ったまま「藤本 記子」）。</item>
///   <item>連名の中で同じ所属が続くときは、続きの最後の名義のあとにだけ添える（盤スタッフの所属と同じ畳み方）。
///     所属の会社名だけを出す行が同じ所属で続くときは、1 回だけ出す（区切りも出さない）。</item>
///   <item>HTML は盤スタッフの所属と同じ見た目（<c>music-credit-affiliation muted</c>）で、会社名義に紐付いていれば企業詳細へリンクする。</item>
/// </list>
/// 連名全体の組み立ては <see cref="BuildNamesHtml"/> / <see cref="BuildNamesText"/> に寄せ、盤のトラック・楽曲詳細・楽曲一覧のカード・
/// 主題歌の行・平文（meta description・OGP カード）がすべて同じ規則で出す。
/// 役職テンプレ（各話のクレジット画面の主題歌カード）は Data 層の <c>SongCreditsRepository.GetDisplayHtmlPartsAsync</c> が同じ規則
/// （所属は添えない。会社名だけの行は会社名義）で組む。
/// </summary>
public static class SongCreditAffiliation
{
    /// <summary>所属の表示名。会社名義があればその名義、無ければ自由記述、どちらも無ければ空文字。</summary>
    public static string Name(SongCredit row, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        if (row.AffiliationCompanyAliasId is int a && companyAliasMap.TryGetValue(a, out var ca)) return ca.Name ?? "";
        return row.AffiliationText?.Trim() ?? "";
    }

    /// <summary>名義を出さず所属の会社名だけを出す行か（出し方が COMPANY_ONLY で、所属が引けるとき。引けなければ名義に戻す）。</summary>
    public static bool IsCompanyOnly(SongCredit row, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
        => string.Equals(row.AffiliationDisplay, SongCreditAffiliationDisplay.CompanyOnly, StringComparison.Ordinal)
           && Name(row, companyAliasMap).Length > 0;

    /// <summary>名義のあとに添える所属名（出し方が名義＋所属のときだけ。それ以外は空文字）。</summary>
    private static string ShownSuffixName(SongCredit row, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
        => string.IsNullOrEmpty(row.AffiliationDisplay)
           || string.Equals(row.AffiliationDisplay, SongCreditAffiliationDisplay.NameWithCompany, StringComparison.Ordinal)
            ? Name(row, companyAliasMap)
            : "";

    /// <summary>所属の HTML（会社名義に紐付いていれば企業詳細へのリンク、無ければエスケープした平文）。</summary>
    private static string AffiliationInnerHtml(SongCredit row, string name, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
        => row.AffiliationCompanyAliasId is int a && companyAliasMap.TryGetValue(a, out var ca) && ca.CompanyId > 0
            ? $"<a class=\"staff-name\" href=\"{HtmlUtil.Escape(PathUtil.CompanyUrl(ca.CompanyId))}\">{HtmlUtil.Escape(name)}</a>"
            : HtmlUtil.Escape(name);

    /// <summary>
    /// 連名 <paramref name="rows"/>（credit_seq 順）の <paramref name="index"/> 番目の名義のあとに添える所属の HTML。
    /// 所属が無い、出し方が名義＋所属でない、または次の名義と同じ所属が続くときは空文字。
    /// </summary>
    public static string SuffixHtml(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = SuffixName(rows, index, companyAliasMap);
        if (aff.Length == 0) return "";
        return $"<span class=\"music-credit-affiliation muted\">（{AffiliationInnerHtml(rows[index], aff, companyAliasMap)}）</span>";
    }

    /// <summary><see cref="SuffixHtml"/> の平文版（「（MONACA）」）。meta description・OGP カードなどに使う。</summary>
    public static string SuffixText(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = SuffixName(rows, index, companyAliasMap);
        return aff.Length == 0 ? "" : $"（{aff}）";
    }

    /// <summary>添えるべき所属名。所属が無いか、出し方が名義＋所属でないか、次の名義と同じ所属が続くときは空文字。</summary>
    private static string SuffixName(IReadOnlyList<SongCredit> rows, int index, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        string aff = ShownSuffixName(rows[index], companyAliasMap);
        if (aff.Length == 0) return "";
        string nextAff = index + 1 < rows.Count ? ShownSuffixName(rows[index + 1], companyAliasMap) : "";
        return string.Equals(aff, nextAff, StringComparison.Ordinal) ? "" : aff;
    }

    /// <summary>
    /// 連名を出す行の並び（<c>(行の位置, 会社名だけか)</c>）。所属の会社名だけを出す行が同じ所属で続くときは、2 行目以降を除く。
    /// 表示の数（連名が 1 つに見えるか）の判定にも使う。
    /// </summary>
    public static IReadOnlyList<(int Index, bool CompanyOnly)> VisibleRows(IReadOnlyList<SongCredit> rows, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        var result = new List<(int, bool)>(rows.Count);
        string? lastCompanyOnly = null;
        for (int i = 0; i < rows.Count; i++)
        {
            if (IsCompanyOnly(rows[i], companyAliasMap))
            {
                string name = Name(rows[i], companyAliasMap);
                if (string.Equals(lastCompanyOnly, name, StringComparison.Ordinal)) continue;
                result.Add((i, true));
                lastCompanyOnly = name;
            }
            else
            {
                result.Add((i, false));
                lastCompanyOnly = null;
            }
        }
        return result;
    }

    /// <summary>
    /// 連名の HTML を組み立てる。名義は <paramref name="personHtml"/>（人物リンクと、別名義の本名義の括弧など呼び出し側の添え物）で出し、
    /// 所属は出し方に従って添えるか、会社名だけに置き換える。区切りは <see cref="SongCredit.PrecedingSeparator"/>
    /// （空なら <paramref name="defaultSeparator"/>）を HTML エスケープして挟む。
    /// </summary>
    public static string BuildNamesHtml(
        IReadOnlyList<SongCredit> rows,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasMap,
        Func<SongCredit, string> personHtml,
        string defaultSeparator = "")
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var (i, companyOnly) in VisibleRows(rows, companyAliasMap))
        {
            var row = rows[i];
            if (!first)
                sb.Append(HtmlUtil.Escape(string.IsNullOrEmpty(row.PrecedingSeparator) ? defaultSeparator : row.PrecedingSeparator));
            first = false;
            if (companyOnly)
            {
                sb.Append(AffiliationInnerHtml(row, Name(row, companyAliasMap), companyAliasMap));
                continue;
            }
            sb.Append(personHtml(row));
            sb.Append(SuffixHtml(rows, i, companyAliasMap));
        }
        return sb.ToString();
    }

    /// <summary><see cref="BuildNamesHtml"/> の平文版。名義は <paramref name="personText"/> で出す。</summary>
    public static string BuildNamesText(
        IReadOnlyList<SongCredit> rows,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasMap,
        Func<SongCredit, string> personText,
        string defaultSeparator = "")
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var (i, companyOnly) in VisibleRows(rows, companyAliasMap))
        {
            var row = rows[i];
            if (!first) sb.Append(string.IsNullOrEmpty(row.PrecedingSeparator) ? defaultSeparator : row.PrecedingSeparator);
            first = false;
            if (companyOnly)
            {
                sb.Append(Name(row, companyAliasMap));
                continue;
            }
            sb.Append(personText(row)).Append(SuffixText(rows, i, companyAliasMap));
        }
        return sb.ToString();
    }
}
