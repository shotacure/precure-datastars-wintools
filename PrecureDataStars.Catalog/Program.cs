using System;
using System.Configuration;
using System.Windows.Forms;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog
{
    /// <summary>
    /// PrecureDataStars.Catalog（音楽・映像カタログ管理 GUI）のエントリポイント。
    /// <para>
    /// App.config の "DatastarsMySql" 接続文字列から MySQL 接続を確立し、
    /// 各リポジトリを生成して MainForm に注入する。
    /// </para>
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // 接続文字列の取得（App.config は配布 zip の App.config.sample を参照して各自作成する）
            var connStr = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
            {
                MessageBox.Show(
                    "App.config に DatastarsMySql 接続文字列が設定されていません。\n" +
                    "App.config.sample を参考に App.config を作成してください。",
                    "起動エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var factory = new MySqlConnectionFactory(new DbConfig(connStr));

            // 商品・ディスク・トラック系
            var productsRepo = new ProductsRepository(factory);
            var discsRepo = new DiscsRepository(factory);
            var tracksRepo = new TracksRepository(factory);

            // 曲・劇伴系
            var songsRepo = new SongsRepository(factory);
            var songRecRepo = new SongRecordingsRepository(factory);
            var bgmCuesRepo = new BgmCuesRepository(factory);
            var bgmSessionsRepo = new BgmSessionsRepository(factory);

            // マスタ系
            var productKindsRepo = new ProductKindsRepository(factory);
            var discKindsRepo = new DiscKindsRepository(factory);
            var trackContentKindsRepo = new TrackContentKindsRepository(factory);
            var songMusicClassesRepo = new SongMusicClassesRepository(factory);
            var songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
            var songPartVariantsRepo = new SongPartVariantsRepository(factory);

            // 既存（シリーズ参照用）
            var seriesRepo = new SeriesRepository(factory);

            Application.Run(new MainForm(
                productsRepo, discsRepo, tracksRepo,
                songsRepo, songRecRepo, bgmCuesRepo, bgmSessionsRepo,
                productKindsRepo, discKindsRepo, trackContentKindsRepo,
                songMusicClassesRepo, songSizeVariantsRepo,
                songPartVariantsRepo,
                seriesRepo));
        }
    }
}
