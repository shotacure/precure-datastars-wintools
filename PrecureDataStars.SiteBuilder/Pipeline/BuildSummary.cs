namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// ビルド実行終了後にコンソールへ出すサマリ情報を集計するヘルパー。
/// </summary>
public sealed class BuildSummary
{
    /// <summary>各 Generator が「ページを 1 個書いた」と申告した回数の累計。</summary>
    public int PagesGenerated { get; private set; }

    /// <summary>セクション別のページ数（"series", "episodes", "credits" 等）。</summary>
    public Dictionary<string, int> PagesBySection { get; } = new(StringComparer.Ordinal);

    /// <summary>1 ページ生成を記録する。</summary>
    /// <param name="section">セクション名（同名カウンタに加算する）。</param>
    public void IncrementPage(string section)
    {
        PagesGenerated++;
        PagesBySection[section] = PagesBySection.TryGetValue(section, out var n) ? n + 1 : 1;
    }
}
