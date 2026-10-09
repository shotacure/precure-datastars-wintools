using System;
using System.Configuration;
using System.Windows.Forms;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Common.Services;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>BDAnalyzer のエントリポイント。</summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // DB 接続が構成されている場合のみ DB 連携を有効化
            DbConfig? dbConfig = null;
            try
            {
                var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString;
                if (!string.IsNullOrWhiteSpace(cs))
                    dbConfig = new DbConfig(cs);
            }
            catch { /* 設定未設定時はサイレントに DB 無効モードで起動 */ }

            MainForm form;
            if (dbConfig is null)
            {
                form = new MainForm();
            }
            else
            {
                var factory = new MySqlConnectionFactory(dbConfig);
                var discsRepo = new DiscsRepository(factory);
                var productsRepo = new ProductsRepository(factory);
                var tracksRepo = new TracksRepository(factory);
                var productKindsRepo = new ProductKindsRepository(factory);
                var seriesRepo = new SeriesRepository(factory);
                // BD/DVD のチャプターを video_chapters に一括登録するためのリポジトリ
                var videoChaptersRepo = new VideoChaptersRepository(factory);
                // 商品社名マスタ（NewProductDialog の既定社取得用）
                var productCompaniesRepo = new ProductCompaniesRepository(factory);
                // Blu-ray の情報（bd_* テーブル群。ディスク ID が鍵）
                var bdRepo = new BdStructureRepository(factory);
                // 話とパートを当てる（作品の話と、パートの円盤尺）
                var episodesRepo = new EpisodesRepository(factory);
                var episodePartsRepo = new EpisodePartsRepository(factory);
                var service = new DiscRegistrationService(discsRepo, productsRepo, tracksRepo);
                form = new MainForm(service, discsRepo, productsRepo, tracksRepo, productKindsRepo, seriesRepo, videoChaptersRepo, productCompaniesRepo, bdRepo, episodesRepo, episodePartsRepo);
            }

            Application.Run(form);
        }
    }
}