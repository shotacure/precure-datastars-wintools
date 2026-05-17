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
    /// <para>
    /// クレジット系マスタ管理（<see cref="Forms.CreditMastersEditorForm"/>）は
    /// 人物名義・企業屋号・ロゴ・キャラクター名義の編集 UI を持ち、
    /// 対応するリポジトリ（<see cref="PersonAliasesRepository"/>,
    /// <see cref="PersonAliasPersonsRepository"/>, <see cref="CompanyAliasesRepository"/>,
    /// <see cref="LogosRepository"/>, <see cref="CharacterAliasesRepository"/>）を DI に追加。
    /// </para>
    /// <para>
    /// 音楽系クレジット構造化テーブル群（person_alias_members /
    /// song_credits / song_recording_singers / bgm_cue_credits）を新設し、
    /// 対応する 4 リポジトリを DI に追加。
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

            // クレジット系マスタ用リポジトリ
            var personsRepo = new PersonsRepository(factory);
            var companiesRepo = new CompaniesRepository(factory);
            var charactersRepo = new CharactersRepository(factory);
            var rolesRepo = new RolesRepository(factory);
            // 役職系譜（多対多）リポジトリ
            var roleSuccessionsRepo = new RoleSuccessionsRepository(factory);
            // role_templates 統合
            // テーブルを扱う RoleTemplatesRepository。クレジット種別マスタも保持する。
            var creditKindsRepo = new CreditKindsRepository(factory);
            var roleTemplatesRepo = new RoleTemplatesRepository(factory);
            var episodeThemeSongsRepo = new EpisodeThemeSongsRepository(factory);
            var seriesKindsRepo = new SeriesKindsRepository(factory);
            var partTypesRepo = new PartTypesRepository(factory);
            var episodesRepo = new EpisodesRepository(factory);

            // マスタ補完（名義・屋号・ロゴ）用リポジトリ（5 本）
            var personAliasesRepo = new PersonAliasesRepository(factory);
            var personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
            var companyAliasesRepo = new CompanyAliasesRepository(factory);
            var logosRepo = new LogosRepository(factory);
            var characterAliasesRepo = new CharacterAliasesRepository(factory);

            // 商品社名マスタ（クレジット非依存・商品メタ専用）。
            // 商品の発売元（label）／販売元（distributor）を ID 紐付けで構造化するための
            // 専用社名マスタ。クレジット系の companies / company_aliases とは完全に独立した別系統。
            var productCompaniesRepo = new ProductCompaniesRepository(factory);

            // クレジット本体（カード／役職／ブロック／エントリ）用リポジトリ（5 本）
            var creditsRepo = new CreditsRepository(factory);
            var creditCardsRepo = new CreditCardsRepository(factory);
            var creditCardRolesRepo = new CreditCardRolesRepository(factory);
            var creditRoleBlocksRepo = new CreditRoleBlocksRepository(factory);
            var creditBlockEntriesRepo = new CreditBlockEntriesRepository(factory);

            // キャラクター区分マスタ
            var characterKindsRepo = new CharacterKindsRepository(factory);

            // Tier / Group 階層の実体テーブル
            var creditCardTiersRepo  = new CreditCardTiersRepository(factory);
            var creditCardGroupsRepo = new CreditCardGroupsRepository(factory);

            // 音楽系クレジット構造化用リポジトリ（4 本）
            var personAliasMembersRepo = new PersonAliasMembersRepository(factory);
            var songCreditsRepo = new SongCreditsRepository(factory);
            var songRecordingSingersRepo = new SongRecordingSingersRepository(factory);
            var bgmCueCreditsRepo = new BgmCueCreditsRepository(factory);

            // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）リポジトリ（3 本）
            var precuresRepo = new PrecuresRepository(factory);
            var characterRelationKindsRepo = new CharacterRelationKindsRepository(factory);
            var characterFamilyRelationsRepo = new CharacterFamilyRelationsRepository(factory);

            Application.Run(new MainForm(
                productsRepo, discsRepo, tracksRepo,
                songsRepo, songRecRepo, bgmCuesRepo, bgmSessionsRepo,
                productKindsRepo, discKindsRepo, trackContentKindsRepo,
                songMusicClassesRepo, songSizeVariantsRepo,
                songPartVariantsRepo,
                seriesRepo,
                // MainForm に渡すクレジット系リポジトリ（voiceCastingsRepo を除外）
                personsRepo, companiesRepo, charactersRepo,
                // creditKindsRepo / roleTemplatesRepo を追加。
                rolesRepo, creditKindsRepo, roleTemplatesRepo, episodeThemeSongsRepo,
                seriesKindsRepo, partTypesRepo,
                episodesRepo,
                // 追加分
                personAliasesRepo, personAliasPersonsRepo,
                companyAliasesRepo, logosRepo,
                characterAliasesRepo,
                // 追加分（クレジット本体構造）
                creditsRepo, creditCardsRepo, creditCardRolesRepo,
                creditRoleBlocksRepo, creditBlockEntriesRepo,
                // 追加分（キャラクター区分マスタ）
                characterKindsRepo,
                // 追加分（Tier / Group 階層の実体テーブル）
                creditCardTiersRepo, creditCardGroupsRepo,
                // 追加分（IConnectionFactory：役職テンプレ展開用）
                factory,
                // 追加分（音楽系クレジット構造化）
                personAliasMembersRepo, songCreditsRepo,
                songRecordingSingersRepo, bgmCueCreditsRepo,
                // 追加分（プリキュア本体マスタ・続柄マスタ・家族関係）
                precuresRepo, characterRelationKindsRepo, characterFamilyRelationsRepo,
                // 役職系譜（多対多）
                roleSuccessionsRepo,
                // 商品社名マスタ
                productCompaniesRepo));
        }
    }
}
