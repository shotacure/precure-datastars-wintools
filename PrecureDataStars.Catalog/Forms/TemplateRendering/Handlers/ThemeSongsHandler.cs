using Dapper;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Catalog.Forms.TemplateRendering.Handlers;

/// <summary>
/// <c>{THEME_SONGS}</c> プレースホルダの専用ハンドラ（v1.2.0 工程 H 追加）。
/// <para>
/// クレジット側で楽曲を直接持つ運用を撤廃した代わりに、役職レベルでテンプレ展開時に
/// <c>episode_theme_songs</c> を JOIN して楽曲群を取得し、整形済み文字列を生成する。
/// 楽曲ごとのレイアウト（曲名・作詞・作曲・編曲・うた）は固定の整形ロジックで生成し、
/// 全曲を縦並び（既定）または横カラム並び（<c>columns=N</c> オプション）で結合する。
/// </para>
/// <para>
/// 既存スキーマ（<c>songs</c> + <c>song_recordings</c>）の構造に依存:
/// <list type="bullet">
///   <item><description><c>songs.title</c> ... 曲タイトル</description></item>
///   <item><description><c>songs.lyricist_name</c> ... 作詞名義（テキスト）</description></item>
///   <item><description><c>songs.composer_name</c> ... 作曲名義（テキスト）</description></item>
///   <item><description><c>songs.arranger_name</c> ... 編曲名義（テキスト）</description></item>
///   <item><description><c>song_recordings.singer_name</c> ... 歌唱者名義（テキスト）</description></item>
/// </list>
/// </para>
/// </summary>
internal static class ThemeSongsHandler
{
    /// <summary>
    /// <paramref name="episodeId"/> に対応する <c>episode_theme_songs</c> 行を引き、
    /// 楽曲ごとの「『曲名』 / 作詞:○○ / 作曲:○○ / 編曲:○○ / うた:○○」ブロックを生成、
    /// 縦並びまたは横カラム並びで結合した最終文字列を返す。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    /// <param name="episodeId">scope_kind=EPISODE のときの episode_id。null や 0 の場合は空文字を返す（シリーズ単位クレジットでは主題歌は出さない方針）。</param>
    /// <param name="kinds">取得する theme_kind の配列（指定順で並ぶ）。空または null の場合は OP/ED/INSERT 全部。</param>
    /// <param name="columns">横並びカラム数（既定 1=縦並び）。<c>columns=2</c> なら 2 曲を横並びにする。</param>
    public static async Task<string> RenderAsync(
        IConnectionFactory factory,
        int? episodeId,
        IReadOnlyList<string>? kinds,
        int columns,
        CancellationToken ct = default)
    {
        if (episodeId is null || episodeId.Value <= 0) return "";
        if (columns < 1) columns = 1;

        // kinds が未指定なら OP/ED/INSERT 全部を扱う（後方互換）。
        var effectiveKinds = (kinds is null || kinds.Count == 0)
            ? new[] { "OP", "ED", "INSERT" }
            : kinds.Where(k => k is "OP" or "ED" or "INSERT").Distinct().ToArray();
        if (effectiveKinds.Length == 0) return "";

        // episode_theme_songs を JOIN して必要情報を一括取得。
        // 並び順は kinds パラメータで指定された theme_kind 順序を尊重し、INSERT 内では
        // insert_seq 昇順、同位置に既定行と本放送限定行があれば既定行を先に。
        // FIELD() で theme_kind の指定順序を表現する。
        // theme_kind の絞り込みは IN 句 + Dapper の動的引数で安全にバインド。
        string fieldList = string.Join(",", effectiveKinds.Select(k => $"'{k}'"));
        string sql = $$"""
            SELECT
              s.title             AS SongTitle,
              s.lyricist_name     AS LyricistName,
              s.composer_name     AS ComposerName,
              s.arranger_name     AS ArrangerName,
              sr.singer_name      AS SingerName,
              sr.variant_label    AS VariantLabel,
              ets.theme_kind      AS ThemeKind,
              ets.insert_seq      AS InsertSeq,
              ets.is_broadcast_only AS IsBroadcastOnly
            FROM episode_theme_songs ets
            JOIN song_recordings sr ON sr.song_recording_id = ets.song_recording_id
            JOIN songs           s  ON s.song_id           = sr.song_id
            WHERE ets.episode_id = @episodeId
              AND ets.theme_kind IN @kinds
            ORDER BY
              FIELD(ets.theme_kind, {{fieldList}}),
              ets.insert_seq,
              ets.is_broadcast_only;
            """;

        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<ThemeSongRow>(new CommandDefinition(
            sql,
            new { episodeId = episodeId.Value, kinds = effectiveKinds },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0) return "";

        // 各曲のブロック文字列を作る
        var blocks = rows.Select(r => RenderSingleSongBlock(r)).ToList();

        // columns=1 → 縦に空行区切りで結合
        if (columns <= 1)
        {
            return string.Join("\n\n", blocks);
        }

        // columns>=2 → 行ごとに columns 件ずつ横並びにする（タブ区切りで簡易再現）
        // ツリー上の表示は構造確認用なので、見栄えの厳密性より「複数曲を視認できる」ことを優先。
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < blocks.Count; i += columns)
        {
            var slice = blocks.Skip(i).Take(columns).ToList();
            // 各ブロックを行単位に分割し、横並びで合成（最も行数の多いブロックに合わせる）
            var lines = slice.Select(b => b.Split('\n')).ToList();
            int maxLines = lines.Max(a => a.Length);
            for (int li = 0; li < maxLines; li++)
            {
                for (int ci = 0; ci < lines.Count; ci++)
                {
                    string cell = li < lines[ci].Length ? lines[ci][li] : "";
                    if (ci > 0) sb.Append("    ");
                    sb.Append(cell);
                }
                sb.Append('\n');
            }
            if (i + columns < blocks.Count) sb.Append('\n'); // 行間
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderSingleSongBlock(ThemeSongRow r)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('「');
        sb.Append(r.SongTitle ?? "(曲名未登録)");
        sb.Append('」');
        if (!string.IsNullOrEmpty(r.VariantLabel)) sb.Append($" [{r.VariantLabel}]");
        sb.Append('\n');
        if (!string.IsNullOrEmpty(r.LyricistName)) sb.Append($"作詞:{r.LyricistName}\n");
        if (!string.IsNullOrEmpty(r.ComposerName)) sb.Append($"作曲:{r.ComposerName}\n");
        if (!string.IsNullOrEmpty(r.ArrangerName)) sb.Append($"編曲:{r.ArrangerName}\n");
        if (!string.IsNullOrEmpty(r.SingerName))   sb.Append($"うた:{r.SingerName}\n");
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>JOIN 結果を受ける内部 DTO（Dapper マッピング用）。</summary>
    private sealed class ThemeSongRow
    {
        public string? SongTitle { get; set; }
        public string? LyricistName { get; set; }
        public string? ComposerName { get; set; }
        public string? ArrangerName { get; set; }
        public string? SingerName { get; set; }
        public string? VariantLabel { get; set; }
        public string? ThemeKind { get; set; }
        public byte? InsertSeq { get; set; }
        public byte IsBroadcastOnly { get; set; }
    }
}
