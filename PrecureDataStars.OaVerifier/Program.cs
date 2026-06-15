using System.Configuration;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.OaVerifier.Forms;

namespace PrecureDataStars.OaVerifier;

/// <summary>
/// 本放送フォーマット検証ツール（OaVerifier）のエントリポイント。
/// 地デジ録画 TS を再生し、TOT から放送日を確定 → 該当エピソードの
/// 【本放送未確認】パートの開始/終了境界を頭出しして人手で確認し、
/// 承認したパートの notes からマーカーを除去する。
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // LibVLC のネイティブライブラリ初期化（VideoView / MediaPlayer 生成前に 1 度だけ）。
        LibVLCSharp.Shared.Core.Initialize();

        // App.config の <connectionStrings> から取得する。
        // 承認時の notes 更新（書き込み）を伴うため、書き込み可能な接続が必要。
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
                 ?? throw new InvalidOperationException(
                     "Connection string 'DatastarsMySql' not found. " +
                     "App.config の <connectionStrings> に定義してください。");

        var factory = new MySqlConnectionFactory(new DbConfig(cs));
        var seriesRepo = new SeriesRepository(factory);
        var episodesRepo = new EpisodesRepository(factory);
        var partsRepo = new EpisodePartsRepository(factory);

        Application.Run(new MainForm(seriesRepo, episodesRepo, partsRepo));
    }
}
