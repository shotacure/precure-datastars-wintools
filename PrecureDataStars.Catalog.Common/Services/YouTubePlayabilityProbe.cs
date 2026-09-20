using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// 配信音源が実際に再生できるかを、動画ページの <c>playabilityStatus</c> から判定する。
/// <para>
/// <b>Data API では判別できないための補助手段</b>であり、<see cref="YouTubeDataApiClient"/> とは
/// 別経路であることを明示するためクラスを分けてある。
/// <c>status.embeddable</c> は「埋め込みタグを置いてよいか」しか示さず、embeddable = true のまま
/// 「この動画を視聴できるのは、Music Premium のメンバーのみです」となる音源が実在する。
/// 両者は status・contentDetails・regionRestriction・oEmbed のすべてが同一で、
/// Data API のどのフィールドでも区別できないことを実測で確認している。
/// </para>
/// <para>
/// 取り込み時に 1 動画 1 回だけ問い合わせる用途に限る（巡回や探索には使わない）。
/// 連続アクセスにならないよう 1 件ごとに短い間隔を空ける。
/// </para>
/// </summary>
public sealed class YouTubePlayabilityProbe : IDisposable
{
    /// <summary>誰でも再生できる。</summary>
    public const string StatusOk = "OK";
    /// <summary>YouTube Music Premium 会員のみ再生できる。</summary>
    public const string StatusPremiumOnly = "PREMIUM_ONLY";
    /// <summary>上記以外の理由で再生できない（削除・地域制限など）。</summary>
    public const string StatusUnplayable = "UNPLAYABLE";

    /// <summary>この件数だけ続けて同じ結果が出たら、残りも同じとみなして問い合わせを打ち切る。</summary>
    private const int ConfirmStreakLength = 3;

    /// <summary>1 件ごとに空ける間隔。短時間に連続して叩かないための最小限の配慮。</summary>
    private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(200);

    private readonly HttpClient _http;

    /// <summary><see cref="YouTubePlayabilityProbe"/> の新しいインスタンスを生成する。</summary>
    public YouTubePlayabilityProbe()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36");
        // 判定理由の文言が日本語で返るようにする（Premium 限定の見分けに使う）。
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9");
    }

    /// <summary>動画の再生可否をまとめて判定する。 取得や解析に失敗した動画は戻り値に現れない（未確認のまま扱う）。</summary>
    /// <remarks>
    /// 全件を確かめる必要はない。Premium 限定かどうかはアルバム単位の配信条件で決まるため、
    /// 1 枚の中で混在することは実質起こらない。そこで先頭から順に確かめ、
    /// <see cref="ConfirmStreakLength"/> 件続けて同じ結果が出た時点で打ち切り、
    /// 残りは同じ結果として埋める。40 曲のアルバムでも問い合わせは 3 件で済む。
    /// 判定できなかった（null）件は連続の数に入れず、そこで数え直す。
    /// </remarks>
    /// <param name="videoIds">対象の動画 ID。</param>
    /// <param name="progress">1 件終わるごとに (完了数, 総数) を通知する。UI の進捗表示用。 打ち切ったときは残りをまとめて完了として通知する。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyDictionary<string, string>> GetPlayabilityAsync(
        IEnumerable<string> videoIds,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var distinct = videoIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        string? streakStatus = null;
        int streakLength = 0;

        for (int i = 0; i < distinct.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            string? status = await ProbeOneAsync(distinct[i], ct).ConfigureAwait(false);
            if (status is not null)
            {
                map[distinct[i]] = status;

                if (string.Equals(status, streakStatus, StringComparison.Ordinal)) streakLength++;
                else { streakStatus = status; streakLength = 1; }
            }
            else
            {
                // 判定できなかったものは同じ結果が続いている証拠にならないので数え直す。
                streakStatus = null;
                streakLength = 0;
            }

            progress?.Report((i + 1, distinct.Count));

            // 同じ結果が続いたら残りは問い合わせずに埋める。
            if (streakLength >= ConfirmStreakLength && streakStatus is not null)
            {
                for (int k = i + 1; k < distinct.Count; k++) map[distinct[k]] = streakStatus;
                progress?.Report((distinct.Count, distinct.Count));
                break;
            }

            if (i + 1 < distinct.Count) await Task.Delay(RequestInterval, ct).ConfigureAwait(false);
        }

        return map;
    }

    /// <summary>動画 1 件の再生可否を判定する。判定できなければ null。</summary>
    private async Task<string?> ProbeOneAsync(string videoId, CancellationToken ct)
    {
        try
        {
            string url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
            string html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

            string? json = ExtractPlayabilityStatusJson(html);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string status = root.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
            if (string.Equals(status, "OK", StringComparison.Ordinal)) return StatusOk;

            string reason = root.TryGetProperty("reason", out var rs) ? (rs.GetString() ?? "") : "";
            // 理由文言は表示言語で変わるため、言語に依存しにくい "Premium" の語で判定する。
            return reason.Contains("Premium", StringComparison.OrdinalIgnoreCase)
                ? StatusPremiumOnly
                : StatusUnplayable;
        }
        // 取得や解析に失敗した動画は「未確認」のまま返し、取り込み全体は止めない。
        // TaskCanceledException は OperationCanceledException の派生なので、
        // キャンセル要求とタイムアウトを取り違えないよう ct の状態で切り分ける。
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>ページ HTML から <c>playabilityStatus</c> のオブジェクトだけを切り出す。 全体を JSON として読むと後続のスクリプトまで巻き込むため、波括弧の対応を数えて該当部分を取る。</summary>
    internal static string? ExtractPlayabilityStatusJson(string html)
    {
        // プレイヤー応答側の playabilityStatus を対象にする（ページ冒頭の別データと取り違えないため）。
        int anchor = html.IndexOf("ytInitialPlayerResponse", StringComparison.Ordinal);
        if (anchor < 0) anchor = 0;

        var m = Regex.Match(html[anchor..], "\"playabilityStatus\"\\s*:\\s*\\{");
        if (!m.Success) return null;

        int start = anchor + m.Index + m.Length - 1;
        int depth = 0;
        for (int i = start; i < html.Length; i++)
        {
            if (html[i] == '{') depth++;
            else if (html[i] == '}')
            {
                depth--;
                if (depth == 0) return html[start..(i + 1)];
            }
        }

        return null;
    }

    /// <summary>内部の <see cref="HttpClient"/> を破棄する。</summary>
    public void Dispose() => _http.Dispose();
}
