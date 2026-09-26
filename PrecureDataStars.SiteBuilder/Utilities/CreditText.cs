using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 歌・録音・劇伴の名義を「リンクを含まない平文」に解決する共通ヘルパ。
/// meta description / OGP カード / JSON-LD / 使用曲リストの副題など、HTML リンクを置けない
/// 出力先で使う。どのメソッドも「構造化行（song_credits / song_recording_singers / bgm_cue_credits）が
/// あればそれだけを使い、1 行も無いときに限ってフリーテキスト列へフォールバックする」規則で統一する。
/// 平文の書式は HTML 版（<see cref="SingerHtmlBuilder"/> 等）の表示テキストと同一
/// （名義は <see cref="PersonAlias.GetDisplayName"/>、キャラ歌唱は「キャラ(CV:声優)」、
/// スラッシュ並列は「/」連結、区切りは <c>preceding_separator</c>）。
/// 状態を持たない純粋関数のみで、並列レンダリングフェーズから呼んでも安全。
/// </summary>
public static class CreditText
{
    /// <summary>
    /// 歌の指定役職（作詞 / 作曲 / 編曲）の名義を、<c>preceding_separator</c> で連結した平文で返す。
    /// 構造化行が無ければ <paramref name="fallbackText"/>（songs の *_name 列）を返す。
    /// </summary>
    public static string SongCreditNames(
        IReadOnlyList<SongCredit>? credits,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var rows = SongCreditRows(credits, roleCode);
        if (rows.Count == 0) return fallbackText?.Trim() ?? "";
        return JoinNames(rows.Select(r => (r.PrecedingSeparator, PersonName(r.PersonAliasId, personAliasMap))));
    }

    /// <summary>
    /// 歌の指定役職の名義を 1 名義 1 要素のリストで返す（JSON-LD の Person ノード列用）。
    /// 構造化行が無ければ、空でないフリーテキストを 1 要素として返す。
    /// </summary>
    public static IReadOnlyList<string> SongCreditNameList(
        IReadOnlyList<SongCredit>? credits,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var rows = SongCreditRows(credits, roleCode);
        if (rows.Count > 0)
        {
            return rows.Select(r => PersonName(r.PersonAliasId, personAliasMap))
                .Where(n => n.Length > 0)
                .ToList();
        }
        string fb = fallbackText?.Trim() ?? "";
        return fb.Length == 0 ? Array.Empty<string>() : new[] { fb };
    }

    /// <summary>
    /// 録音の歌唱者（VOCALS 役）を平文で返す。構造化行が無ければ <paramref name="fallbackText"/>
    /// （song_recordings.singer_name）を返す。書式は <see cref="SingerHtmlBuilder.BuildVocalistsHtml"/> の表示テキストと同じ。
    /// </summary>
    public static string Vocalists(
        IReadOnlyList<SongRecordingSinger>? singers,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var rows = (singers ?? Array.Empty<SongRecordingSinger>())
            .Where(s => string.Equals(s.RoleCode, SongRecordingSingerRoles.Vocals, StringComparison.Ordinal))
            .OrderBy(s => s.SingerSeq)
            .ToList();
        if (rows.Count == 0) return fallbackText?.Trim() ?? "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            if (i > 0) sb.Append(s.PrecedingSeparator ?? "");
            if (s.BillingKind == SingerBillingKind.Person)
            {
                sb.Append(PersonName(s.PersonAliasId, personAliasMap));
                if (s.SlashPersonAliasId.HasValue)
                    sb.Append(" / ").Append(PersonName(s.SlashPersonAliasId, personAliasMap));
            }
            else
            {
                sb.Append(CharacterName(s.CharacterAliasId, characterAliasMap));
                if (s.SlashCharacterAliasId.HasValue)
                    sb.Append('/').Append(CharacterName(s.SlashCharacterAliasId, characterAliasMap));
                sb.Append("(CV:").Append(PersonName(s.VoicePersonAliasId, personAliasMap)).Append(')');
            }
            if (!string.IsNullOrEmpty(s.AffiliationText)) sb.Append(' ').Append(s.AffiliationText);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 劇伴 cue の指定役職（作曲 / 編曲）の名義を、<c>preceding_separator</c> で連結した平文で返す。
    /// 構造化行が無ければ <paramref name="fallbackText"/>（bgm_cues の *_name 列）を返す。
    /// </summary>
    public static string BgmCueCreditNames(
        IReadOnlyList<BgmCueCredit>? credits,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var rows = (credits ?? Array.Empty<BgmCueCredit>())
            .Where(c => string.Equals(c.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(c => c.CreditSeq)
            .ToList();
        if (rows.Count == 0) return fallbackText?.Trim() ?? "";
        return JoinNames(rows.Select(r => (r.PrecedingSeparator, PersonName(r.PersonAliasId, personAliasMap))));
    }

    private static List<SongCredit> SongCreditRows(IReadOnlyList<SongCredit>? credits, string roleCode)
        => (credits ?? Array.Empty<SongCredit>())
            .Where(c => string.Equals(c.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(c => c.CreditSeq)
            .ToList();

    /// <summary>(区切り, 名義) の列を連結する。先頭要素の区切りは使わない。</summary>
    private static string JoinNames(IEnumerable<(string? Separator, string Name)> items)
    {
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var (sep, name) in items)
        {
            if (!first) sb.Append(sep ?? "");
            sb.Append(name);
            first = false;
        }
        return sb.ToString();
    }

    private static string PersonName(int? aliasId, IReadOnlyDictionary<int, PersonAlias> personAliasMap)
        => aliasId is int id && personAliasMap.TryGetValue(id, out var a) ? a.GetDisplayName() : "";

    private static string CharacterName(int? aliasId, IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
        => aliasId is int id && characterAliasMap.TryGetValue(id, out var a) ? a.Name : "";
}
