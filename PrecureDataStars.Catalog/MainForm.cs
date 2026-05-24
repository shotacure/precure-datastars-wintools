using System;
using System.Windows.Forms;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms;
using PrecureDataStars.Catalog.Forms.NameResolution;

namespace PrecureDataStars.Catalog;

/// <summary>カタログ管理 GUI のメインウィンドウ（ハブ画面）。</summary>
public partial class MainForm : Form
{
    // 商品・ディスク・トラック
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly TracksRepository _tracksRepo;

    // 曲・劇伴
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly BgmSessionsRepository _bgmSessionsRepo;

    // マスタ
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongMusicClassesRepository _songMusicClassesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;

    // 既存参照
    private readonly SeriesRepository _seriesRepo;

    // クレジット系マスタの 13 タブ最小編集機能版で必要なリポジトリ群
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly RolesRepository _rolesRepo;
    // role_templates 統合テーブルを扱う RoleTemplatesRepository。
    // クレジット種別マスタの CreditKindsRepository も保持する。
    private readonly CreditKindsRepository _creditKindsRepo;
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    private readonly EpisodeThemeSongsRepository _episodeThemeSongsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;
    private readonly PartTypesRepository _partTypesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // クレジット本体（カード／役職／ブロック／エントリ）
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditCardsRepository _creditCardsRepo;
    private readonly CreditCardRolesRepository _creditCardRolesRepo;
    // Tier / Group 階層の実体テーブル用リポジトリ
    private readonly CreditCardTiersRepository _creditCardTiersRepo;
    private readonly CreditCardGroupsRepository _creditCardGroupsRepo;
    private readonly CreditRoleBlocksRepository _creditRoleBlocksRepo;
    private readonly CreditBlockEntriesRepository _creditBlockEntriesRepo;
    // キャラクター区分マスタ
    private readonly CharacterKindsRepository _characterKindsRepo;
    // 役職テンプレ展開で episode_theme_songs JOIN 用の接続ファクトリ
    private readonly PrecureDataStars.Data.Db.IConnectionFactory _factory;

    // 音楽系クレジット構造化用リポジトリ（4 本）
    private readonly PersonAliasMembersRepository _personAliasMembersRepo;
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly BgmCueCreditsRepository _bgmCueCreditsRepo;

    // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterRelationKindsRepository _characterRelationKindsRepo;
    private readonly CharacterFamilyRelationsRepository _characterFamilyRelationsRepo;

    // 役職系譜（多対多）
    private readonly RoleSuccessionsRepository _roleSuccessionsRepo;

    // 商品社名マスタ（クレジット非依存）。
    // 商品の発売元（label）／販売元（distributor）を ID 紐付けで構造化するための専用マスタ。
    // ProductDiscsEditorForm に渡して social_company_id 紐付け UI に使うほか、
    // 「商品社名マスタ管理...」メニューから ProductCompaniesEditorForm 経由でも編集する。
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    // 映画作品の BGM リスト（bgm_cues とは別概念の movie_bgm_cues 用）
    private readonly MovieBgmCuesRepository _movieBgmCuesRepo;

