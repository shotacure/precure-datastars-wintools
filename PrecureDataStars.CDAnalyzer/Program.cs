using System;
using System.Configuration;
using System.Windows.Forms;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Common.Services;

namespace PrecureDataStars.CDAnalyzer
{
    /// <summary>CDAnalyzer（CD-DA トラック解析ツール）のエントリポイント。</summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // DB 接続が構成されている場合のみ DB 連携を有効化する（App.config 未設定でも起動は可能）
            DbConfig? dbConfig = null;
            try
            {
                var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString;
                if (!string.IsNullOrWhiteSpace(cs))
                    dbConfig = new DbConfig(cs);
            }
            catch
            {
                // 設定読み取り失敗はサイレント（DB 機能無効モードで起動）
            }

            MainForm form;
            if (dbConfig is null)
            {
                // DB 無効モード：従来どおり読み取り専用
                form = new MainForm();
            }
            else
            {
                // DB 有効モード：リポジトリ類をまとめて DI
                var factory = new MySqlConnectionFactory(dbConfig);
                var discsRepo = new DiscsRepository(factory);
                var productsRepo = new ProductsRepository(factory);
                var tracksRepo = new TracksRepository(factory);
                var productKindsRepo = new ProductKindsRepository(factory);
                var seriesRepo = new SeriesRepository(factory);
                var service = new DiscRegistrationService(discsRepo, productsRepo, tracksRepo);
                form = new MainForm(service, discsRepo, productsRepo, tracksRepo, productKindsRepo, seriesRepo);
            }

            Application.Run(form);
        }
    }
}
