using System.Text;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 作品詳細・エピソード詳細の主題歌 / 挿入歌カードで、歌と作家の行を「役職バッジ + 名義」の並びに組む。
/// <para>
/// 楽曲索引のカード・商品詳細のトラック行と同じ <c>.role-badge.role-badge-sm[data-role-code]</c> のバッジを使い、
/// 1 役職 = 1 グループ（<c>.staff-badge-group</c>）として <c>.staff-badges-row</c> の中に横に流す。
/// 並びは 歌 → コーラス → 台詞 → 語り → 作詞 → 作曲 → 編曲 → メドレー編曲。
/// </para>
/// <para>
/// 作詞・作曲・編曲・メドレー編曲は、隣り合う役職の名義 HTML が完全に一致するとき 1 つのグループにまとめ、
/// バッジを並べて名義を 1 回だけ出す（<c>[作詞][作曲] 藤本 記子</c>）。歌唱系の役職はまとめない。
/// </para>
/// <para>
/// バッジの文字は、作家の役職なら盤の役職の表記（<see cref="SongCredit.RoleLabelText"/>）→ 役職マスタの和名 → 既定の文字の順、
/// 歌唱系なら役職マスタの和名 → 既定の文字の順。リンク先は <see cref="PathUtil.SongRoleBadgeUrl"/>（作家は役職詳細、歌唱系は歌唱ページ）。
/// </para>
/// </summary>
public static class ThemeSongCreditBadges
{
    /// <summary>
    /// 主題歌 1 曲分のバッジ行の中身（<c>.staff-badge-group</c> の連なり）を返す。名義がすべて空なら空文字。
    /// 名義 HTML はエスケープ・リンク化済みのものを受け取り、そのまま埋め込む。
    /// </summary>
    public static string Build(
        IReadOnlyDictionary<string, Role> roleMap,
        IReadOnlyList<SongCredit> songCredits,
        string vocalistsHtml,
        string chorusHtml,
        string dialogueHtml,
        string spokenWordHtml,
        string lyricsHtml,
        string compositionHtml,
        string arrangementHtml,
        string medleyArrangementHtml)
    {
        var groups = new List<(List<(string Code, string Label)> Badges, string NamesHtml, bool Mergeable, string ExtraClass)>();

        void Add(string roleCode, string label, string namesHtml, bool mergeable, string extraClass = "")
        {
            if (string.IsNullOrEmpty(namesHtml)) return;
            if (mergeable
                && groups.Count > 0
                && groups[^1].Mergeable
                && string.Equals(groups[^1].NamesHtml, namesHtml, StringComparison.Ordinal))
            {
                groups[^1].Badges.Add((roleCode, label));
                return;
            }
            groups.Add((new List<(string, string)> { (roleCode, label) }, namesHtml, mergeable, extraClass));
        }

        string RoleName(string roleCode, string fallback)
            => roleMap.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa) ? role.NameJa : fallback;
        string WriterLabel(string roleCode, string fallback)
            => SongCreditRoles.LabelTextOf(songCredits, roleCode) ?? RoleName(roleCode, fallback);

        // 歌は主役級なので先頭に置き、名義を少し大きく太く出す（ts-credit-vocals）。
        Add(SongRecordingSingerRoles.Vocals, RoleName(SongRecordingSingerRoles.Vocals, "歌"), vocalistsHtml, mergeable: false, extraClass: " ts-credit-vocals");
        Add(SongRecordingSingerRoles.Chorus, RoleName(SongRecordingSingerRoles.Chorus, "コーラス"), chorusHtml, mergeable: false);
        Add(SongRecordingSingerRoles.Dialogue, RoleName(SongRecordingSingerRoles.Dialogue, "台詞"), dialogueHtml, mergeable: false);
        Add(SongRecordingSingerRoles.SpokenWord, RoleName(SongRecordingSingerRoles.SpokenWord, "語り"), spokenWordHtml, mergeable: false);
        Add(SongCreditRoles.Lyrics, WriterLabel(SongCreditRoles.Lyrics, "作詞"), lyricsHtml, mergeable: true);
        Add(SongCreditRoles.Composition, WriterLabel(SongCreditRoles.Composition, "作曲"), compositionHtml, mergeable: true);
        Add(SongCreditRoles.Arrangement, WriterLabel(SongCreditRoles.Arrangement, "編曲"), arrangementHtml, mergeable: true);
        Add(SongCreditRoles.MedleyArrangement, WriterLabel(SongCreditRoles.MedleyArrangement, "メドレー編曲"), medleyArrangementHtml, mergeable: true);

        if (groups.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            sb.Append("<span class=\"staff-badge-group").Append(g.ExtraClass).Append("\">");
            foreach (var (code, label) in g.Badges)
            {
                string href = PathUtil.SongRoleBadgeUrl(code);
                if (href.Length > 0)
                    sb.Append("<a class=\"role-badge role-badge-sm\" data-role-code=\"").Append(HtmlUtil.Escape(code))
                      .Append("\" href=\"").Append(HtmlUtil.Escape(href)).Append("\">")
                      .Append(HtmlUtil.Escape(label)).Append("</a>");
                else
                    sb.Append("<span class=\"role-badge role-badge-sm\" data-role-code=\"").Append(HtmlUtil.Escape(code))
                      .Append("\">").Append(HtmlUtil.Escape(label)).Append("</span>");
            }
            sb.Append("<span class=\"ts-credit-names\">").Append(g.NamesHtml).Append("</span>");
            sb.Append("</span>");
        }
        return sb.ToString();
    }
}