    /// <summary><see cref="MainForm"/> の新しいインスタンスを生成する。</summary>
    public MainForm(
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        TracksRepository tracksRepo,
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        BgmCuesRepository bgmCuesRepo,
        BgmSessionsRepository bgmSessionsRepo,
        ProductKindsRepository productKindsRepo,
        DiscKindsRepository discKindsRepo,
        TrackContentKindsRepository trackContentKindsRepo,
        SongMusicClassesRepository songMusicClassesRepo,
        SongSizeVariantsRepository songSizeVariantsRepo,
        SongPartVariantsRepository songPartVariantsRepo,
        SeriesRepository seriesRepo,
        // 追加されたクレジット系マスタ用リポジトリ群（voiceCastingsRepo を除外）
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        RolesRepository rolesRepo,
        // CreditKindsRepository / RoleTemplatesRepository に置き換え。
        CreditKindsRepository creditKindsRepo,
        RoleTemplatesRepository roleTemplatesRepo,
        EpisodeThemeSongsRepository episodeThemeSongsRepo,
        SeriesKindsRepository seriesKindsRepo,
        PartTypesRepository partTypesRepo,
        EpisodesRepository episodesRepo,
        // 追加された名義・屋号・ロゴ用リポジトリ群（5 本）
        PersonAliasesRepository personAliasesRepo,
        PersonAliasPersonsRepository personAliasPersonsRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        // 追加されたクレジット本体構造用リポジトリ群（5 本）
        CreditsRepository creditsRepo,
        CreditCardsRepository creditCardsRepo,
        CreditCardRolesRepository creditCardRolesRepo,
        CreditRoleBlocksRepository creditRoleBlocksRepo,
        CreditBlockEntriesRepository creditBlockEntriesRepo,
        CharacterKindsRepository characterKindsRepo,
        CreditCardTiersRepository creditCardTiersRepo,
        CreditCardGroupsRepository creditCardGroupsRepo,
        // 役職テンプレ展開で episode_theme_songs JOIN 用の接続ファクトリ
        PrecureDataStars.Data.Db.IConnectionFactory factory,
        // 音楽系クレジット構造化用リポジトリ（4 本）
        PersonAliasMembersRepository personAliasMembersRepo,
        SongCreditsRepository songCreditsRepo,
        SongRecordingSingersRepository songRecordingSingersRepo,
        BgmCueCreditsRepository bgmCueCreditsRepo,
        // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
        PrecuresRepository precuresRepo,
        CharacterRelationKindsRepository characterRelationKindsRepo,
        CharacterFamilyRelationsRepository characterFamilyRelationsRepo,
        // 役職系譜（多対多）
        RoleSuccessionsRepository roleSuccessionsRepo,
        // 商品社名マスタ
        ProductCompaniesRepository productCompaniesRepo,
        // 映画 BGM リスト（movie_bgm_cues）
        MovieBgmCuesRepository movieBgmCuesRepo)
    {
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
        _songsRepo = songsRepo ?? throw new ArgumentNullException(nameof(songsRepo));
        _songRecRepo = songRecRepo ?? throw new ArgumentNullException(nameof(songRecRepo));
        _bgmCuesRepo = bgmCuesRepo ?? throw new ArgumentNullException(nameof(bgmCuesRepo));
        _bgmSessionsRepo = bgmSessionsRepo ?? throw new ArgumentNullException(nameof(bgmSessionsRepo));
        _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
        _discKindsRepo = discKindsRepo ?? throw new ArgumentNullException(nameof(discKindsRepo));
        _trackContentKindsRepo = trackContentKindsRepo ?? throw new ArgumentNullException(nameof(trackContentKindsRepo));
        _songMusicClassesRepo = songMusicClassesRepo ?? throw new ArgumentNullException(nameof(songMusicClassesRepo));
        _songSizeVariantsRepo = songSizeVariantsRepo ?? throw new ArgumentNullException(nameof(songSizeVariantsRepo));
        _songPartVariantsRepo = songPartVariantsRepo ?? throw new ArgumentNullException(nameof(songPartVariantsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        // クレジット系マスタ用の保持
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _creditKindsRepo = creditKindsRepo ?? throw new ArgumentNullException(nameof(creditKindsRepo));
        _roleTemplatesRepo = roleTemplatesRepo ?? throw new ArgumentNullException(nameof(roleTemplatesRepo));
        _episodeThemeSongsRepo = episodeThemeSongsRepo ?? throw new ArgumentNullException(nameof(episodeThemeSongsRepo));
        _seriesKindsRepo = seriesKindsRepo ?? throw new ArgumentNullException(nameof(seriesKindsRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));

        // 追加分の保持
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _personAliasPersonsRepo = personAliasPersonsRepo ?? throw new ArgumentNullException(nameof(personAliasPersonsRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));

        // 追加分の保持（クレジット本体構造）
        _creditsRepo = creditsRepo ?? throw new ArgumentNullException(nameof(creditsRepo));
        _creditCardsRepo = creditCardsRepo ?? throw new ArgumentNullException(nameof(creditCardsRepo));
        _creditCardRolesRepo = creditCardRolesRepo ?? throw new ArgumentNullException(nameof(creditCardRolesRepo));
        _creditRoleBlocksRepo = creditRoleBlocksRepo ?? throw new ArgumentNullException(nameof(creditRoleBlocksRepo));
        _creditBlockEntriesRepo = creditBlockEntriesRepo ?? throw new ArgumentNullException(nameof(creditBlockEntriesRepo));

        // 追加分の保持（キャラクター区分マスタ）
        _characterKindsRepo = characterKindsRepo ?? throw new ArgumentNullException(nameof(characterKindsRepo));

        // 追加分の保持（Tier / Group 階層の実体テーブル）
        _creditCardTiersRepo = creditCardTiersRepo ?? throw new ArgumentNullException(nameof(creditCardTiersRepo));
        _creditCardGroupsRepo = creditCardGroupsRepo ?? throw new ArgumentNullException(nameof(creditCardGroupsRepo));

        // 追加分の保持（IConnectionFactory：役職テンプレ展開用）
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        // 追加分の保持（音楽系クレジット構造化）
        _personAliasMembersRepo  = personAliasMembersRepo  ?? throw new ArgumentNullException(nameof(personAliasMembersRepo));
        _songCreditsRepo         = songCreditsRepo         ?? throw new ArgumentNullException(nameof(songCreditsRepo));
        _songRecordingSingersRepo = songRecordingSingersRepo ?? throw new ArgumentNullException(nameof(songRecordingSingersRepo));
        _bgmCueCreditsRepo       = bgmCueCreditsRepo       ?? throw new ArgumentNullException(nameof(bgmCueCreditsRepo));

        // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
        _precuresRepo                  = precuresRepo                  ?? throw new ArgumentNullException(nameof(precuresRepo));
        _characterRelationKindsRepo    = characterRelationKindsRepo    ?? throw new ArgumentNullException(nameof(characterRelationKindsRepo));
        _characterFamilyRelationsRepo  = characterFamilyRelationsRepo  ?? throw new ArgumentNullException(nameof(characterFamilyRelationsRepo));

        // 役職系譜
        _roleSuccessionsRepo           = roleSuccessionsRepo           ?? throw new ArgumentNullException(nameof(roleSuccessionsRepo));

        // 商品社名マスタ
        _productCompaniesRepo          = productCompaniesRepo          ?? throw new ArgumentNullException(nameof(productCompaniesRepo));
        // 映画 BGM リスト
        _movieBgmCuesRepo              = movieBgmCuesRepo              ?? throw new ArgumentNullException(nameof(movieBgmCuesRepo));

        InitializeComponent();
    }

    /// <summary>子フォームを開く間 MainForm を一時的に隠して、戻ってきたら再表示するヘルパ。
    /// 例外時にも MainForm が消えたままにならないよう try/finally で確実に Show する。</summary>
    private void RunChildModal(Action open)
    {
        Hide();
        try { open(); }
        finally { Show(); }
    }

    /// <summary>「ディスク・トラック閲覧」メニュー：DiscBrowserForm を開く（読み取り専用ビュー）。</summary>
    private void mnuBrowse_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new DiscBrowserForm(_discsRepo, _tracksRepo, _seriesRepo);
            f.ShowDialog();
        });

