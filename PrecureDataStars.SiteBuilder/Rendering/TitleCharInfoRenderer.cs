using System.Text;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// サブタイトル文字ごとの「初出 / 唯一 / N年Mか月ぶり」情報を HTML 化する。
/// 出力は「グリフのボックス＋バッジ群（初出 / 唯一 / N年ぶり）＋復活詳細」の
/// <c>&lt;li class="charinfo-item"&gt;</c> 列で、テンプレ側が <c>&lt;ul class="charinfo-list"&gt;</c> で包む。
/// 判定はすべて <see cref="TitleCharIndex"/>（ビルド開始時に <see cref="Data.SiteDataLoader"/> が
/// 1 度だけ構築する事前計算インデックス）への辞書参照で完結させ、ページ生成中の DB 往復を完全に排除する。
/// 経過月数の計算は既存 SQL <c>GetTitleCharRevivalStatsAsync</c> と同じく
/// 日差 / 30.4375 の四捨五入で求め、12 か月以上のときだけ「ぶり」を出す方針を踏襲する。
/// </summary>
public sealed class TitleCharInfoRenderer
{
    private readonly TitleCharIndex _index;

    public TitleCharInfoRenderer(TitleCharIndex index)
    {
        _index = index;
    }

    /// <summary>指定エピソードの文字情報 HTML を返す。情報が無い場合は空文字。</summary>
    public Task<string> RenderAsync(Episode ep, CancellationToken ct = default)
    {
        // 同期処理だけで完結するが、呼び出し側との互換のため Task を返すシグネチャは維持する。
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Render(ep));
    }

    private string Render(Episode ep)
    {
        var title = ep.TitleText ?? string.Empty;
        if (title.Length == 0) return string.Empty;

        // 1) 登場順ユニーク文字（空白除外、大小文字厳密区別）。
        //    char ベースだが BMP 範囲ではこれで十分（既存エディタの仕様と同じ）。
        var orderedChars = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch)) continue;
            var s = ch.ToString();
            if (seen.Add(s)) orderedChars.Add(s);
        }
        if (orderedChars.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var c in orderedChars)
        {
            if (!_index.ByChar.TryGetValue(c, out var occs) || occs.Count == 0) continue;

            // 初出：TotalEpNo 昇順に整列済みリストの先頭が自分なら初出。
            bool hasFirst = occs[0].EpisodeId == ep.EpisodeId;
            // 唯一：登録エピソードが自分のみ。
            bool hasUnique = occs.Count == 1;

            // 「ぶり」判定：自分より過去（TotalEpNo が小さい）の最後の要素を取り出して経過月数を測る。
            // TotalEpNo が未設定のエピソードは TitleCharIndex 構築時にインデックス対象外なので、ここでは
            // ep.TotalEpNo が null のときだけスキップ判定が要る（リスト上に自分自身が居ないケース）。
            bool hasRevival = false;
            int yearsRev = 0;
            int monthsRev = 0;
            int episodesSince = 0;
            int occurrenceIndex = 0;
            TitleCharOccurrence? prev = null;

            if (ep.TotalEpNo is int meTotalEpNo)
            {
                // occs は TotalEpNo 昇順なので、自分より小さい総話数の最後の要素を線形走査で拾う。
                // 1 文字あたりの要素数はせいぜい数百なので、二分探索化の必要はまだない。
                for (int i = 0; i < occs.Count; i++)
                {
                    var o = occs[i];
                    if (o.TotalEpNo <= meTotalEpNo) occurrenceIndex = i + 1;
                    if (o.TotalEpNo < meTotalEpNo) prev = o;
                    if (o.TotalEpNo > meTotalEpNo) break;
                }

                if (prev is not null)
                {
                    // 既存 SQL ROUND(TIMESTAMPDIFF(DAY, prev, cur) / 30.4375) と等価。
                    int monthsRounded = (int)Math.Round((ep.OnAirAt - prev.OnAirAt).TotalDays / 30.4375);
                    if (monthsRounded >= 12)
                    {
                        hasRevival = true;
                        yearsRev = monthsRounded / 12;
                        monthsRev = monthsRounded % 12;
                        episodesSince = meTotalEpNo - prev.TotalEpNo;
                    }
                }
            }

            if (!hasFirst && !hasUnique && !hasRevival) continue;

            // 1 文字 = 1 行アイテム。グリフのボックスの右にバッジ（初出 / 唯一 / N年ぶり）を並べ、
            // 復活の詳細（何話ぶり・何回目・直前の登場エピソード）は薄色の補足テキストで添える。
            sb.Append("<li class=\"charinfo-item\"><span class=\"charinfo-glyph\">")
              .Append(HtmlUtil.Escape(c))
              .Append("</span><span class=\"charinfo-info\">");
            if (hasFirst) sb.Append("<span class=\"badge badge-first\">初出</span>");
            if (hasUnique) sb.Append("<span class=\"badge badge-uniq\">唯一</span>");
            if (hasRevival && prev is not null)
            {
                sb.Append("<span class=\"badge badge-revival\">").Append(yearsRev).Append("年");
                if (monthsRev > 0) sb.Append(monthsRev).Append("か月");
                sb.Append("ぶり</span>");

                sb.Append("<span class=\"charinfo-detail muted\">(")
                  .Append(episodesSince).Append("話ぶり・").Append(occurrenceIndex).Append("回目) ");
                if (!string.IsNullOrEmpty(prev.SeriesTitle))
                {
                    sb.Append("『").Append(HtmlUtil.Escape(prev.SeriesTitle)).Append("』")
                      .Append("第").Append(prev.SeriesEpNo).Append("話");
                }
                if (!string.IsNullOrEmpty(prev.TitleText))
                {
                    sb.Append("「").Append(HtmlUtil.Escape(prev.TitleText)).Append("」");
                }
                sb.Append("(").Append(prev.OnAirAt.ToString("yyyy.M.d")).Append(")以来</span>");
            }
            sb.Append("</span></li>\n");
        }
        return sb.ToString().TrimEnd('\n');
    }
}
