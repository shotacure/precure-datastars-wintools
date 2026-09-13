using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>YouTube 動画 URL の解釈ヘルパ。エピソード予告・楽曲録音の動画埋め込みで共用する。</summary>
public static partial class YoutubeUtil
{
    /// <summary>
    /// YouTube 動画 URL から 11 文字の動画 ID を取り出す。取り出せなければ空文字を返す。
    /// 典型的な 4 パターンを直接見る:
    ///   https://www.youtube.com/watch?v=XXXX
    ///   https://youtu.be/XXXX
    ///   https://www.youtube.com/embed/XXXX
    ///   https://m.youtube.com/watch?v=XXXX
    /// 11 文字の英数字 + アンダースコア + ハイフンが ID。
    /// </summary>
    public static string ExtractId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var m = VideoIdRegex().Match(url);
        return m.Success ? m.Groups[1].Value : "";
    }

    [GeneratedRegex(@"(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/embed/)([A-Za-z0-9_\-]{11})")]
    private static partial Regex VideoIdRegex();
}
