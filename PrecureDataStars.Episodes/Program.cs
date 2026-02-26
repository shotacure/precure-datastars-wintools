using System.Configuration;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Episodes;

/// <summary>Episodes エディタ（WinForms GUI）のエントリポイント。App.config から接続文字列を取得し、各リポジトリを生成して MainForm を起動する。</summary>
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // App.config の <connectionStrings> から取得します。
        // ※ ビルド後に存在しない場合は例外を投げて終了します。
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
                 ?? throw new InvalidOperationException(
                     "Connection string 'DatastarsMySql' not found. " +
                     "App.config の <connectionStrings> に定義してください。");

        var factory = new MySqlConnectionFactory(new DbConfig(cs));
        var seriesRepo = new SeriesRepository(factory);
        var episodesRepo = new EpisodesRepository(factory);
        var partsRepo = new EpisodePartsRepository(factory);
        var kindsRepo = new SeriesKindsRepository(factory);
        var relKindsRepo = new SeriesRelationKindsRepository(factory);
        var partTypesRepo = new PartTypesRepository(factory);

        Application.Run(new MainForm(seriesRepo, episodesRepo, partsRepo, kindsRepo, relKindsRepo, partTypesRepo));
    }
}
