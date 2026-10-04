using System.Text;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// クレジットで 1 行にまとめて表示する役職（<c>credit_card_roles.joined_label</c> / <c>join_previous</c>）の、
/// エントリ一致判定。まとめる役職どうしはデータ上同じエントリを持つ前提で、
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
    /// <c>joined_label</c> を持つ役職を先頭に、直後に続く <c>join_previous</c> の役職をまとめる。
    /// 先頭が <c>joined_label</c> を持たない <c>join_previous</c> の役職はまとめない。
    /// </summary>
    /// <param name="roles">Group 内の役職（表示順）。各要素は (まとめた行の文字, 直前とまとめるか, 配下のブロック)。</param>
    /// <returns>
    /// まとめる組の一覧（先頭の位置・後続の数・まとめた行の文字）と、
    /// エントリが一致せずまとめなかった組の一覧（先頭の位置・まとめた行の文字）。
    /// </returns>
    public static (IReadOnlyList<(int LeadIndex, int FollowerCount, string Label)> Joins,
                   IReadOnlyList<(int LeadIndex, string Label)> Mismatches)
        Resolve(IReadOnlyList<(string? JoinedLabel, bool JoinPrevious, IReadOnlyList<BlockSnapshot> Blocks)> roles)
    {
        var joins = new List<(int, int, string)>();
        var mismatches = new List<(int, string)>();
        for (int i = 0; i < roles.Count; i++)
        {
            string? label = roles[i].JoinedLabel;
            if (string.IsNullOrWhiteSpace(label)) continue;

            int followers = 0;
            for (int j = i + 1; j < roles.Count && roles[j].JoinPrevious; j++) followers++;
            if (followers == 0) continue;

            string leadKey = BuildKey(roles[i].Blocks);
            bool allMatch = true;
            for (int j = i + 1; j <= i + followers; j++)
            {
                if (!string.Equals(BuildKey(roles[j].Blocks), leadKey, StringComparison.Ordinal)) { allMatch = false; break; }
            }
            if (allMatch) joins.Add((i, followers, label!));
            else mismatches.Add((i, label!));
            i += followers;
        }
        return (joins, mismatches);
    }
}
