using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 楽曲カードの役職バッジ 1 個分の共通中間表現（役職コード / 表示ラベル / 並び順）。
/// 並びは role_map.display_order 昇順 → role_code 昇順で確定済み。各ジェネレータが
/// それぞれの最終 DTO（人物詳細は役職統計ページ URL 付きバッジ、キャラ詳細は URL なしバッジ）へ射影する。
/// </summary>
internal sealed record SongRoleBadgeCore(string Code, string Label, int DisplayOrder);

/// <summary>
/// 楽曲カード 1 枚分（1 曲）の共通中間表現。人物詳細（PersonSongCard）とキャラクター詳細
/// （CharacterSongCard）の双方が本 DTO から必要なフィールドを射影する。
/// </summary>
internal sealed class SongCardCore
{
    public int SongId { get; init; }
    public string SongUrl { get; init; } = "";
    /// <summary>版込みの表示タイトル（歌った録音があれば VariantLabel を半角SP連結、無ければ親曲タイトル）。</summary>
    public string Title { get; init; } = "";
    /// <summary>代表 recording 由来の出典シリーズ名（解決できない場合は空文字）。</summary>
    public string SeriesTitle { get; init; } = "";
    public string SeriesUrl { get; init; } = "";
    /// <summary>出典シリーズの開始年（4 桁、未解決時は空文字）。テンプレで「(2004)」のように添える。</summary>
    public string SeriesStartYearLabel { get; init; } = "";
    /// <summary>並び替え用のシリーズ開始日原値（出典不明は null）。</summary>
    public DateOnly? SeriesStartDateRaw { get; init; }
    /// <summary>並び替え用：代表録音の recording_id。歌唱を含む曲は歌った録音、作詞作曲のみは曲の最古録音の id。</summary>
    public int SortRecordingId { get; init; }
    /// <summary>楽曲種別ラベル（OP / ED / イメージソング 等。代表録音の music_class_code 由来。未設定なら空文字）。</summary>
    public string MusicClassLabel { get; init; } = "";
    /// <summary>楽曲種別バッジ用クラス末尾（"op" / "movie-ed" 等。CSS の .songs-badge-{ここ} / .cat-{ここ} に対応。未設定なら空文字）。</summary>
    public string BadgeClassSuffix { get; init; } = "";
    /// <summary>当該曲での担当役職バッジ群（role_map.display_order 昇順 → role_code 昇順）。</summary>
    public IReadOnlyList<SongRoleBadgeCore> Roles { get; init; } = Array.Empty<SongRoleBadgeCore>();
}

/// <summary>
/// 人物詳細・キャラクター詳細の「楽曲」セクションのカード行（1 カード = 1 曲）構築の共通中間処理。
/// PersonsGenerator / CharactersGenerator が「担当曲の song_id 単位集約 → 歌った録音の解決 →
/// 出典シリーズ・版込みタイトル・楽曲種別・役職バッジの解決」という同一骨格を重複保持していたため一本化した。
/// 最終 DTO（PersonSongCard / CharacterSongCard）への射影と最終並び順の確定だけを各ジェネレータ側に残す。
/// 依存は読み取り専用の <see cref="BuildContext"/> 辞書と引数のみで、並列レンダリングフェーズから
/// 複数スレッドで同時に呼んでも安全。
/// </summary>
internal static class SongCardBuilder
{
    /// <summary>当該名義群のうち、指定曲を「歌った」録音を返す（複数あれば出典シリーズが最も早いもの）。
    /// 歌っていなければ null（人物詳細ではその曲は作詞作曲編曲のみ＝曲単位で出す）。
    /// 人物詳細は person_alias 軸、キャラクター詳細は character_alias 軸の「歌った録音」索引を渡して共用する。</summary>
    internal static SongRecording? ResolveSungRecording(
        BuildContext ctx,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>>? sungRecordingByAlias,
        IReadOnlyList<int> aliasIds,
        int songId)
    {
        if (sungRecordingByAlias is null) return null;
        SongRecording? best = null;
        foreach (var aliasId in aliasIds)
        {
            if (sungRecordingByAlias.TryGetValue(aliasId, out var bySong)
                && bySong.TryGetValue(songId, out var rec)
                && (best is null || ctx.RecordingSeriesStart(rec) < ctx.RecordingSeriesStart(best)))
            {
                best = rec;
            }
        }
        return best;
    }

