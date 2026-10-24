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
///   <item>所属の会社名だけを出す行は、会社名のあとに実際の人物を小さい括弧（<c>staff-primary-name muted</c>）で添える
///     （「Nostalgic Orchestra（藤本 記子）」）。人物が 1 人に決まれば会社名の文字をその人物の詳細へリンクし、括弧の名前は平文。
///     同じ会社名だけの行が続いて人物が 2 人以上になるときは、会社名の文字は企業詳細へ、括弧の名前をそれぞれ人物の詳細へリンクする。</item>
/// </list>
/// 連名全体の組み立ては <see cref="BuildNamesHtml"/> / <see cref="BuildNamesText"/> に寄せ、盤のトラック・楽曲詳細・楽曲一覧のカード・
/// 主題歌の行・平文（meta description・OGP カード）がすべて同じ規則で出す。
/// 役職テンプレ（各話のクレジット画面の主題歌カード）は Data 層の <c>SongCreditsRepository.GetDisplayHtmlPartsAsync</c> が同じ規則
/// （所属は添えない。会社名だけの行は会社名義で、企業詳細へリンク）で組み、人物の括弧はテンプレの展開
/// （<c>RoleTemplateRenderer</c>）が画面の行のまとまりに合わせて添える。
/// </summary>
public static class SongCreditAffiliation
{
    /// <summary>所属の表示名。会社名義があればその名義、無ければ自由記述、どちらも無ければ空文字。</summary>
    public static string Name(SongCredit row, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        if (row.AffiliationCompanyAliasId is int a && companyAliasMap.TryGetValue(a, out var ca)) return ca.Name ?? "";
        return row.AffiliationText?.Trim() ?? "";
    }

    /// <summary>人物の名義の表示名（<see cref="PersonAlias.GetDisplayName"/>）。名義が引けなければ「[alias#ID]」。会社名だけの行の括弧に使う。</summary>
    public static string PersonDisplayName(IReadOnlyDictionary<int, PersonAlias> personAliasMap, int personAliasId)
        => personAliasMap.TryGetValue(personAliasId, out var alias) ? alias.GetDisplayName() : "[alias#" + personAliasId + "]";

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
        => VisibleRowsWithRuns(rows, companyAliasMap).Select(v => (v.Index, v.CompanyOnly)).ToList();

    /// <summary>
    /// <see cref="VisibleRows"/> に、会社名だけを出す行がまとめた行の位置（<c>Run</c>。続きの先頭から最後まで。会社名だけでない行は自分 1 つ）を添えたもの。
    /// 会社名のあとに添える人物の括弧を組むのに使う。
    /// </summary>
    public static IReadOnlyList<(int Index, bool CompanyOnly, IReadOnlyList<int> Run)> VisibleRowsWithRuns(IReadOnlyList<SongCredit> rows, IReadOnlyDictionary<int, CompanyAlias> companyAliasMap)
    {
        var result = new List<(int, bool, IReadOnlyList<int>)>(rows.Count);
        string? lastCompanyOnly = null;
        List<int>? currentRun = null;
        for (int i = 0; i < rows.Count; i++)
        {
            if (IsCompanyOnly(rows[i], companyAliasMap))
            {
                string name = Name(rows[i], companyAliasMap);
                if (string.Equals(lastCompanyOnly, name, StringComparison.Ordinal) && currentRun is not null)
                {
                    currentRun.Add(i);
                    continue;
                }
                currentRun = new List<int> { i };
                result.Add((i, true, currentRun));
                lastCompanyOnly = name;
            }
            else
            {
                result.Add((i, false, new[] { i }));
                lastCompanyOnly = null;
                currentRun = null;
            }
        }
        return result;
    }

    /// <summary>まとめた行の人物の並び（同じ名義は 1 回だけ。出てきた順）。</summary>
    private static IReadOnlyList<SongCredit> DistinctPersons(IReadOnlyList<SongCredit> rows, IReadOnlyList<int> run)
        => run.Select(i => rows[i]).GroupBy(r => r.PersonAliasId).Select(g => g.First()).ToList();

