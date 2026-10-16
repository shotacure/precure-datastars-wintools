namespace PrecureDataStars.Data.Models;

/// <summary>
/// fonts テーブルに対応するエンティティモデル。作品で使われた書体のマスタで、1 行 = 放送で使われた書体 1 つ
/// （「ファン蘭 B」のように書体名＋ウェイト）。書体の情報（書体のページ・見本・テロップ画像・免責事項の使用フォント一覧）は
/// すべてこの表から引く。
/// <para>
/// 見本とサブタイトルのテロップ画像は <see cref="RenderFontName"/>（手元の書体）で描く。放送の書体が手元に無いときは
/// 別のウェイトや改刻版で描け、<see cref="RenderFamilyName"/>・<see cref="RenderWeight"/> が放送の書体と違えばサイトに代用の旨を出す。
/// 改刻版のように代用でない描き方は、<see cref="RenderNote"/> に見本へ添える文を持つ。
/// 手元に無い書体は <see cref="RenderFontName"/> を NULL にして見本を描かない。
/// </para>
/// </summary>
public sealed class FontFace
{
    /// <summary>書体 ID（PK）。</summary>
    public int FontId { get; set; }

    /// <summary>書体の作り手（<see cref="FontFoundries"/> のコード）。</summary>
    public string Foundry { get; set; } = string.Empty;

    /// <summary>書体名（提供元の書き方。「ファン蘭」「UD丸ゴ_ラージ」）。</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>ウェイト（「B」「DB」）。ウェイトの無い書体・ウェイトの分からない書体は NULL。</summary>
    public string? Weight { get; set; }

    /// <summary>見本・テロップ画像を描く手元の書体（Windows の書体名）。NULL なら描かない。</summary>
    public string? RenderFontName { get; set; }

    /// <summary>見本を描く書体の名前（「石井ゴシック」）。NULL なら <see cref="FamilyName"/> と同じとみなす。</summary>
    public string? RenderFamilyName { get; set; }

    /// <summary>描く書体のウェイト。<see cref="Weight"/> と違えば代用。NULL なら <see cref="Weight"/> と同じとみなす。</summary>
    public string? RenderWeight { get; set; }

    /// <summary>見本に添える注記（「見本は改刻版の石井ゴシック B で描いています」）。NULL なら代用のときだけ自動の文を出す。</summary>
    public string? RenderNote { get; set; }

    /// <summary>描く書体のライセンス（<see cref="FontLicenseKinds"/> のコード）。描かない書体は NULL。</summary>
    public string? LicenseKind { get; set; }

    /// <summary>提供元の製品ページ（モリサワ・フォントワークス）。</summary>
    public string? ProductUrl { get; set; }

    /// <summary>写研アーカイブの書体のページ。</summary>
    public string? ArchiveUrl { get; set; }

    /// <summary>内部メモ（サイトに出さない）。</summary>
    public string? Notes { get; set; }

    /// <summary>見せる名前（「ファン蘭 B」。ウェイトが無ければ書体名だけ）。</summary>
    public string DisplayName => string.IsNullOrEmpty(Weight) ? FamilyName : $"{FamilyName} {Weight}";

    /// <summary>見本を描く書体の名前（<see cref="RenderFamilyName"/>、無ければ <see cref="FamilyName"/>）。</summary>
    public string EffectiveRenderFamilyName => string.IsNullOrEmpty(RenderFamilyName) ? FamilyName : RenderFamilyName;

    /// <summary>描く書体のウェイト（<see cref="RenderWeight"/>、無ければ <see cref="Weight"/>）。</summary>
    public string? EffectiveRenderWeight => string.IsNullOrEmpty(RenderWeight) ? Weight : RenderWeight;

    /// <summary>見本を描く書体の見せる名前（「石井ゴシック B」）。</summary>
    public string RenderDisplayName =>
        string.IsNullOrEmpty(EffectiveRenderWeight) ? EffectiveRenderFamilyName : $"{EffectiveRenderFamilyName} {EffectiveRenderWeight}";

    /// <summary>見本を放送とは別の書体名・ウェイトで描くか（描く書体がある書体だけ）。</summary>
    public bool IsRenderSubstitute =>
        !string.IsNullOrEmpty(RenderFontName)
        && (!string.Equals(EffectiveRenderFamilyName, FamilyName, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(Weight) && !string.Equals(EffectiveRenderWeight, Weight, StringComparison.Ordinal)));
}

/// <summary>fonts.foundry（書体の作り手）のコード定数。</summary>
public static class FontFoundries
{
    /// <summary>写研。</summary>
    public const string Shaken = "SHAKEN";

    /// <summary>モリサワ。</summary>
    public const string Morisawa = "MORISAWA";

    /// <summary>フォントワークス。</summary>
    public const string Fontworks = "FONTWORKS";

    /// <summary>サイトに出す作り手の名前。</summary>
    public static string Label(string foundry) => foundry switch
    {
        Shaken => "写研",
        Morisawa => "モリサワ",
        Fontworks => "フォントワークス",
        _ => foundry
    };
}

/// <summary>
/// fonts.license_kind（描く書体を使うライセンス）のコード定数。
/// 免責事項の使用フォント一覧ではこの区分ごとに書体をまとめ、区分の見出しからライセンスの公式サイトへリンクする。
/// </summary>
public static class FontLicenseKinds
{
    /// <summary>フォントワークス LETS（FOT- の書体）。</summary>
    public const string FontworksLets = "FONTWORKS_LETS";

    /// <summary>Morisawa Fonts（A P-OTF のモリサワ書体と、A-SK の写研書体）。</summary>
    public const string MorisawaFonts = "MORISAWA_FONTS";
}
