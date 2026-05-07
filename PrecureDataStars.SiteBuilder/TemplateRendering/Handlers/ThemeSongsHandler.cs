using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.TemplateRendering.Handlers;

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

        var rows = await FetchAsync(factory, episodeId, kinds, ct).ConfigureAwait(false);
        if (rows.Count == 0) return "";

        // 各曲のブロック文字列を作る
        var blocks = rows.Select(r => RenderSingleSongBlock(r)).ToList();

        // columns=1 → 縦に空行区切りで結合
        if (columns <= 1)
        {
            return string.Join("\n\n", blocks);
        }

        // columns>=2 → HTML テーブルで横並びにする（v1.2.0 工程 H-12 改修）。
        // 旧実装では半角スペース 4 個で「桁揃え風」に並べていたが、HTML レンダラ側で
        // 連続空白が折り畳まれるため曲ごとの列が食い込んで見えていた。本実装では <table> で
        // 確実に列を分離し、各セルには <br> で改行を保つ HTML を出力する。
        // テンプレ側で {THEME_SONGS:columns=2} と書けば、左右に OP / ED が完全に独立した
        // 列として並ぶ。プレビューレンダラは HTML 素通しなのでこのまま反映される。
        // セル間隔（padding-right）は 32px、上端揃え（vertical-align:top）。
        var sb = new System.Text.StringBuilder();
        sb.Append("<table style=\"border-collapse:collapse;margin:0;\">");
        for (int i = 0; i < blocks.Count; i += columns)
        {
            var slice = blocks.Skip(i).Take(columns).ToList();
            sb.Append("<tr>");
            foreach (var cell in slice)
            {
                // セル内の改行は <br>、& < > は念のためエスケープ。
                // v1.2.0 工程 H-14：改行コード（\r\n / \r / \n）を \n に正規化してから <br> へ。
                // RenderSingleSongBlock 自身は \n しか吐かないが、念のため将来的な安全のため正規化する。
                string normalized = cell.Replace("\r\n", "\n").Replace("\r", "\n");
                string cellHtml = System.Net.WebUtility.HtmlEncode(normalized).Replace("\n", "<br>");
                sb.Append("<td style=\"vertical-align:top;padding:0 32px 12px 0;\">");
                sb.Append(cellHtml);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>
    /// 楽曲行を SQL で取得する共通ロジック（v1.2.0 工程 H-16 で切り出し）。
    /// 旧 <c>{THEME_SONGS}</c> プレースホルダ用と新 <c>{#THEME_SONGS}</c> ループ用で共有する。
    /// <para>
    /// v1.2.3 追加：取得後に <c>song_credits</c> / <c>song_recording_singers</c> が存在する曲・録音は、
    /// 構造化クレジットを優先表示文字列に展開して <see cref="ThemeSongRow"/> の各クレジット列を上書きする。
    /// 既存のフリーテキスト列（songs.lyricist_name 等）はフォールバックとしてそのまま使われる。
    /// </para>
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    /// <param name="episodeId">対象エピソード ID。null や 0 なら空リスト返却。</param>
    /// <param name="kinds">theme_kind 絞り込み（OP/ED/INSERT、空なら全部）。指定順がそのまま並び順になる。</param>
    /// <returns>JOIN 結果の楽曲行リスト。</returns>
    internal static async Task<IReadOnlyList<ThemeSongRow>> FetchAsync(
        IConnectionFactory factory,
        int? episodeId,
        IReadOnlyList<string>? kinds,
        CancellationToken ct = default)
    {
        if (episodeId is null || episodeId.Value <= 0) return Array.Empty<ThemeSongRow>();

        // kinds が未指定なら OP/ED/INSERT 全部を扱う。
        var effectiveKinds = (kinds is null || kinds.Count == 0)
            ? new[] { "OP", "ED", "INSERT" }
            : kinds.Where(k => k is "OP" or "ED" or "INSERT").Distinct().ToArray();
        if (effectiveKinds.Length == 0) return Array.Empty<ThemeSongRow>();

        // episode_theme_songs を JOIN して必要情報を一括取得。
        // 並び順は kinds パラメータで指定された theme_kind 順序を尊重し、INSERT 内では insert_seq 昇順、
        // 同位置に既定行と本放送限定行があれば既定行を先に。FIELD() で theme_kind の指定順序を表現する。
        // v1.2.3：song_id / song_recording_id も取得して、後段の構造化クレジット解決に使う。
        string fieldList = string.Join(",", effectiveKinds.Select(k => $"'{k}'"));
        string sql = $$"""
            SELECT
              s.song_id           AS SongId,
              sr.song_recording_id AS SongRecordingId,
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

        // v1.2.3：構造化クレジット（song_credits / song_recording_singers）が存在する場合は
        // それを優先表示文字列に展開してフリーテキスト列を上書きする。
        // テンプレ展開は 1 エピソード当たり主題歌 2-4 件程度のため、行ごとの追加クエリで実用的に問題ない。
        var songCredits = new SongCreditsRepository(factory);
        var recordingSingers = new SongRecordingSingersRepository(factory);
        foreach (var r in rows)
        {
            if (r.SongId > 0)
            {
                string lyr = await songCredits.GetDisplayStringAsync(r.SongId, SongCreditRole.Lyricist, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lyr)) r.LyricistName = lyr;

                string cmp = await songCredits.GetDisplayStringAsync(r.SongId, SongCreditRole.Composer, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cmp)) r.ComposerName = cmp;

                string arr = await songCredits.GetDisplayStringAsync(r.SongId, SongCreditRole.Arranger, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(arr)) r.ArrangerName = arr;
            }
            if (r.SongRecordingId > 0)
            {
                string sing = await recordingSingers.GetDisplayStringAsync(r.SongRecordingId, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(sing)) r.SingerName = sing;
            }
        }

        return rows;
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

    /// <summary>
    /// JOIN 結果を受ける DTO（Dapper マッピング用、内部公開）。
    /// v1.2.0 工程 H-16 で internal 化：新 <c>{#THEME_SONGS}</c> ループ構文の Renderer から
    /// 楽曲スコープのプレースホルダ（{SONG_TITLE} 等）を解決するために、フィールドへ直接アクセスする必要がある。
    /// v1.2.3 で <see cref="SongId"/> と <see cref="SongRecordingId"/> を追加：
    /// 構造化クレジット（song_credits / song_recording_singers）の解決に使う。
    /// </summary>
    internal sealed class ThemeSongRow
    {
        /// <summary>親曲 ID（v1.2.3 追加、構造化作家クレジット参照に使用）。</summary>
        public int SongId { get; set; }
        /// <summary>録音 ID（v1.2.3 追加、構造化歌唱者クレジット参照に使用）。</summary>
        public int SongRecordingId { get; set; }
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