    /// <summary>
    /// 連名の HTML を組み立てる。名義は <paramref name="personHtml"/>（人物リンクと、別名義の本名義の括弧など呼び出し側の添え物）で出し、
    /// 所属は出し方に従って添えるか、会社名だけに置き換える。区切りは <see cref="SongCredit.PrecedingSeparator"/>
    /// （空なら <paramref name="defaultSeparator"/>）を HTML エスケープして挟む。
    /// 会社名だけの行は、会社名のあとに人物の括弧を添える。<paramref name="personName"/> は人物の名義の表示名、
    /// <paramref name="personLinkHtml"/> は人物の詳細へのリンクを任意の文字で組む（共有名義の添字付きリンクなども呼び出し側の規則に従う）。
    /// </summary>
    public static string BuildNamesHtml(
        IReadOnlyList<SongCredit> rows,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasMap,
        Func<SongCredit, string> personHtml,
        Func<SongCredit, string> personName,
        Func<SongCredit, string, string> personLinkHtml,
        string defaultSeparator = "")
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var (i, companyOnly, run) in VisibleRowsWithRuns(rows, companyAliasMap))
        {
            var row = rows[i];
            if (!first)
                sb.Append(HtmlUtil.Escape(string.IsNullOrEmpty(row.PrecedingSeparator) ? defaultSeparator : row.PrecedingSeparator));
            first = false;
            if (companyOnly)
            {
                string company = Name(row, companyAliasMap);
                var persons = DistinctPersons(rows, run);
                if (persons.Count == 1)
                {
                    // 人物が 1 人に決まるので、会社名の文字をその人物へリンクし、括弧の名前は平文。
                    sb.Append(personLinkHtml(persons[0], company));
                    sb.Append("<span class=\"staff-primary-name muted\">（").Append(HtmlUtil.Escape(personName(persons[0]))).Append("）</span>");
                }
                else
                {
                    // 人物が決まらないので、会社名の文字は企業詳細へ、括弧の名前をそれぞれ人物へリンクする。
                    sb.Append(AffiliationInnerHtml(row, company, companyAliasMap));
                    sb.Append("<span class=\"staff-primary-name muted\">（")
                      .Append(string.Join("・", persons.Select(p => personLinkHtml(p, personName(p)))))
                      .Append("）</span>");
                }
                continue;
            }
            sb.Append(personHtml(row));
            sb.Append(SuffixHtml(rows, i, companyAliasMap));
        }
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="BuildNamesHtml"/> の平文版。名義は <paramref name="personText"/> で出す。
    /// <paramref name="noteWrap"/> を渡すと、会社名だけの行に添える人物の括弧（クレジットに無い補足）をそれで包む
    /// （OGP カードで小さい字に描き分けるため。<see cref="Rendering.OgCardNote.Wrap"/>）。
    /// </summary>
    public static string BuildNamesText(
        IReadOnlyList<SongCredit> rows,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasMap,
        Func<SongCredit, string> personText,
        string defaultSeparator = "",
        Func<string, string>? noteWrap = null)
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var (i, companyOnly, run) in VisibleRowsWithRuns(rows, companyAliasMap))
        {
            var row = rows[i];
            if (!first) sb.Append(string.IsNullOrEmpty(row.PrecedingSeparator) ? defaultSeparator : row.PrecedingSeparator);
            first = false;
            if (companyOnly)
            {
                // 会社名のあとに実際の人物を括弧で添える（「Nostalgic Orchestra（藤本 記子）」）。
                string note = "（" + string.Join("・", DistinctPersons(rows, run).Select(personText)) + "）";
                sb.Append(Name(row, companyAliasMap)).Append(noteWrap is null ? note : noteWrap(note));
                continue;
            }
            sb.Append(personText(row)).Append(SuffixText(rows, i, companyAliasMap));
        }
        return sb.ToString();
    }
}
