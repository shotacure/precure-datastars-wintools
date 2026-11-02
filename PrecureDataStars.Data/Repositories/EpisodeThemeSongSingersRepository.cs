using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// episode_theme_song_singers テーブル（エピソードのクレジットの主題歌のカードに出た歌唱者）の読み取りリポジトリ。
/// 主題歌のカードの歌唱者が、使った録音の歌唱者（song_recording_singers）と違うときに、画面に出た歌唱者を
/// 名義でつないで持つ。行の形は song_recording_singers と同じで、episode_theme_songs の 1 行
/// （episode_id, is_broadcast_only, theme_kind, seq）に連なる。
/// 表示の組み立ては <see cref="SongRecordingSingersRepository"/> と共有する（「キャラ(CV:声優)」・スラッシュ・所属・区切り）。
/// </summary>
public sealed class EpisodeThemeSongSingersRepository : RepositoryBase
{
    public EpisodeThemeSongSingersRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 指定の主題歌の行に、クレジットに出た歌唱者の行が 1 つでもあるかを返す。
    /// あれば、その主題歌のクレジットのカードは歌・コーラスとも録音の歌唱者ではなくこの表の行で描く。
    /// </summary>
    public async Task<bool> ExistsAsync(int episodeId, byte isBroadcastOnly, string themeKind, byte seq, CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1 FROM episode_theme_song_singers
               WHERE episode_id = @episodeId AND is_broadcast_only = @isBroadcastOnly
                 AND theme_kind = @themeKind AND seq = @seq);
            """;
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql, new { episodeId, isBroadcastOnly, themeKind, seq }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定の主題歌の行・役職の歌唱者を、歌唱者ごとの部品（直前との区切り, リンク化済み HTML）の並びで返す。
    /// 行が無ければ空の並び。<paramref name="cvLabelOverride"/> があれば、声優の前の記号を行の記号（cv_label）に代えてその文字にする。
    /// </summary>
    public async Task<IReadOnlyList<(string Sep, string Html)>> GetDisplayHtmlPartsAsync(
        int episodeId,
        byte isBroadcastOnly,
        string themeKind,
        byte seq,
        string roleCode,
        ILookupCache lookup,
        string? cvLabelOverride = null,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.singer_seq               AS Seq,
              s.billing_kind             AS Kind,
              s.preceding_separator      AS Sep,
              s.affiliation_text         AS Aff,
              s.cv_label                 AS CvLabel,
              s.person_alias_id          AS PersonAliasId,
              s.character_alias_id       AS CharacterAliasId,
              s.voice_person_alias_id    AS VoicePersonAliasId,
              s.slash_person_alias_id    AS SlashPersonAliasId,
              s.slash_character_alias_id AS SlashCharacterAliasId,
              s.slash_voice_person_alias_id AS SlashVoicePersonAliasId
            FROM episode_theme_song_singers s
            WHERE s.episode_id        = @episodeId
              AND s.is_broadcast_only = @isBroadcastOnly
              AND s.theme_kind        = @themeKind
              AND s.seq               = @seq
              AND s.role_code         = @roleCode
            ORDER BY s.singer_seq;
            """;
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<SongRecordingSingersRepository.HtmlRow>(new CommandDefinition(
            sql, new { episodeId, isBroadcastOnly, themeKind, seq, roleCode }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        return await SongRecordingSingersRepository.BuildHtmlPartsAsync(rows, lookup, cvLabelOverride).ConfigureAwait(false);
    }

    /// <summary>部品の並びを、直前との区切り（HtmlEncode する）でつないだ連名 HTML にする。</summary>
    public static string JoinHtmlParts(IReadOnlyList<(string Sep, string Html)> parts)
        => SongRecordingSingersRepository.JoinHtmlParts(parts);
}
