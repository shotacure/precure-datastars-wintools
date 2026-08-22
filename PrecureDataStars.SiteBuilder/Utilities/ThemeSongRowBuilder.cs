using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>主題歌 / 挿入歌の 1 行分（テンプレ描画用 DTO）。
/// EpisodeGenerator / SeriesGenerator の両方が同じ構造で利用する。
/// テンプレ <c>episode-detail.sbn</c> / <c>series-detail.sbn</c> の <c>#theme-songs</c> セクションで描画される。</summary>
public sealed class ThemeSongRow
{
    /// <summary>区分ラベル（"OP" / "ED" / "挿入歌"）。</summary>
    public string KindLabel { get; set; } = "";
    /// <summary>表示タイトル。「<c>songs.title</c> + 半角SP + <c>song_recordings.variant_label</c> 接尾辞」で組む
    /// （SongsGenerator の displayTitle 慣例と同一）。variant_label は「(MOVIE EDIT)」等の版接尾辞のみを保持する
    /// 前提で、テンプレ側は別途バリエーション欄を出さない。</summary>
    public string Title { get; set; } = "";
    /// <summary>親 song のタイトル（VariantLabel に依存しない常に <c>songs.title</c> の値）。
    /// テンプレ側で「実際の収録は recording 側だが、クレジット文脈では親 song のタイトルで表示したい」
    /// シチュエーション（例：本編クレジットが「心のチカラ」表記、recording は「心のチカラ(MOVIE EDIT)」）に使う。
    /// <see cref="Title"/> とは別アクセサで、用途に応じてテンプレが使い分ける。</summary>
    public string SongTitle { get; set; } = "";
    /// <summary>楽曲詳細ページへのリンク URL（song_id が引けたときだけセット）。</summary>
    public string SongLink { get; set; } = "";
    /// <summary>歌唱者のフリーテキスト（フォールバック用）。<see cref="VocalistsHtml"/> の構造化表示が優先される。</summary>
    public string SingerName { get; set; } = "";
    /// <summary>備考（任意）。</summary>
    public string Notes { get; set; } = "";
    /// <summary>本放送限定フラグ（「（本放送のみ）」を末尾に併記する）。</summary>
    public bool IsBroadcastOnly { get; set; }
    /// <summary>使用話数の範囲ラベル（「#1～49 (全話)」「#24～47」等）。
    /// シリーズ詳細で <c>episode_theme_songs</c> をシリーズ単位に集約して出す文脈でのみセットされ、
    /// エピソード詳細・映画シリーズ詳細（<c>series_theme_songs</c> 由来）では空文字のまま。
    /// 既定行の使用範囲に本放送限定行の差し替えが割り込むときは
    /// 「#1～49 (全話)（本放送では #35～38 を除く）」のように差し替え区間の附記まで含む。
    /// 組み立ては <see cref="ThemeSongSeriesAggregator"/> が担う。</summary>
    public string EpisodeRangeLabel { get; set; } = "";

    // ── 構造化クレジット由来の HTML 群 ──
    /// <summary>作詞の表示用 HTML。</summary>
    public string LyricsHtml { get; set; } = "";
    /// <summary>「作詞」役職ラベル HTML（/creators/roles/{rep}/ リンク化済み、未登録時は平文）。</summary>
    public string LyricsRoleLabelHtml { get; set; } = "";
    /// <summary>作曲の表示用 HTML。</summary>
    public string CompositionHtml { get; set; } = "";
    /// <summary>「作曲」役職ラベル HTML。</summary>
    public string CompositionRoleLabelHtml { get; set; } = "";
    /// <summary>編曲の表示用 HTML。</summary>
    public string ArrangementHtml { get; set; } = "";
    /// <summary>「編曲」役職ラベル HTML。</summary>
    public string ArrangementRoleLabelHtml { get; set; } = "";
    /// <summary>歌唱者の表示用 HTML。</summary>
    public string VocalistsHtml { get; set; } = "";
    /// <summary>「歌」役職ラベル HTML（/creators/roles/VOCALS/ リンク付き、未登録時は固定文字列「歌」）。</summary>
    public string VocalistsRoleLabelHtml { get; set; } = "";
    /// <summary>コーラス（BACKING_VOCALS 役）連名の表示用 HTML。 該当録音にコーラス行が無ければ空文字列。</summary>
    public string ChorusHtml { get; set; } = "";
    /// <summary>「コーラス」役職ラベル HTML（/creators/roles/BACKING_VOCALS/ リンク付き、未登録時は固定文字列「コーラス」）。 <see cref="ChorusHtml"/> が非空のときだけセットされる。</summary>
    public string ChorusRoleLabelHtml { get; set; } = "";
}