    /// <summary>
    /// 名義群の担当楽曲をカードの共通中間表現（<see cref="SongCardCore"/>）群に集約する。1 カード = 1 曲。
    /// 同じ曲で複数役職（作詞 + 作曲 等）を持つ場合は同カード内に役職バッジを並べる。
    /// 出典シリーズ・タイトルは、歌った曲は「歌った録音」から解決する（録音ごとに出典・版が異なり得るため）。
    /// 歌唱が無い曲は <paramref name="fallbackRecordingsBySong"/>（人物詳細のみが渡す。作詞作曲編曲だけの
    /// 曲の出典を当該曲の最古 recording から解決するための索引）があればそこから解決する。
    /// 戻り値の並びは song_id 集約辞書の列挙順のまま（最終ソートは呼び出し側が行う）。
    /// </summary>
    internal static List<SongCardCore> BuildCores(
        BuildContext ctx,
        IReadOnlyList<int> aliasIds,
        IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>> songRolesByAlias,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>>? sungRecordingByAlias,
        IReadOnlyDictionary<int, IReadOnlyList<SongRecording>>? fallbackRecordingsBySong,
        IReadOnlyDictionary<string, Role> roleMap)
    {
        // 担当楽曲を song_id 単位で集約。同一曲で複数役職を持つときは role_code 集合を統合する。
        var rolesBySong = new Dictionary<int, HashSet<string>>();
        foreach (var aliasId in aliasIds)
        {
            if (!songRolesByAlias.TryGetValue(aliasId, out var rows)) continue;
            foreach (var (songId, roleCode) in rows)
            {
                if (!rolesBySong.TryGetValue(songId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    rolesBySong[songId] = set;
                }
                set.Add(roleCode);
            }
        }

        var cores = new List<SongCardCore>(rolesBySong.Count);
        foreach (var (songId, roleSet) in rolesBySong)
        {
            if (!ctx.SongById.TryGetValue(songId, out var song)) continue;

            // 出典シリーズ・タイトルの解決：当該名義群が曲を「歌った」場合は、歌った録音（song_recording）の
            // 出典シリーズと版で出す（録音ごとに出典・版が異なり得るため）。歌唱が無く作詞作曲編曲だけの曲は、
            // fallbackRecordingsBySong があれば曲の代表録音（最古 SeriesId）から出典を解決する。
            var sungRec = ResolveSungRecording(ctx, sungRecordingByAlias, aliasIds, songId);
            Series? series = null;
            string title = song.Title;
            // 楽曲種別（OP / ED / イメージソング 等）。カード背景の薄色と右上バッジに使う。
            // 出典・タイトルと同じ代表録音（歌唱曲は歌った録音、作詞作曲のみは最古録音）から解決する。
            string musicClassCode = "";
            // 並び順キー：録音（recording）を共通軸にしたカタログ登場順。song_id と recording_id は
            // 別連番で 1 列に混ぜられないため、代表録音の recording_id を共通の並び順キーとして使う。
            //   歌唱を含む曲 … 歌った録音の recording_id（歌は recording_id 昇順）
            //   作詞作曲編曲のみの曲 … その曲の最古録音（初出）の recording_id（曲と初出録音はほぼ同時登録なので実質 song_id 昇順）
            int sortRecId = int.MaxValue;
            if (sungRec is not null)
            {
                sortRecId = sungRec.SongRecordingId;
                musicClassCode = sungRec.MusicClassCode ?? "";
                if (sungRec.SeriesId is int sungSid && ctx.SeriesById.TryGetValue(sungSid, out var sungSeries))
                    series = sungSeries;
                // VariantLabel は録音の版接尾辞（例「~…Version~」）。曲名に半角SPを挟んで連結し、
                // 版込みの表示タイトルにする（親曲タイトルだけでは版が落ちて不正確なため）。
                title = SongDisplayTitle.Build(song.Title, sungRec.VariantLabel);
            }
            else if (fallbackRecordingsBySong is not null && fallbackRecordingsBySong.TryGetValue(songId, out var recs))
            {
                // recs は SongRecordingId 昇順（事前ソート済み）。先頭＝最古録音＝その曲の初出位置。
                if (recs.Count > 0)
                {
                    sortRecId = recs[0].SongRecordingId;
                    musicClassCode = recs[0].MusicClassCode ?? "";
                }
                foreach (var r in recs)
                {
                    if (r.SeriesId is int sid && ctx.SeriesById.TryGetValue(sid, out var s))
                    {
                        series = s;
                        break;
                    }
                }
            }

            // 役職バッジ群：role_map の display_order 昇順、ラベルは roles マスタから引く
            //（マスタ未登録時はコード値をフォールバック表示する）。
            var roleBadges = roleSet
                .Select(code => new SongRoleBadgeCore(
                    Code: code,
                    Label: roleMap.TryGetValue(code, out var r) ? (r.NameJa ?? code) : code,
                    DisplayOrder: roleMap.TryGetValue(code, out var r2) && r2.DisplayOrder is ushort d
                        ? d : int.MaxValue))
                .OrderBy(b => b.DisplayOrder)
                .ThenBy(b => b.Code, StringComparer.Ordinal)
                .ToList();

            // 楽曲種別ラベル・バッジクラス末尾（楽曲索引 SongsGenerator と同じ規約）。
            string musicClassLabel = (!string.IsNullOrEmpty(musicClassCode)
                && ctx.MusicClassByCode.TryGetValue(musicClassCode, out var mc)) ? mc.NameJa : "";
            string badgeClassSuffix = string.IsNullOrEmpty(musicClassCode)
                ? "" : musicClassCode.ToLowerInvariant().Replace('_', '-');

            cores.Add(new SongCardCore
            {
                SongId = songId,
                SongUrl = ctx.SongLinkForRecording(sortRecId, songId),
                Title = title,
                SeriesTitle = series?.Title ?? "",
                SeriesUrl = series is null ? "" : PathUtil.SeriesUrl(series.Slug),
                SeriesStartYearLabel = series?.StartDate.Year.ToString() ?? "",
                SeriesStartDateRaw = series?.StartDate,
                SortRecordingId = sortRecId,
                MusicClassLabel = musicClassLabel,
                BadgeClassSuffix = badgeClassSuffix,
                Roles = roleBadges
            });
        }
        return cores;
    }
}
