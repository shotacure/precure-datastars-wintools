using System.Text;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// サブタイトル文字ごとの「初出 / 唯一 / N年Mか月ぶり」情報を HTML 化する。
/// <para>
/// ロジックは <c>PrecureDataStars.Episodes.Forms.EpisodesEditorForm.BuildTitleInformationPerCharAsync</c>
/// の移植。エディタ側はテキスト出力（プレーン文字列）だが、本クラスは Web 表示用の HTML
/// （<c>&lt;span class="badge"&gt;</c> 等）を生成する。
/// </para>
/// <para>
/// アルゴリズム:
/// <list type="number">
///   <item>サブタイトル <c>title_text</c> から、登場順ユニークな書記素のリストを作る（空白除外、Ordinal 比較）。</item>
///   <item>各文字について <c>EpisodesRepository.GetFirstUseOfCharAsync</c> で初出話を、
///         <c>GetEpisodeUsageCountOfCharAsync</c> で総使用話数を取得し、初出/唯一を判定。</item>
///   <item><c>GetTitleCharRevivalStatsAsync</c> で 1 年以上ぶりの復活情報を取得。</item>
///   <item>該当情報を持つ文字のみ 1 行ずつ "[badge] 詳細" 形式で出力。</item>
/// </list>
/// </para>
/// </summary>
public sealed class TitleCharInfoRenderer
{
    private readonly EpisodesRepository _episodesRepo;

    public TitleCharInfoRenderer(EpisodesRepository episodesRepo)
    {
        _episodesRepo = episodesRepo;
    }

    /// <summary>
    /// 指定エピソードの文字情報 HTML を返す。情報が無い場合は空文字。
    /// </summary>
    public async Task<string> RenderAsync(Episode ep, CancellationToken ct = default)
    {
        var title = ep.TitleText ?? string.Empty;
        if (title.Length == 0) return string.Empty;

        // 1) 登場順ユニーク文字（空白除外、大小文字厳密区別）。
        //    char ベースだが BMP 範囲ではこれで十分（既存エディタの仕様も同じ）。
        var orderedChars = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch)) continue;
            var s = ch.ToString();
            if (seen.Add(s)) orderedChars.Add(s);
        }
        if (orderedChars.Count == 0) return string.Empty;

        // 2) 各文字について初出 / 唯一を判定。
        var firstSet = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in orderedChars)
        {
            var (firstId, _) = await _episodesRepo.GetFirstUseOfCharAsync(c, ct).ConfigureAwait(false);
            var cnt = await _episodesRepo.GetEpisodeUsageCountOfCharAsync(c, ct).ConfigureAwait(false);
            if (firstId.HasValue && firstId.Value == ep.EpisodeId) firstSet.Add(c);
            if (cnt == 1) uniqueSet.Add(c);
        }

        // 3) 復活情報を辞書化。
        var revival = await _episodesRepo.GetTitleCharRevivalStatsAsync(ep.EpisodeId, ct).ConfigureAwait(false);
        var revivalByChar = revival.ToDictionary(x => x.Char, StringComparer.Ordinal);

        // 4) 行を組み立てる。
        var sb = new StringBuilder();
        foreach (var c in orderedChars)
        {
            bool hasFirst = firstSet.Contains(c);
            bool hasUnique = uniqueSet.Contains(c);
            bool hasRevival = revivalByChar.TryGetValue(c, out var r);
            if (!hasFirst && !hasUnique && !hasRevival) continue;

            // "「文字」… [初出] [唯一] 5年3か月(168話)ぶり3回目 『シリーズ』第N話「サブタイトル」(YYYY.M.D)以来"
            sb.Append("「").Append(HtmlUtil.Escape(c)).Append("」… ");
            if (hasFirst) sb.Append("<span class=\"badge badge-first\">初出</span> ");
            if (hasUnique) sb.Append("<span class=\"badge badge-uniq\">唯一</span> ");
            if (hasRevival && r != null)
            {
                sb.Append(r.Years).Append("年").Append(r.Months).Append("か月(")
                  .Append(r.EpisodesSince).Append("話)ぶり").Append(r.OccurrenceIndex).Append("回目 ");

                if (!string.IsNullOrEmpty(r.LastSeriesTitle))
                {
                    sb.Append("『").Append(HtmlUtil.Escape(r.LastSeriesTitle)).Append("』")
                      .Append("第").Append(r.LastSeriesEpNo).Append("話");
                }
                if (!string.IsNullOrEmpty(r.LastTitleText))
                {
                    sb.Append("「").Append(HtmlUtil.Escape(r.LastTitleText)).Append("」");
                }
                sb.Append("(").Append(r.LastOnAirAt.ToString("yyyy.M.d")).Append(")以来");
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }
}