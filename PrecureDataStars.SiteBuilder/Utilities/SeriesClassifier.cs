using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>シリーズの種別（<see cref="Series.KindCode"/>）に基づく分類判定の共通ヘルパー。</summary>
public static class SeriesClassifier
{
    /// <summary>
    /// 「親映画併映の短編（子作品）」判定：<c>kind_code == 'MOVIE_SHORT'</c> のものが該当する。
    /// 子作品も単独詳細ページを持つが、作品数としては親映画に含めて数えるため、ホーム統計の映画の本数や
    /// 記念日カレンダーの公開日など、「作品」の母集合からは除外する用途で使う。シリーズ一覧では
    /// 親映画のカードの中に字下げ表示する。'MOVIE_SHORT' 以外
    /// （'TV' / 'MOVIE' / 'SPRING' / 'OTONA' / 'SHORT' / 'EVENT' / 'SPIN-OFF'）はすべて親として
    /// 扱うため <c>false</c> を返す。
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
