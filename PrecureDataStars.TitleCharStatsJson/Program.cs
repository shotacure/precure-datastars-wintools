using System.Configuration;
using System.Diagnostics;
using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.TitleCharStatsJson;

namespace PrecureDataStars.TitleCharStatsJson;

/// <summary>
/// サブタイトル文字統計 (<c>title_char_stats</c>) の一括再計算コンソールツール。
/// <para>
/// 全エピソード（is_deleted=0 かつ title_text 非空）を対象に
/// <see cref="TitleCharStatsBuilder.BuildJson"/> で JSON を再生成し、DB を UPDATE する。
/// 進捗表示（% / ETA）付き。
/// </para>
/// </summary>
internal static class Program
{
    /// <summary>監査列 (updated_by) に記録するユーザー識別子。</summary>
    private const string AuditUser = "title_char_stats_json";

    private static async Task<int> Main()
    {
        // コンソール出力を UTF-8 に設定（日本語タイトルの表示に必要）
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 接続準備（App.config: <connectionStrings> の DatastarsMySql）
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
                 ?? throw new InvalidOperationException(
                     "Connection string 'DatastarsMySql' not found in App.config.");
        var factory = new MySqlConnectionFactory(new DbConfig(cs));

        Console.WriteLine("PrecureDataStars.TitleCharStatsJson");
        Console.WriteLine("- 対象: is_deleted = 0 AND title_text IS NOT NULL/EMPTY（全件再計算）");
        Console.WriteLine();

        // 件数
        int total;
        await using (var conn = await factory.CreateOpenedAsync())
        {
            total = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM episodes
                    WHERE is_deleted = 0
                      AND title_text IS NOT NULL
                      AND title_text <> ''
                    """));
        }

        if (total == 0)
        {
            Console.WriteLine("対象レコードはありません。");
            return 0;
        }

        Console.WriteLine($"対象件数: {total} 件");
        Console.WriteLine();

        // 対象一覧（ID + タイトルのみ）
        List<(int EpisodeId, string TitleText)> targets;
        await using (var conn = await factory.CreateOpenedAsync())
        {
            var rows = await conn.QueryAsync<(int EpisodeId, string TitleText)>(
                new CommandDefinition(
                    """
                    SELECT episode_id AS EpisodeId,
                           title_text  AS TitleText
                    FROM episodes
                    WHERE is_deleted = 0
                      AND title_text IS NOT NULL
                      AND title_text <> ''
                    ORDER BY on_air_at, episode_id;
                    """));
            targets = rows.ToList();
        }

        // 進捗
        var sw = Stopwatch.StartNew();
        int done = 0, ok = 0, failed = 0;

        Console.WriteLine("集計開始");
        WriteProgress(done, total, sw.Elapsed, suffix: "");

        // 対象エピソードを 1 件ずつ処理（NFKC 正規化 → 書記素分類 → JSON 生成 → DB 更新）
        foreach (var t in targets)
        {
            try
            {
                // 仕様準拠のビルダーで JSON 生成（書記素ベース / 正規化 等）
                var json = TitleCharStatsBuilder.BuildJson(t.TitleText);

                await UpdateStatsAsync(factory, t.EpisodeId, json);
                ok++;
            }
            catch
            {
                failed++;
            }
            finally
            {
                done++;
                var suffix = $" OK:{ok}  Error:{failed}";
                WriteProgress(done, total, sw.Elapsed, suffix);
            }
        }

        Console.WriteLine();
        Console.WriteLine("完了。");
        Console.WriteLine($"OK={ok}, Error={failed}");
        return 0;
    }
    
    /// <summary>
    /// 指定エピソードの <c>title_char_stats</c> カラムを生成済み JSON で UPDATE する。
    /// updated_by には <see cref="AuditUser"/> を記録する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    /// <param name="episodeId">更新対象のエピソード ID。</param>
    /// <param name="json">生成済みの文字統計 JSON 文字列。</param>
    private static async Task UpdateStatsAsync(MySqlConnectionFactory factory, int episodeId, string json)
    {
        await using var conn = await factory.CreateOpenedAsync();
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE episodes
                SET title_char_stats = @json,
                    updated_by       = @updatedBy
                WHERE episode_id    = @episodeId;
                """,
                new { json, updatedBy = AuditUser, episodeId }));
    }

    /// <summary>
    /// YouTubeCrawler の進捗と同等の表示（% と ETA）
    /// </summary>
    /// <param name="done"></param>
    /// <param name="total"></param>
    /// <param name="elapsed"></param>
    /// <param name="suffix"></param>
    private static void WriteProgress(int done, int total, TimeSpan elapsed, string suffix)
    {
        double rate = done > 0 ? elapsed.TotalSeconds / done : 0.0;
        var remaining = done < total
            ? TimeSpan.FromSeconds(rate * (total - done))
            : TimeSpan.Zero;

        var pct = total > 0 ? (int)(done * 100.0 / total) : 100;

        Console.Write($"\r[{pct,3}%] {done,5}/{total,-5}  elapsed {elapsed:hh\\:mm\\:ss}  eta {remaining:hh\\:mm\\:ss}{suffix}   ");
        if (done == total) Console.WriteLine();
    }
}
