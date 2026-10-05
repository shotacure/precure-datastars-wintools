using System.Text;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// クレジットで 1 行にまとめて表示する役職（<c>credit_card_roles.join_previous</c> / <c>join_separator</c>）の、
/// 組の解決とエントリ一致判定。まとめる役職どうしはデータ上同じエントリを持つ前提で、
/// 一致しないとき（打ち間違い）はまとめずに別々の行で出すために使う。
/// SiteBuilder のクレジット描画と Catalog のプレビューで同じ判定を共有する。
/// </summary>
public static class RoleJoinComparer
{
    /// <summary>
    /// 役職配下のブロック群から比較用のキーを作る。ブロックの区切り・カラム数・ブロック先頭企業と、
    /// エントリの並び・種別・参照先・表記（誤記・伏せ字を含む）をすべて含める。
    /// </summary>
    public static string BuildKey(IReadOnlyList<BlockSnapshot> blocks)
    {
        var sb = new StringBuilder();
        foreach (var bs in blocks)
        {
            sb.Append("|B").Append(bs.Block.ColCount).Append(':').Append(bs.Block.LeadingCompanyAliasId);
            foreach (var e in bs.Entries)
            {
                sb.Append("|E").Append(e.EntryKind)
                  .Append(':').Append(e.PersonAliasId)
                  .Append(':').Append(e.CharacterAliasId)
                  .Append(':').Append(e.RawCharacterText)
                  .Append(':').Append(e.CompanyAliasId)
                  .Append(':').Append(e.LogoId)
                  .Append(':').Append(e.RawText)
                  .Append(':').Append(e.AffiliationCompanyAliasId)
                  .Append(':').Append(e.AffiliationPersonAliasId)
                  .Append(':').Append(e.AffiliationText)
                  .Append(':').Append(e.PersonMisprintText)
                  .Append(':').Append(e.PersonMaskedText)
                  .Append(':').Append(e.CharacterMisprintText)
                  .Append(':').Append(e.CompanyMisprintText);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Group 内の役職列（表示順）から、1 行にまとめる組を解決する。
    /// 役職の直後に <c>join_previous</c> の役職が続いていれば、その役職を先頭に後続をまとめる。
    /// </summary>
    /// <param name="roles">Group 内の役職（表示順）。各要素は (直前とまとめるか, 配下のブロック)。</param>
    /// <returns>
    /// まとめる組の一覧（先頭の位置・後続の数）と、エントリが一致せずまとめなかった組の一覧（先頭の位置・後続の数）。
    /// </returns>
    public static (IReadOnlyList<(int LeadIndex, int FollowerCount)> Joins,
                   IReadOnlyList<(int LeadIndex, int FollowerCount)> Mismatches)
        Resolve(IReadOnlyList<(bool JoinPrevious, IReadOnlyList<BlockSnapshot> Blocks)> roles)
    {
        var joins = new List<(int, int)>();
        var mismatches = new List<(int, int)>();
        for (int i = 0; i < roles.Count; i++)
        {
            int followers = 0;
            for (int j = i + 1; j < roles.Count && roles[j].JoinPrevious; j++) followers++;
            if (followers == 0) continue;

            string leadKey = BuildKey(roles[i].Blocks);
            bool allMatch = true;
            for (int j = i + 1; j <= i + followers; j++)
            {
                if (!string.Equals(BuildKey(roles[j].Blocks), leadKey, StringComparison.Ordinal)) { allMatch = false; break; }
            }
            if (allMatch) joins.Add((i, followers));
            else mismatches.Add((i, followers));
            i += followers;
        }
        return (joins, mismatches);
    }

    /// <summary>
    /// まとめた行の役職名の文字（プレーンテキスト）を組み立てる。先頭の役職の表記に、後続の役職ごとに
    /// 「区切り + 表記」をつなげる。<paramref name="parts"/> の先頭要素の区切りは使わない。
    /// </summary>
    /// <param name="parts">まとめる役職（表示順）の (画面の表記, 直前との区切り)。</param>
    public static string ComposeLabel(IReadOnlyList<(string Label, string? Separator)> parts)
    {
        var sb = new StringBuilder();
        for (int k = 0; k < parts.Count; k++)
        {
            if (k > 0) sb.Append(parts[k].Separator);
            sb.Append(parts[k].Label);
        }
        return sb.ToString();
    }
}
