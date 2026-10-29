using System.Globalization;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 人物・企業の詳細ページの脚注（出典）1 件（<see cref="EntitySource"/> の表示用）。
/// 部分テンプレ <c>_entity-sources.sbn</c> がページの一番下に番号つきで並べ、
/// ページタイトル・見出し・発行者・URL・確認日を出す。本文の項目には <see cref="EntitySourceRefs"/> の番号を付ける。
/// </summary>
public sealed class EntitySourceView
{
    /// <summary>脚注の番号（1 から。並び順どおり）。</summary>
    public int Number { get; init; }
    /// <summary>脚注の飛び先の id（「fn-1」）。</summary>
    public string Anchor => "fn-" + Number.ToString(CultureInfo.InvariantCulture);
    /// <summary>ページタイトル（&lt;title&gt; のまま）。</summary>
    public string PageTitle { get; init; } = "";
    /// <summary>情報が載っている欄の見出し（無ければ空文字）。</summary>
    public string SectionTitle { get; init; } = "";
    /// <summary>発行者（著作権表記の名前どおり）。</summary>
    public string Publisher { get; init; } = "";
    /// <summary>開いたページの URL。</summary>
    public string Url { get; init; } = "";
    /// <summary>確認日（「2026年10月25日」）。</summary>
    public string CheckedOn { get; init; } = "";
}

/// <summary>
/// 本文の項目に付ける脚注の番号の HTML（「<sup class="fn-ref"><a href="#fn-1">[1]</a></sup>」）。
/// 項目を裏付ける出典が無ければ空文字。同じ項目を複数の出典が裏付けるときは番号を並べる。
/// </summary>
public sealed class EntitySourceRefs
{
    public static readonly EntitySourceRefs Empty = new();

    /// <summary>読み（人物・企業）。</summary>
    public string Kana { get; init; } = "";
    /// <summary>誕生日（人物）。</summary>
    public string Birth { get; init; } = "";
    /// <summary>没年（人物）。</summary>
    public string Death { get; init; } = "";
    /// <summary>設立（企業）。</summary>
    public string Founded { get; init; } = "";
    /// <summary>解散（企業）。</summary>
    public string Dissolved { get; init; } = "";
    /// <summary>メンバー（企業。「所属としてクレジットされた方々」の見出し）。</summary>
    public string Members { get; init; } = "";
}

/// <summary>人物・企業ごとの脚注の並びと、本文に付ける番号のまとまり。</summary>
public sealed class EntitySourceFootnotes
{
    public static readonly EntitySourceFootnotes Empty = new();

    /// <summary>脚注（番号順）。空なら脚注の欄を出さない。</summary>
    public IReadOnlyList<EntitySourceView> Notes { get; init; } = Array.Empty<EntitySourceView>();
    /// <summary>本文の項目に付ける番号。</summary>
    public EntitySourceRefs Refs { get; init; } = EntitySourceRefs.Empty;

    /// <summary>1 つの人物・企業の出典（並び順どおり）から、脚注と本文の番号を作る。</summary>
    public static EntitySourceFootnotes Build(IReadOnlyList<EntitySource> sources)
    {
        if (sources.Count == 0) return Empty;
        var notes = new List<EntitySourceView>(sources.Count);
        var numbersByField = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            int number = i + 1;
            notes.Add(new EntitySourceView
            {
                Number = number,
                PageTitle = s.PageTitle,
                SectionTitle = s.SectionTitle?.Trim() ?? "",
                Publisher = s.Publisher.Trim(),
                Url = s.Url.Trim(),
                CheckedOn = s.CheckedOn.ToString("yyyy年M月d日", CultureInfo.InvariantCulture)
            });
            foreach (var code in s.SupportCodes())
            {
                if (!numbersByField.TryGetValue(code, out var list)) numbersByField[code] = list = new List<int>();
                list.Add(number);
            }
        }

        string RefHtml(string code) => numbersByField.TryGetValue(code, out var numbers)
            ? "<sup class=\"fn-ref\">" + string.Concat(numbers.Select(n =>
                $"<a href=\"#fn-{n.ToString(CultureInfo.InvariantCulture)}\">[{n.ToString(CultureInfo.InvariantCulture)}]</a>")) + "</sup>"
            : "";

        return new EntitySourceFootnotes
        {
            Notes = notes,
            Refs = new EntitySourceRefs
            {
                Kana = RefHtml(EntitySourceFields.Kana),
                Birth = RefHtml(EntitySourceFields.Birth),
                Death = RefHtml(EntitySourceFields.Death),
                Founded = RefHtml(EntitySourceFields.Founded),
                Dissolved = RefHtml(EntitySourceFields.Dissolved),
                Members = RefHtml(EntitySourceFields.Members),
            }
        };
    }

    /// <summary>全件を人物ごとの脚注にする。</summary>
    public static IReadOnlyDictionary<int, EntitySourceFootnotes> ByPerson(IEnumerable<EntitySource> all)
        => all.Where(s => s.PersonId.HasValue)
              .GroupBy(s => s.PersonId!.Value)
              .ToDictionary(g => g.Key, g => Build(g.ToList()));

    /// <summary>全件を企業ごとの脚注にする。</summary>
    public static IReadOnlyDictionary<int, EntitySourceFootnotes> ByCompany(IEnumerable<EntitySource> all)
        => all.Where(s => s.CompanyId.HasValue)
              .GroupBy(s => s.CompanyId!.Value)
              .ToDictionary(g => g.Key, g => Build(g.ToList()));
}