    /// <summary>「商品・ディスク管理」メニュー：ProductDiscsEditorForm を開く。</summary>
    /// <see cref="ProductCompaniesRepository"/> を追加注入する。
    /// 商品の発売元（label）／販売元（distributor）の社名 ID 紐付け UI（picker 起動）で使う。
    private void mnuProductDiscs_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new ProductDiscsEditorForm(
                _productsRepo, _discsRepo, _productKindsRepo, _discKindsRepo, _seriesRepo,
                _productCompaniesRepo);
            f.ShowDialog();
        });

    /// <summary>「商品社名マスタ管理」メニュー： <see cref="ProductCompaniesEditorForm"/> を開く。商品の発売元・販売元として 紐付ける社名（クレジット非依存）の CRUD を行う。</summary>
    private void mnuProductCompanies_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new ProductCompaniesEditorForm(_productCompaniesRepo);
            f.ShowDialog();
        });

    /// <summary>「トラック管理」メニュー：TracksEditorForm を開く。</summary>
    private void mnuTracks_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new TracksEditorForm(
                _discsRepo, _tracksRepo, _trackContentKindsRepo,
                _songsRepo, _songRecRepo, _bgmCuesRepo,
                _songSizeVariantsRepo, _songPartVariantsRepo,
                _seriesRepo);
            f.ShowDialog();
        });

    /// <summary>「歌管理」メニュー：SongsEditorForm を開く（構造化クレジット用 4 リポジトリを追加注入）。</summary>
    private void mnuSongs_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new SongsEditorForm(
                _songsRepo, _songRecRepo, _tracksRepo,
                _songMusicClassesRepo,
                _seriesRepo,
                // 構造化クレジット用
                _personAliasesRepo, _songCreditsRepo,
                _songRecordingSingersRepo, _characterAliasesRepo);
            f.ShowDialog();
        });

    /// <summary>「劇伴管理」メニュー：BgmCuesEditorForm を開く（構造化クレジット用 2 リポジトリを追加注入）。</summary>
    private void mnuBgm_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new BgmCuesEditorForm(
                _bgmCuesRepo, _bgmSessionsRepo, _tracksRepo, _seriesRepo,
                // 構造化クレジット用
                _personAliasesRepo, _bgmCueCreditsRepo);
            f.ShowDialog();
        });

    /// <summary>
    /// 「映画 BGM リスト管理」メニュー：<see cref="MovieBgmCuesEditorForm"/> を開く。
    /// 映画作品専用の movie_bgm_cues（順序・サブ順序・M ナンバー・区分・未使用/欠番）を
    /// 編集する。bgm_cues（TV シリーズのセッション制・劇伴専用）とは別概念。
    /// 紐づけ先は映画系シリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）のみ。
    /// </summary>
    private void mnuMovieBgm_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new MovieBgmCuesEditorForm(
                _movieBgmCuesRepo, _seriesRepo, _trackContentKindsRepo);
            f.ShowDialog();
        });

    /// <summary>「マスタ管理」メニュー：MastersEditorForm を開く。</summary>
    private void mnuMasters_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new MastersEditorForm(
                _productKindsRepo, _discKindsRepo, _trackContentKindsRepo,
                _songMusicClassesRepo, _songSizeVariantsRepo,
                _songPartVariantsRepo,
                _bgmSessionsRepo, _seriesRepo);
            f.ShowDialog();
        });

    /// <summary>「クレジット系マスタ管理」メニュー： <see cref="CreditMastersEditorForm"/> を開く。「プリキュア」「キャラクター続柄」</summary>
    private void mnuCreditMasters_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new CreditMastersEditorForm(
                _personsRepo,
                _companiesRepo,
                _charactersRepo,
                _rolesRepo,
                // 役職書式は _roleTemplatesRepo / _creditKindsRepo で扱う
                // （コンストラクタの順序に合わせて roleTemplates → creditKinds の順で渡す）
                _roleTemplatesRepo,
                _creditKindsRepo,
                _episodeThemeSongsRepo,
                _seriesKindsRepo,
                _partTypesRepo,
                _seriesRepo,
                _episodesRepo,
                _personAliasesRepo,
                _personAliasPersonsRepo,
                _companyAliasesRepo,
                _logosRepo,
                _characterAliasesRepo,
                // 歌録音ピッカー用に既存リポジトリを流用
                _songRecRepo,
                // キャラクター区分マスタ
                _characterKindsRepo,
                // ユニットメンバー管理
                _personAliasMembersRepo,
                // プリキュア本体マスタ・続柄マスタ・家族関係
                _precuresRepo,
                _characterRelationKindsRepo,
                _characterFamilyRelationsRepo,
                // 役職系譜（多対多）
                _roleSuccessionsRepo);
            f.ShowDialog();
        });

    /// <summary>
    /// 「音楽名寄せセンター」メニュー：<see cref="MusicNameResolutionForm"/> を開く。
    /// 構造化エントリ未登録の曲・録音のフリーテキストを一覧化し、トークン分解・
    /// alias マスタ厳密一致による候補提示・ワンクリック登録を提供する。
    /// 入力作業が完了したら撤去する前提のため、ここから単独で起動する。
    /// </summary>
    private void mnuMusicNameResolution_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new MusicNameResolutionForm(
                _factory,
                _personAliasesRepo,
                _characterAliasesRepo,
                _songCreditsRepo,
                _songRecordingSingersRepo);
            f.ShowDialog();
        });

    /// <summary>「クレジット編集」メニュー：CreditEditorForm を開く。</summary>
    private void mnuCreditEditor_Click(object? sender, EventArgs e)
        => RunChildModal(() =>
        {
            using var f = new CreditEditorForm(
                _creditsRepo,
                _creditCardsRepo,
                _creditCardRolesRepo,
                _creditRoleBlocksRepo,
                _creditBlockEntriesRepo,
                _seriesRepo,
                _episodesRepo,
                _rolesRepo,
                _partTypesRepo,
                _personAliasesRepo,
                _companyAliasesRepo,
                _logosRepo,
                _characterAliasesRepo,
                _songRecRepo,
                // QuickAdd ダイアログでマスタ自動投入に使うリポジトリ
                _personsRepo,
                _companiesRepo,
                // キャラ名義 QuickAdd 用
                _charactersRepo,
                _characterKindsRepo,
                // Tier / Group 階層の実体テーブル
                _creditCardTiersRepo,
                _creditCardGroupsRepo,
                // 役職テンプレ展開で episode_theme_songs JOIN 用の接続ファクトリ
                _factory,
                // 「旧 => 新」記法で既存 person に新 alias を追加するための中間表用リポジトリ
                _personAliasPersonsRepo);
            f.ShowDialog();
        });
}
