using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>シリーズの種別（<see cref="Series.KindCode"/>）に基づく分類判定の共通ヘルパー。</summary>
public static class SeriesClassifier
{
    /// <summary>
    /// 「親映画併映の短編（子作品）」判定：<c>kind_code == 'MOVIE_SHORT'</c> のものが該当する。
    /// 子作品は単独詳細ページを生成せず、親映画詳細の「併映・子作品」セクションに字下げ表示する
    /// のみとなる。よって一覧・索引・統計の母集合（エピポゲ生成可否、ホーム統計の集計対象など)
    /// からも除外する用途で使う。'MOVIE_SHORT' 以外
    /// （'TV' / 'MOVIE' / 'SPRING' / 'OTONA' / 'SHORT' / 'EVENT' / 'SPIN-OFF'）はすべて親として
    /// 扱い、それぞれ単独詳細ページを持つため <c>false</c> を返す。
    /// SeriesGenerator / MusicGenerator / HomeGenerator が同一実装で個別に保持していた
    /// 判定ロジックを単一定義へ集約したもの。
    /// </summary>
    public static bool IsMovieShortChild(Series s)
        => string.Equals(s.KindCode, "MOVIE_SHORT", StringComparison.Ordinal);

    /// <summary>
    /// シリーズ種別の <c>series_kinds.credit_attach_to</c> が <c>EPISODE</c>（= TV / SPIN-OFF /
    /// OTONA / SHORT。エピソード単位でクレジットが付くシリーズ）かを判定する。
    /// EPISODE 系シリーズは end_date 未確定なら「継続中」を示す「〜」止め期間表記
    /// （<see cref="JpDateFormat.PeriodOrOngoing"/>）の対象。
    /// SERIES 系シリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）はそもそも継続概念を持たない。
    /// kindMap に該当 kind_code が無い場合は安全側で <c>false</c>。
    /// </summary>
    public static bool IsEpisodeAttaching(
        Series s,
        IReadOnlyDictionary<string, SeriesKind> kindMap)
    {
        return kindMap.TryGetValue(s.KindCode, out var k)
            && string.Equals(k.CreditAttachTo, "EPISODE", StringComparison.Ordinal);
    }
}
