namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// プリキュアの名義表記を組み立てる共有ヘルパ。
/// <para>
/// <c>/precures/{id}/</c> 詳細の h1 と、シリーズ一覧 TV サブ行のプリキュアバッジで
/// 共通して使う。「変身後 / 変身後 2 / 変身前」の各 <c>character_aliases.name</c> を
/// プリキュア観点の並び（変身後を先頭）で「 / 」連結する。NULL・空・空白のみの
/// 名義は連結対象から除外する。
/// </para>
/// </summary>
public static class PrecureNaming
{
    /// <summary>
    /// 渡された名義名のうち、null・空文字・空白のみを除外し、引数の順序のまま
    /// 「 / 」（前後スペース付きスラッシュ）で連結して返す。
    /// すべて空のときは空文字を返す（呼び出し側でフォールバック表記を決める）。
    /// </summary>
    /// <param name="names">連結したい名義名（プリキュア観点の並びで渡す）。</param>
    public static string JoinAliasNames(params string?[] names)
    {
        var parts = new System.Collections.Generic.List<string>(names.Length);
        foreach (var n in names)
        {
            if (!string.IsNullOrWhiteSpace(n))
            {
                parts.Add(n!.Trim());
            }
        }
        return string.Join(" / ", parts);
    }
}
