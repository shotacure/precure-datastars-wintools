namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 作品ページへリンクしているコラム 1 本（作品詳細の「関連記事」の節に自動で並べる分）。
/// </summary>
/// <param name="Title">コラムの題。</param>
/// <param name="Url">コラムの URL（<c>/articles/{slug}/</c>）。</param>
/// <param name="DateLabel">公開日の表示（「2026年11月7日」。日付が無ければ空）。</param>
/// <param name="DateIso">公開日の time 要素の datetime 属性（"yyyy-MM-dd"。日付が無ければ空）。</param>
public sealed record SeriesArticleRef(string Title, string Url, string DateLabel, string DateIso);
