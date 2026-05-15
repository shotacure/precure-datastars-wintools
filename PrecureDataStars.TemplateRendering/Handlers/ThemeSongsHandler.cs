using Dapper;
using PrecureDataStars.Data;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.TemplateRendering.Handlers;

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
/// <para>
/// v1.3.0 続編：クレジット展開での主題歌・挿入歌情報のリンク化要件に対応。
/// 曲タイトルは <c>/songs/{song_id}/</c> へのリンクに変換、作詞・作曲・編曲・うた等の
/// テキスト由来部分は HTML エスケープのみ（人物リンク化は CreditTreeRenderer の役職別レイアウトで
/// 別途行う前提で、本ハンドラは「楽曲詳細へ誘導するリンク + プレーンテキストの作家情報」の責務に絞る）。
/// 出力は HTML 文字列で、CreditTreeRenderer 側で素通しされる（HtmlEncode は通らない経路）。
/// </para>
/// </summary>
public static class ThemeSongsHandler
{
    /// <summary>
    /// <paramref name="episodeId"/> に対応する <c>episode_theme_songs</c> 行を引き、
    /// 楽曲ごとの「『曲名』 / 作詞:○○ / 作曲:○○ / 編曲:○○ / うた:○○」ブロックを生成、
    /// 縦並びまたは横カラム並びで結合した最終文字列を返す。
    /// <para>
    /// v1.3.1 stage B-4 で <paramref name="lookup"/> を追加。各構造化クレジット由来テキストを
    /// リポジトリ層の HTML 版経由でリンク化済み HTML 断片として取得するために、
    /// <see cref="ILookupCache"/> を引き回す経路に切り替えた。役職ラベル（作詞・作曲・編曲・うた）も
    /// <see cref="ILookupCache.LookupRoleHtmlAsync"/> で役職統計ページへのリンク付きラベルに展開する。
    /// </para>
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    /// <param name="episodeId">scope_kind=EPISODE のときの episode_id。null や 0 の場合は空文字を返す（シリーズ単位クレジットでは主題歌は出さない方針）。</param>
    /// <param name="kinds">取得する theme_kind の配列（指定順で並ぶ）。空または null の場合は OP/ED/INSERT 全部。</param>
    /// <param name="columns">横並びカラム数（既定 1=縦並び）。<c>columns=2</c> なら 2 曲を横並びにする。</param>
    /// <param name="lookup">名義・役職 ID をリンク化済み HTML へ解決するインターフェース（v1.3.1 stage B-4 追加）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public static async Task<string> RenderAsync(
        IConnectionFactory factory,
        int? episodeId,
        IReadOnlyList<string>? kinds,
        int columns,
        ILookupCache lookup,
        CancellationToken ct = default)
    {
        if (episodeId is null || episodeId.Value <= 0) return "";
        if (columns < 1) columns = 1;

        var rows = await FetchAsync(factory, episodeId, kinds, lookup, ct).ConfigureAwait(false);
        if (rows.Count == 0) return "";

        // v1.3.1 stage B-4：各曲のブロック文字列を HTML 出力モードで作る。
        // 曲タイトルは <a href="/songs/{song_id}/">、変種ラベルは HTML エスケープ済み、
        // 作詞・作曲・編曲・うたの各行はリポジトリ層の HTML 版で人物・キャラリンク付きの HTML に展開済み。
        var blocks = rows.Select(r => RenderSingleSongBlockHtml(r, lookup)).ToList();
        // RenderSingleSongBlockHtml が async になったため、Task<string> のリストになる。
        // 1 エピソードあたり主題歌は最大数曲のため、シーケンシャル await で十分。
        var resolved = new List<string>(rows.Count);
        foreach (var task in blocks) resolved.Add(await task.ConfigureAwait(false));

        // columns=1 → 縦に空行区切りで結合
        if (columns <= 1)
        {
            return string.Join("\n\n", resolved);
        }

        // columns>=2 → HTML テーブルで横並びにする（v1.2.0 工程 H-12 改修）。
        // v1.3.0 続編：セル内容が既に HTML（<a> タグ含む）なので HtmlEncode は通さず素通し。
        // 改行（\n）だけは <br> に置換する。
        var sb = new System.Text.StringBuilder();
        sb.Append("<table style=\"border-collapse:collapse;margin:0;\">");
        for (int i = 0; i < resolved.Count; i += columns)
        {
            var slice = resolved.Skip(i).Take(columns).ToList();
            sb.Append("<tr>");
            foreach (var cell in slice)
            {
                // セル内の改行は <br>。RenderSingleSongBlockHtml は \n しか吐かないが、念のため正規化。
                string normalized = cell.Replace("\r\n", "\n").Replace("\r", "\n");
                string cellHtml = normalized.Replace("\n", "<br>");
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
    /// <para>
    /// v1.3.1 stage B-4：HTML 出力経路への切替えに伴い、<paramref name="lookup"/> を経由して
    /// 構造化クレジット由来の情報をリンク化済み HTML 断片として <see cref="ThemeSongRow.LyricistHtml"/>
    /// 等の HTML 用フィールドに詰める。構造化が無い曲・録音はフリーテキスト列（<see cref="ThemeSongRow.LyricistName"/>
    /// 等）を HtmlEncode した平文を *Html フィールドに入れる（リンクなしフォールバック）。
    /// </para>
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    /// <param name="episodeId">対象エピソード ID。null や 0 なら空リスト返却。</param>
    /// <param name="kinds">theme_kind 絞り込み（OP/ED/INSERT、空なら全部）。指定順がそのまま並び順になる。</param>
    /// <param name="lookup">名義・キャラ ID → リンク化 HTML 解決インターフェース（v1.3.1 stage B-4 追加）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>JOIN 結果の楽曲行リスト（*Html フィールド設定済み）。</returns>
    internal static async Task<IReadOnlyList<ThemeSongRow>> FetchAsync(
        IConnectionFactory factory,
        int? episodeId,
        IReadOnlyList<string>? kinds,
        ILookupCache lookup,
        CancellationToken ct = default)
    {
        if (episodeId is null || episodeId.Value <= 0) return Array.Empty<ThemeSongRow>();

        // kinds が未指定なら OP/ED/INSERT 全部を扱う。
        var effectiveKinds = (kinds is null || kinds.Count == 0)
            ? new[] { "OP", "ED", "INSERT" }
            : kinds.Where(k => k is "OP" or "ED" or "INSERT").Distinct().ToArray();
        if (effectiveKinds.Length == 0) return Array.Empty<ThemeSongRow>();

        // episode_theme_songs を JOIN して必要情報を一括取得。
        // v1.3.0：旧 insert_seq 列を seq にリネームし、値の意味を「劇中で流れた順
        // （OP/INSERT/ED 区別なくエピソード単位の劇中順）」に統一。並び順も ets.seq
        // 単独でソートし、kinds パラメータはフィルタとしてのみ使用する。同位置に
        // 既定行と本放送限定行があれば既定行（is_broadcast_only=0）を先に。
        // v1.2.3：song_id / song_recording_id も取得して、後段の構造化クレジット解決に使う。
        // v1.3.0 ブラッシュアップ続編：usage_actuality 列を取得し、
        //   - 'BROADCAST_NOT_CREDITED' はクレジット側に出さない方針なので WHERE で除外
        //   - 'CREDITED_NOT_BROADCAST' は「実際には不使用」注記付きで残す（クレジット事実）
        // を実現する。テンプレで {THEME_SONGS} は基本「クレジット側展開」の用途なので、
        // BROADCAST_NOT_CREDITED は本ハンドラからの出力には含めないのが自然。
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
              ets.seq             AS Seq,
              ets.is_broadcast_only AS IsBroadcastOnly,
              ets.usage_actuality AS UsageActuality
            FROM episode_theme_songs ets
            JOIN song_recordings sr ON sr.song_recording_id = ets.song_recording_id
            JOIN songs           s  ON s.song_id           = sr.song_id
            WHERE ets.episode_id = @episodeId
              AND ets.theme_kind IN @kinds
              AND ets.usage_actuality <> 'BROADCAST_NOT_CREDITED'
            ORDER BY
              ets.seq,
              ets.is_broadcast_only;
            """;

        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<ThemeSongRow>(new CommandDefinition(
            sql,
            new { episodeId = episodeId.Value, kinds = effectiveKinds },
            cancellationToken: ct))).ToList();

        // v1.3.1 stage B-4：構造化クレジット（song_credits / song_recording_singers）が存在する場合は
        // それを HTML 版で取得してリンク化済み HTML 断片を *Html フィールドに詰める。
        // 構造化が存在しない場合は、フリーテキスト列（songs.lyricist_name 等）を HtmlEncode した平文を
        // *Html フィールドに入れる（リンクなしフォールバック、選択肢 A：移行期の混在表示を許容）。
        // テンプレ展開は 1 エピソード当たり主題歌 2-4 件程度のため、行ごとの追加クエリで実用的に問題ない。
        var songCredits = new SongCreditsRepository(factory);
        var recordingSingers = new SongRecordingSingersRepository(factory);
        foreach (var r in rows)
        {
            if (r.SongId > 0)
            {
                // 作詞：構造化があればリンク化 HTML、なければフリーテキスト HtmlEncode 平文。
                string lyrHtml = await songCredits.GetDisplayHtmlAsync(r.SongId, SongCreditRoles.Lyrics, lookup, ct).ConfigureAwait(false);
                r.LyricistHtml = !string.IsNullOrEmpty(lyrHtml)
                    ? lyrHtml
                    : (string.IsNullOrEmpty(r.LyricistName) ? "" : System.Net.WebUtility.HtmlEncode(r.LyricistName));

                // 作曲：同様。
                string cmpHtml = await songCredits.GetDisplayHtmlAsync(r.SongId, SongCreditRoles.Composition, lookup, ct).ConfigureAwait(false);
                r.ComposerHtml = !string.IsNullOrEmpty(cmpHtml)
                    ? cmpHtml
                    : (string.IsNullOrEmpty(r.ComposerName) ? "" : System.Net.WebUtility.HtmlEncode(r.ComposerName));

                // 編曲：同様。
                string arrHtml = await songCredits.GetDisplayHtmlAsync(r.SongId, SongCreditRoles.Arrangement, lookup, ct).ConfigureAwait(false);
                r.ArrangerHtml = !string.IsNullOrEmpty(arrHtml)
                    ? arrHtml
                    : (string.IsNullOrEmpty(r.ArrangerName) ? "" : System.Net.WebUtility.HtmlEncode(r.ArrangerName));
            }
            if (r.SongRecordingId > 0)
            {
                // うた：song_recording_singers から VOCALS 役職の連名を HTML 版で取得。
                string singHtml = await recordingSingers.GetDisplayHtmlAsync(r.SongRecordingId, SongRecordingSingerRoles.Vocals, lookup, ct).ConfigureAwait(false);
                r.SingerHtml = !string.IsNullOrEmpty(singHtml)
                    ? singHtml
                    : (string.IsNullOrEmpty(r.SingerName) ? "" : System.Net.WebUtility.HtmlEncode(r.SingerName));
            }
        }

        return rows;
    }

    /// <summary>
    /// 1 曲分のブロック文字列を HTML 出力モードで組み立てる
    /// （v1.3.0 続編で追加 / v1.3.1 stage B-4 で役職リンク化対応）。
    /// <para>
    /// 曲タイトルは <c>/songs/{song_id}/</c> へのリンク、変種ラベルは HTML エスケープのみ。
    /// 「作詞」「作曲」「編曲」「うた」の役職ラベルは
    /// <see cref="ILookupCache.LookupRoleHtmlAsync"/> 経由で役職統計ページへのリンク付き HTML に展開する。
    /// 各クレジット欄の値（<see cref="ThemeSongRow.LyricistHtml"/> 等）は既にリンク化済みの
    /// HTML 断片として詰められているため、二重エンコードを避けるため HtmlEncode は通さない。
    /// </para>
    /// </summary>
    private static async Task<string> RenderSingleSongBlockHtml(ThemeSongRow r, ILookupCache lookup)
    {
        var sb = new System.Text.StringBuilder();
        var safeTitle = System.Net.WebUtility.HtmlEncode(r.SongTitle ?? "(曲名未登録)");
        sb.Append('「');
        if (r.SongId > 0)
        {
            sb.Append($"<a href=\"/songs/{r.SongId}/\">{safeTitle}</a>");
        }
        else
        {
            sb.Append(safeTitle);
        }
        sb.Append('」');
        if (!string.IsNullOrEmpty(r.VariantLabel))
        {
            sb.Append($" [{System.Net.WebUtility.HtmlEncode(r.VariantLabel)}]");
        }
        // v1.3.0 ブラッシュアップ続編：CREDITED_NOT_BROADCAST のときだけ注記。
        // 「クレジットには載っているが本放送では実際には流れていない」状態の主題歌で、
        // クレジット展開には残しつつ「事実としては不使用」を読者に伝えるため。
        if (string.Equals(r.UsageActuality, "CREDITED_NOT_BROADCAST", StringComparison.Ordinal))
        {
            sb.Append("（実際には不使用）");
        }
        sb.Append('\n');

        // 役職ラベルを lookup でリンク化済み HTML に展開する。
        // 未登録役職コードのときは null が返るので、その場合は素朴な固定ラベル（"作詞" 等）にフォールバック。
        async Task<string> RoleLabel(string roleCode, string fallback)
        {
            var html = await lookup.LookupRoleHtmlAsync(roleCode).ConfigureAwait(false);
            return string.IsNullOrEmpty(html) ? fallback : html!;
        }

        if (!string.IsNullOrEmpty(r.LyricistHtml))
        {
            var label = await RoleLabel(SongCreditRoles.Lyrics, "作詞").ConfigureAwait(false);
            sb.Append(label).Append(':').Append(r.LyricistHtml).Append('\n');
        }
        if (!string.IsNullOrEmpty(r.ComposerHtml))
        {
            var label = await RoleLabel(SongCreditRoles.Composition, "作曲").ConfigureAwait(false);
            sb.Append(label).Append(':').Append(r.ComposerHtml).Append('\n');
        }
        if (!string.IsNullOrEmpty(r.ArrangerHtml))
        {
            var label = await RoleLabel(SongCreditRoles.Arrangement, "編曲").ConfigureAwait(false);
            sb.Append(label).Append(':').Append(r.ArrangerHtml).Append('\n');
        }
        if (!string.IsNullOrEmpty(r.SingerHtml))
        {
            var label = await RoleLabel(SongRecordingSingerRoles.Vocals, "うた").ConfigureAwait(false);
            sb.Append(label).Append(':').Append(r.SingerHtml).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// JOIN 結果を受ける DTO（Dapper マッピング用、内部公開）。
    /// v1.2.0 工程 H-16 で internal 化：新 <c>{#THEME_SONGS}</c> ループ構文の Renderer から
    /// 楽曲スコープのプレースホルダ（{SONG_TITLE} 等）を解決するために、フィールドへ直接アクセスする必要がある。
    /// v1.2.3 で <see cref="SongId"/> と <see cref="SongRecordingId"/> を追加：
    /// 構造化クレジット（song_credits / song_recording_singers）の解決に使う。
    /// v1.3.1 stage B-4 で <see cref="LyricistHtml"/> / <see cref="ComposerHtml"/> /
    /// <see cref="ArrangerHtml"/> / <see cref="SingerHtml"/> を追加：
    /// 構造化クレジットからリンク化済み HTML 断片を組み立てて保持する経路。
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
        public byte? Seq { get; set; }
        public byte IsBroadcastOnly { get; set; }
        /// <summary>
        /// 使用実態フラグ（v1.3.0 ブラッシュアップ続編）。NORMAL / CREDITED_NOT_BROADCAST のいずれか。
        /// BROADCAST_NOT_CREDITED は SQL の WHERE で除外済みなので本プロパティに入ることはない。
        /// </summary>
        public string? UsageActuality { get; set; }

        // ── v1.3.1 stage B-4 追加：リンク化済み HTML 断片 ──
        // 構造化クレジットがあればその HTML、なければ <see cref="LyricistName"/> 等のフリーテキストを
        // HtmlEncode した平文がそれぞれ詰まる。<see cref="RenderSingleSongBlockHtml"/> はこれらを
        // 二重エンコードせずそのまま結合する。
        public string LyricistHtml { get; set; } = "";
        public string ComposerHtml { get; set; } = "";
        public string ArrangerHtml { get; set; } = "";
        public string SingerHtml { get; set; } = "";
    }
}