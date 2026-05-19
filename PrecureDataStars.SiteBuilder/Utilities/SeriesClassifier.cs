using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>シリーズの種別（<see cref="Series.KindCode"/>）に基づく分類判定の共通ヘルパー。</summary>
public static class SeriesClassifier
{
    /// <summary>
    /// 「親映画併映の短編（子作品）」判定：<c>kind_code == 'MOVIE_SHORT'</c> のものが該当する。
    /// 子作品は単独詳細ページを生成せず、親映画詳細の「併映・子作品」セクションに字下げ表示する
    /// のみとなる。よって一覧・索引・統計の母集合（エピポゲ生成可否、ホーム統計の集計対象など）
    /// からも除外する用途で使う。'MOVIE_SHORT' 以外
    /// （'TV' / 'MOVIE' / 'SPRING' / 'OTONA' / 'SHORT' / 'EVENT' / 'SPIN-OFF'）はすべて親として
    /// 扱い、それぞれ単独詳細ページを持つため <c>false</c> を返す。
    /// SeriesGenerator / MusicGenerator / HomeGenerator が同一実装で個別に保持していた
    /// 判定ロジックを単一定義へ集約したもの。
    /// </summary>
    public static bool IsMovieShortChild(Series s)
        => string.Equals(s.KindCode, "MOVIE_SHORT", StringComparison.Ordinal);
}
