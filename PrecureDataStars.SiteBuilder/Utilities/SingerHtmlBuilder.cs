using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 録音の歌唱者群（<see cref="SongRecordingSinger"/>）と歌系役職ラベルをリンク付き HTML へ整形する共通ビルダ。
/// EpisodeGenerator / SongsGenerator / ThemeSongRowBuilder が同一実装を private ヘルパとして
/// 重複保持していたため一本化した。
/// 依存は不変の <see cref="StaffNameLinkResolver"/> / <see cref="RoleSuccessorResolver"/> と
/// 引数で渡される辞書のみで、並列レンダリングフェーズから複数スレッドで同時に呼んでも安全。
/// </summary>
public sealed class SingerHtmlBuilder
{
    // 同 alias を複数人物が共有するケースの添字付きリンク化に必要（人物リンクの正規経路）。
    private readonly StaffNameLinkResolver _staffLinkResolver;
    // 役職コード → 統計ページ用の代表 role_code 解決（/creators/roles/{rep}/）。
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    public SingerHtmlBuilder(StaffNameLinkResolver staffLinkResolver, RoleSuccessorResolver roleSuccessorResolver)
    {
        _staffLinkResolver = staffLinkResolver;
        _roleSuccessorResolver = roleSuccessorResolver;
    }

    /// <summary>
    /// 録音の歌唱者群（<see cref="SongRecordingSinger"/>）を HTML 化する。
    /// 仕様：
    /// <list type="bullet">
    ///   <item>VOCALS 役の行を <see cref="SongRecordingSinger.SingerSeq"/> 順に並べ、
    ///     PERSON 名義は /persons/{id}/、CHARACTER_WITH_CV 名義はキャラ /characters/{id}/ ＋
    ///     CV 名義 /persons/{id}/ で構成する「キャラ名(CV:声優)」形式で出す。</item>
    ///   <item>スラッシュ並列（<see cref="SongRecordingSinger.SlashCharacterAliasId"/> 等）は
    ///     主名義側と同じ書式で「/」連結して出す。</item>
    ///   <item><see cref="SongRecordingSinger.AffiliationText"/> が非空なら末尾に半角スペース＋テキスト平文で添える。</item>
    ///   <item>行が 1 件も無ければフォールバックとして <paramref name="fallbackSingerName"/>
    ///     （<see cref="SongRecording.SingerName"/> のフリーテキスト）の HTML エスケープ平文を返す。</item>
    /// </list>
    /// </summary>
    public string BuildVocalistsHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string? fallbackSingerName,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        string html = BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Vocals, personAliasMap, characterAliasMap);
        if (!string.IsNullOrEmpty(html)) return html;
        return string.IsNullOrEmpty(fallbackSingerName) ? "" : HtmlUtil.Escape(fallbackSingerName);
    }

    /// <summary>BACKING_VOCALS（コーラス）役の歌唱者連名 HTML を返す。 該当行が無ければ空文字列（VOCALS と違いフリーテキストのフォールバックは無い）。</summary>
    public string BuildChorusHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
        => BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Chorus, personAliasMap, characterAliasMap);

    /// <summary>役職ラベルを <c>/creators/roles/{rep_role_code}/</c> リンク付き HTML に整形する。 役職マスタに未登録（または和名が空）のときは <paramref name="fallbackLabel"/> のエスケープ平文を返す。</summary>
    public string BuildSongRoleLabelLinkHtml(string roleCode, IReadOnlyDictionary<string, Role> roleMap, string fallbackLabel)
    {
        if (roleMap.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(roleCode);
            string href = PathUtil.CreatorsRoleUrl(string.IsNullOrEmpty(rep) ? roleCode : rep);
            return $"<a href=\"{HtmlUtil.Escape(href)}\">{HtmlUtil.Escape(role.NameJa)}</a>";
        }
        return HtmlUtil.Escape(fallbackLabel);
    }

    /// <summary>指定 <paramref name="roleCode"/>（VOCALS / BACKING_VOCALS 等）の歌唱者行のみを抽出し連名 HTML を組み立てる内部ヘルパ。</summary>
    private string BuildSingersByRoleHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string roleCode,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var rows = singers
            .Where(s => string.Equals(s.RoleCode, roleCode, StringComparison.Ordinal))
            .OrderBy(s => s.SingerSeq)
            .ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            if (i > 0)
            {
                // 区切り文字も HTML エスケープしてから出力する。
                sb.Append(HtmlUtil.Escape(s.PrecedingSeparator ?? ""));
            }
            sb.Append(RenderSingerEntry(s, personAliasMap, characterAliasMap));
            if (!string.IsNullOrEmpty(s.AffiliationText))
            {
                sb.Append(' ').Append(HtmlUtil.Escape(s.AffiliationText));
            }
        }
        return sb.ToString();
    }

    /// <summary>1 つの歌唱者行（主名義 + 任意でスラッシュ並列の相方）を HTML に整形する。</summary>
    private string RenderSingerEntry(
        SongRecordingSinger s,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (s.BillingKind == SingerBillingKind.Person)
        {
            // PERSON：主名義 + （あれば）スラッシュ並列の相方。両方とも person_alias。
            string main = ResolvePersonAliasLink(s.PersonAliasId, personAliasMap);
            if (s.SlashPersonAliasId.HasValue)
            {
                string slash = ResolvePersonAliasLink(s.SlashPersonAliasId, personAliasMap);
                return $"{main} / {slash}";
            }
            return main;
        }
        else
        {
            // CHARACTER_WITH_CV：「キャラ(CV:声優)」、相方ありなら「キャラ/相方キャラ(CV:声優)」。
            string mainChar = ResolveCharacterAliasLink(s.CharacterAliasId, characterAliasMap);
            string charPart = mainChar;
            if (s.SlashCharacterAliasId.HasValue)
            {
                string slashChar = ResolveCharacterAliasLink(s.SlashCharacterAliasId, characterAliasMap);
                charPart = $"{mainChar}/{slashChar}";
            }
            string cv = ResolvePersonAliasLink(s.VoicePersonAliasId, personAliasMap);
            return $"{charPart}(CV:{cv})";
        }
    }

    private string ResolvePersonAliasLink(int? aliasId, IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!personAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[alias#{aliasId.Value}]";
        return _staffLinkResolver.ResolveAsHtml(aliasId, alias.GetDisplayName());
    }

    private static string ResolveCharacterAliasLink(int? aliasId, IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!characterAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[char-alias#{aliasId.Value}]";
        // キャラ詳細ページへの単一リンク。複数キャラを束ねる仕組（StaffNameLinkResolver 相当）は
        // character_aliases が CharacterId を直接持つため不要。
        // CharacterAlias は PersonAlias と違い DisplayTextOverride / GetDisplayName() を持たない
        // （表記揺れごとに別 alias 行を並存させる運用のため、表示テキストは常に Name そのもの）。
        return $"<a href=\"/characters/{alias.CharacterId}/\">{HtmlUtil.Escape(alias.Name)}</a>";
    }
}
