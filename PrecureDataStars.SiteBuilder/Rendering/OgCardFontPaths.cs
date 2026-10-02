namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カードに使う書体ファイルの指定。App.config の <c>OgCard*Font</c> で差し替える。
/// 空のものは同梱フォント（Noto Sans JP）にフォールバックするので、設定が無くてもカードは描ける。
/// <para>
/// 商用書体（モリサワ・フォントワークスなど）はライセンス上リポジトリに同梱できないため、
/// 実行する PC にインストールされたファイルのパスをローカルの設定で指す。
/// </para>
/// </summary>
/// <param name="Title">見出し（氏名・作品名・サブタイトル）と識別子。空なら Noto Sans JP Bold。</param>
/// <param name="Body">本文（値・単位・注記・年表の目盛り）。空なら Noto Sans JP Regular。</param>
/// <param name="Emphasis">本文の強調部（役職名などのラベル、前置きの作品名）。空なら <see cref="Title"/> と同じ書体。</param>
/// <param name="Number">大きいピンクの数字。空なら <see cref="Title"/> と同じ書体。</param>
/// <param name="Watermark">右上の透かし。空なら <see cref="Title"/> と同じ書体。</param>
/// <param name="TitleCondensed">
/// 見出し書体のコンデンス版（UD新ゴ コンデンス90〜50 など、同じ書体の字幅を詰めた別ファイル）。
/// 見出しが 1 行に収まらないとき、長体（字形を横に縮める）の代わりに広い順に差し替えて使う。
/// 無ければ長体で詰める。順序は問わない（幅の比は読み込み時に実測して並べ直す）。
/// </param>
public sealed record OgCardFontPaths(
    string Title = "", string Body = "", string Emphasis = "", string Number = "", string Watermark = "",
    IReadOnlyList<string>? TitleCondensed = null)
{
    /// <summary>何も指定しない（同梱フォントだけで描く）。</summary>
    public static OgCardFontPaths Bundled { get; } = new();

    /// <summary>見出し書体のコンデンス版のパス（指定が無ければ空）。</summary>
    public IReadOnlyList<string> TitleCondensedPaths => TitleCondensed ?? Array.Empty<string>();
}