/// <summary>主題歌行の入力ソース 1 件分（EPISODE / SERIES どちらの主題歌テーブルからでも来る共通形）。
/// <see cref="EpisodeThemeSong"/> ⇔ <see cref="SeriesThemeSong"/> の差分（episode_id vs series_id）を吸収するため、
/// 主題歌行ビルドに必要な属性だけを抽出した中間 DTO。
/// <para><paramref name="Seq"/> は劇中順（1 = 最初に流れたもの）。両テーブルの列は <c>tinyint unsigned</c> だが、
/// シリーズ単位の集約（<see cref="ThemeSongSeriesAggregator"/>）が並び順を通すための連番を載せるので
/// 本 DTO 側は <c>int</c> で受ける。</para>
/// <para><paramref name="EpisodeRangeLabel"/> はシリーズ集約文脈だけで使う使用話数ラベル。
/// エピソード単位・映画シリーズ単位の呼び出しでは省略する（既定 null → 表示されない）。</para></summary>
public sealed record ThemeSongDescriptor(
    int SongRecordingId,
    string ThemeKind,
    int Seq,
    bool IsBroadcastOnly,
    string UsageActuality,
    string? Notes,
    string? EpisodeRangeLabel = null);

/// <summary>主題歌 / 挿入歌セクションの表示用 DTO 列を組み立てる共通ヘルパ。
/// EpisodeGenerator / SeriesGenerator の重複ロジックを集約。
/// BuildContext + Repository + リゾルバ群をコンストラクタで受け取り、
/// <see cref="BuildAsync"/> で <see cref="ThemeSongDescriptor"/> リスト → <see cref="ThemeSongRow"/> リストへ変換する。</summary>
public sealed class ThemeSongRowBuilder
{
    private readonly BuildContext _ctx;
    private readonly StaffNameLinkResolver _staffLinkResolver;
    private readonly SongMusicClassesRepository _songMusicClassesRepo;
    // 歌唱者連名／歌系役職ラベルの HTML 化（3 ジェネレータ共通ビルダ）。
    private readonly SingerHtmlBuilder _singerHtml;

    /// <summary>song_music_classes の表示名キャッシュ（class_code → name_ja）。 同一 builder インスタンスを使い回す限り 1 度だけロード。</summary>
    private Dictionary<string, string>? _songMusicClassLabelMap;

    public ThemeSongRowBuilder(
        BuildContext ctx,
        StaffNameLinkResolver staffLinkResolver,
        RoleSuccessorResolver roleSuccessorResolver,
        SongMusicClassesRepository songMusicClassesRepo)
    {
        _ctx = ctx;
        _staffLinkResolver = staffLinkResolver;
        _songMusicClassesRepo = songMusicClassesRepo;
        _singerHtml = new SingerHtmlBuilder(staffLinkResolver, roleSuccessorResolver);
    }

