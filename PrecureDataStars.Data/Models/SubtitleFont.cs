namespace PrecureDataStars.Data.Models;

/// <summary>
/// subtitle_fonts テーブルに対応するエンティティモデル。
/// サブタイトルのテロップ画像（エピソード詳細のサブタイトル欄・OGP カードのサブタイトル）に使うフォントのマスタで、
/// 免責事項ページの「使用フォントの一覧」の出どころ。
/// <see cref="FontName"/> は <c>series_subtitle_styles.font_subtitle</c> / <c>font_subtitle_ruby</c> に入れている
/// Windows の書体名（「FOT-ハミング ProN B」のように重さまで含む）と同じ文字列で、この文字列で作品側の設定と結び付く。
/// </summary>
public sealed class SubtitleFont
{
    /// <summary>Windows の書体名（PK。作品側の設定と同じ文字列）。</summary>
    public string FontName { get; set; } = string.Empty;

    /// <summary>ライセンス区分（<see cref="SubtitleFontLicenseKinds"/> のコード）。</summary>
    public string LicenseKind { get; set; } = string.Empty;

    /// <summary>提供元の書き方の製品名（「ハミング B」など）。NULL なら <see cref="FontName"/> をそのまま出す。</summary>
    public string? DisplayName { get; set; }

    /// <summary>提供元の製品ページの URL（一覧でリンクする先）。NULL ならリンクしない。</summary>
    public string? ProductUrl { get; set; }
}

/// <summary>
/// subtitle_fonts.license_kind（フォントを使うライセンス）のコード定数。
/// 一覧ではこの区分ごとにフォントをまとめ、区分の見出しからライセンスの公式サイトへリンクする。
/// </summary>
public static class SubtitleFontLicenseKinds
{
    /// <summary>フォントワークス LETS（FOT- の書体）。</summary>
    public const string FontworksLets = "FONTWORKS_LETS";

    /// <summary>Morisawa Fonts（A P-OTF のモリサワ書体と、A-SK の写研書体）。</summary>
    public const string MorisawaFonts = "MORISAWA_FONTS";

    /// <summary>
    /// マスタに行の無い書体のライセンスを、Windows の書体名の接頭辞から推定する
    /// （FOT- ＝ フォントワークス、A-SK / A P-OTF ＝ モリサワ）。どれにも当たらなければ null。
    /// 一覧の出し分けのための補助で、マスタに行があればそちらを使う。
    /// </summary>
    public static string? GuessFromFontName(string fontName)
    {
        if (fontName.StartsWith("FOT-", StringComparison.Ordinal)) return FontworksLets;
        if (fontName.StartsWith("A-SK ", StringComparison.Ordinal) || fontName.StartsWith("A P-OTF ", StringComparison.Ordinal)) return MorisawaFonts;
        return null;
    }
}