    /// <summary>主題歌記述子のリストから描画用 ThemeSongRow リストを組み立てる。
    /// usage_actuality='CREDITED_NOT_BROADCAST' の行は本セクションには出さない（エピソード側で
    /// 「クレジットされているが流れていない」という乖離を表現するための符号）。
    /// 並びは「最初に流れたもの優先」で (Seq 昇順, IsBroadcastOnly 降順)。
    /// Seq が劇中順（OP → 挿入歌 → ED）を表すので第 1 キーはこれ。
    /// 同じ枠が本放送と円盤・配信で差し替えられているとき（2 行並立）は、
    /// 実際に先に流れた本放送限定行を先に置く。</summary>
    public async Task<IReadOnlyList<ThemeSongRow>> BuildAsync(
        IReadOnlyList<ThemeSongDescriptor> sources,
        CancellationToken ct = default)
    {
        // マスタは BuildContext で事前展開済み。
        var roleMap = _ctx.RoleByCode;
        var personAliasMap = _ctx.PersonAliasById;
        var characterAliasMap = _ctx.CharacterAliasById;

        // song_music_classes のラベル辞書を遅延ロード。
        if (_songMusicClassLabelMap is null)
        {
            var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _songMusicClassLabelMap = allClasses.ToDictionary(c => c.ClassCode, c => c.NameJa, StringComparer.Ordinal);
        }

        // 構造化クレジット（song_credits / song_recording_singers）は SiteDataLoader が
        // 全件辞書化済み（BuildContext.SongCreditsBySong / SingersByRecording、並びは per-id 取得と同一）。
        // 旧実装の per-song / per-recording DB 引き＋ローカルキャッシュを同期 lookup に置き換える。
        Task<IReadOnlyList<SongCredit>> GetSongCreditsAsync(int songId)
            => Task.FromResult(_ctx.SongCreditsBySong.TryGetValue(songId, out var rows)
                ? rows
                : Array.Empty<SongCredit>());

        Task<IReadOnlyList<SongRecordingSinger>> GetSingersAsync(int songRecordingId)
            => Task.FromResult(_ctx.SingersByRecording.TryGetValue(songRecordingId, out var rows)
                ? rows
                : Array.Empty<SongRecordingSinger>());

        var rows = new List<ThemeSongRow>(sources.Count);
        foreach (var d in sources
            .Where(x => !string.Equals(x.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
            .OrderBy(x => x.Seq)
            .ThenByDescending(x => x.IsBroadcastOnly))
        {
            SongRecording? rec = _ctx.SongRecordingById.TryGetValue(d.SongRecordingId, out var r) ? r : null;
            Song? song = null;
            if (rec is not null && _ctx.SongById.TryGetValue(rec.SongId, out var s)) song = s;

            string kindLabel = _songMusicClassLabelMap.TryGetValue(d.ThemeKind, out var classNameJa)
                ? classNameJa
                : d.ThemeKind;

            int? songId = song?.SongId;
            // 録音単位のアンカー URL（筆頭録音はページ先頭、それ以外は #recording-N）。
            string songLink = songId.HasValue ? _ctx.SongLinkForRecording(d.SongRecordingId, songId.Value) : "";

            string lyricsHtml = "";
            string lyricsRoleLabelHtml = "";
            string compositionHtml = "";
            string compositionRoleLabelHtml = "";
            string arrangementHtml = "";
            string arrangementRoleLabelHtml = "";
            if (song is not null)
            {
                var credits = await GetSongCreditsAsync(song.SongId).ConfigureAwait(false);
                lyricsHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Lyrics, song.LyricistName, personAliasMap);
                compositionHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Composition, song.ComposerName, personAliasMap);
                arrangementHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Arrangement, song.ArrangerName, personAliasMap);
                lyricsRoleLabelHtml = _singerHtml.BuildSongRoleLabelLinkHtml(SongCreditRoles.Lyrics, roleMap, "作詞");
                compositionRoleLabelHtml = _singerHtml.BuildSongRoleLabelLinkHtml(SongCreditRoles.Composition, roleMap, "作曲");
                arrangementRoleLabelHtml = _singerHtml.BuildSongRoleLabelLinkHtml(SongCreditRoles.Arrangement, roleMap, "編曲");
            }

            string vocalistsHtml = "";
            string vocalistsRoleLabelHtml = "";
            string chorusHtml = "";
            string chorusRoleLabelHtml = "";
            if (rec is not null)
            {
                var singers = await GetSingersAsync(rec.SongRecordingId).ConfigureAwait(false);
                vocalistsHtml = _singerHtml.BuildVocalistsHtml(singers, rec.SingerName, personAliasMap, characterAliasMap);
                vocalistsRoleLabelHtml = _singerHtml.BuildSongRoleLabelLinkHtml(SongRecordingSingerRoles.Vocals, roleMap, "歌");
                // コーラス（BACKING_VOCALS 役）も歌と同じ青系バッジで併出する。該当行が無ければ空のまま。
                chorusHtml = _singerHtml.BuildChorusHtml(singers, personAliasMap, characterAliasMap);
                if (!string.IsNullOrEmpty(chorusHtml))
                {
                    chorusRoleLabelHtml = _singerHtml.BuildSongRoleLabelLinkHtml(SongRecordingSingerRoles.Chorus, roleMap, "コーラス");
                }
            }

            // 表示タイトルは「曲名 + 半角SP + variant_label 接尾辞」（SongsGenerator displayTitle と同一慣例）。
            // variant_label は「(MOVIE EDIT)」等の版接尾辞のみを保持する前提。
            string displayTitle = song is not null
                ? SongDisplayTitle.Build(song.Title, rec?.VariantLabel)
                : "(曲名未登録)";

            rows.Add(new ThemeSongRow
            {
                KindLabel = kindLabel,
                Title = displayTitle,
                SongTitle = song?.Title ?? "",
                SongLink = songLink,
                SingerName = rec?.SingerName ?? "",
                LyricsHtml = lyricsHtml,
                LyricsRoleLabelHtml = lyricsRoleLabelHtml,
                CompositionHtml = compositionHtml,
                CompositionRoleLabelHtml = compositionRoleLabelHtml,
                ArrangementHtml = arrangementHtml,
                ArrangementRoleLabelHtml = arrangementRoleLabelHtml,
                VocalistsHtml = vocalistsHtml,
                VocalistsRoleLabelHtml = vocalistsRoleLabelHtml,
                ChorusHtml = chorusHtml,
                ChorusRoleLabelHtml = chorusRoleLabelHtml,
                Notes = d.Notes ?? "",
                IsBroadcastOnly = d.IsBroadcastOnly,
                EpisodeRangeLabel = d.EpisodeRangeLabel ?? "",
            });
        }
        return rows;
    }

    // ────────── 内部ヘルパ群（旧 EpisodeGenerator から移植） ──────────

    private string BuildCreditRoleHtml(
        IReadOnlyList<SongCredit> rows,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var roleRows = rows
            .Where(r => string.Equals(r.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(r => r.CreditSeq)
            .ToList();
        if (roleRows.Count == 0)
        {
            return string.IsNullOrEmpty(fallbackText) ? "" : HtmlUtil.Escape(fallbackText);
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < roleRows.Count; i++)
        {
            var row = roleRows[i];
            if (i > 0) sb.Append(HtmlUtil.Escape(row.PrecedingSeparator ?? ""));
            if (personAliasMap.TryGetValue(row.PersonAliasId, out var alias))
            {
                sb.Append(_staffLinkResolver.ResolveAsHtml(row.PersonAliasId, alias.GetDisplayName()));
            }
            else
            {
                sb.Append("[alias#").Append(row.PersonAliasId).Append("]");
            }
        }
        return sb.ToString();
    }

}
