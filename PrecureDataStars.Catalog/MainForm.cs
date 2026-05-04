using System;
using System.Windows.Forms;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms;

namespace PrecureDataStars.Catalog;

/// <summary>
/// カタログ管理 GUI のメインウィンドウ（ハブ画面）。
/// <para>
/// メニューから「商品・ディスク」「トラック」「歌」「劇伴」「マスタ」「クレジット系マスタ」の
/// 各エディタ子フォームを開く。すべてのリポジトリはコンストラクタ経由で受け取り、
/// 子フォームに引き渡す。
/// </para>
/// <para>
/// v1.1.3 より「商品管理」と「ディスク／トラック管理」を以下に再編:
/// <list type="bullet">
///   <item><see cref="ProductDiscsEditorForm"/>: 商品と所属ディスクを 1 画面で扱う</item>
///   <item><see cref="TracksEditorForm"/>: トラック編集専用（SONG / BGM のオートコンプリート候補選択）</item>
/// </list>
/// </para>
/// <para>
/// v1.2.0 でクレジット系マスタ管理（<see cref="CreditMastersEditorForm"/>）を新設。
/// 9 タブ構成（人物 / 企業 / キャラクター / 声優キャスティング / 役職 /
/// シリーズ書式上書き / エピソード主題歌 / シリーズ種別 / パート種別）。
/// </para>
/// </summary>
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

    // v1.2.0: クレジット系マスタの 13 タブ最小編集機能版で必要なリポジトリ群
    // （v1.2.0 工程 A で人物名義 / 企業屋号 / ロゴ / キャラクター名義の編集 UI を追加した。
    //  これに伴い `PersonAliasesRepository` / `PersonAliasPersonsRepository` /
    //  `CompanyAliasesRepository` / `LogosRepository` / `CharacterAliasesRepository` も
    //  Catalog 起動時の DI に積む）
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterVoiceCastingsRepository _voiceCastingsRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly SeriesRoleFormatOverridesRepository _roleOverridesRepo;
    private readonly EpisodeThemeSongsRepository _episodeThemeSongsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;
    private readonly PartTypesRepository _partTypesRepo;
    private readonly EpisodesRepository _episodesRepo;
    // v1.2.0 工程 A 追加
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // v1.2.0 工程 B-1 追加：クレジット本体（カード／役職／ブロック／エントリ）
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditCardsRepository _creditCardsRepo;
    private readonly CreditCardRolesRepository _creditCardRolesRepo;
    private readonly CreditRoleBlocksRepository _creditRoleBlocksRepo;
    private readonly CreditBlockEntriesRepository _creditBlockEntriesRepo;

    /// <summary>
    /// <see cref="MainForm"/> の新しいインスタンスを生成する。
    /// </summary>
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
        // v1.2.0 から追加されたクレジット系マスタ用リポジトリ群（10 本）
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        CharacterVoiceCastingsRepository voiceCastingsRepo,
        RolesRepository rolesRepo,
        SeriesRoleFormatOverridesRepository roleOverridesRepo,
        EpisodeThemeSongsRepository episodeThemeSongsRepo,
        SeriesKindsRepository seriesKindsRepo,
        PartTypesRepository partTypesRepo,
        EpisodesRepository episodesRepo,
        // v1.2.0 工程 A から追加された名義・屋号・ロゴ用リポジトリ群（5 本）
        PersonAliasesRepository personAliasesRepo,
        PersonAliasPersonsRepository personAliasPersonsRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        // v1.2.0 工程 B-1 から追加されたクレジット本体構造用リポジトリ群（5 本）
        CreditsRepository creditsRepo,
        CreditCardsRepository creditCardsRepo,
        CreditCardRolesRepository creditCardRolesRepo,
        CreditRoleBlocksRepository creditRoleBlocksRepo,
        CreditBlockEntriesRepository creditBlockEntriesRepo)
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

        // v1.2.0 クレジット系マスタ用の保持
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _voiceCastingsRepo = voiceCastingsRepo ?? throw new ArgumentNullException(nameof(voiceCastingsRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _roleOverridesRepo = roleOverridesRepo ?? throw new ArgumentNullException(nameof(roleOverridesRepo));
        _episodeThemeSongsRepo = episodeThemeSongsRepo ?? throw new ArgumentNullException(nameof(episodeThemeSongsRepo));
        _seriesKindsRepo = seriesKindsRepo ?? throw new ArgumentNullException(nameof(seriesKindsRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));

        // v1.2.0 工程 A 追加分の保持
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _personAliasPersonsRepo = personAliasPersonsRepo ?? throw new ArgumentNullException(nameof(personAliasPersonsRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));

        // v1.2.0 工程 B-1 追加分の保持（クレジット本体構造）
        _creditsRepo = creditsRepo ?? throw new ArgumentNullException(nameof(creditsRepo));
        _creditCardsRepo = creditCardsRepo ?? throw new ArgumentNullException(nameof(creditCardsRepo));
        _creditCardRolesRepo = creditCardRolesRepo ?? throw new ArgumentNullException(nameof(creditCardRolesRepo));
        _creditRoleBlocksRepo = creditRoleBlocksRepo ?? throw new ArgumentNullException(nameof(creditRoleBlocksRepo));
        _creditBlockEntriesRepo = creditBlockEntriesRepo ?? throw new ArgumentNullException(nameof(creditBlockEntriesRepo));

        InitializeComponent();
    }

    /// <summary>「ディスク・トラック閲覧」メニュー：DiscBrowserForm を開く（読み取り専用ビュー）。</summary>
    private void mnuBrowse_Click(object? sender, EventArgs e)
    {
        using var f = new DiscBrowserForm(_discsRepo, _tracksRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「商品・ディスク管理」メニュー（v1.1.3 新設）：ProductDiscsEditorForm を開く。</summary>
    private void mnuProductDiscs_Click(object? sender, EventArgs e)
    {
        using var f = new ProductDiscsEditorForm(
            _productsRepo, _discsRepo, _productKindsRepo, _discKindsRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「トラック管理」メニュー（v1.1.3 新設）：TracksEditorForm を開く。</summary>
    private void mnuTracks_Click(object? sender, EventArgs e)
    {
        using var f = new TracksEditorForm(
            _discsRepo, _tracksRepo, _trackContentKindsRepo,
            _songsRepo, _songRecRepo, _bgmCuesRepo,
            _songSizeVariantsRepo, _songPartVariantsRepo,
            _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「歌管理」メニュー：SongsEditorForm を開く。</summary>
    private void mnuSongs_Click(object? sender, EventArgs e)
    {
        using var f = new SongsEditorForm(
            _songsRepo, _songRecRepo, _tracksRepo,
            _songMusicClassesRepo,
            _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「劇伴管理」メニュー：BgmCuesEditorForm を開く。</summary>
    private void mnuBgm_Click(object? sender, EventArgs e)
    {
        using var f = new BgmCuesEditorForm(_bgmCuesRepo, _bgmSessionsRepo, _tracksRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「マスタ管理」メニュー：MastersEditorForm を開く。</summary>
    private void mnuMasters_Click(object? sender, EventArgs e)
    {
        using var f = new MastersEditorForm(
            _productKindsRepo, _discKindsRepo, _trackContentKindsRepo,
            _songMusicClassesRepo, _songSizeVariantsRepo,
            _songPartVariantsRepo,
            _bgmSessionsRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>
    /// 「クレジット系マスタ管理」メニュー（v1.2.0 新設）：<see cref="CreditMastersEditorForm"/> を開く。
    /// 13 タブ構成（人物 / 人物名義 / 企業 / 企業屋号 / ロゴ / キャラクター / キャラクター名義 /
    /// 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 / シリーズ種別 / パート種別）の
    /// 最小編集機能版（v1.2.0 工程 A 完了時点）。v1.2.0 工程 C で各タブの ID 入力欄に検索ピッカーを
    /// 追加したため、歌録音ピッカー用に既存の <see cref="SongRecordingsRepository"/> も注入する。
    /// クレジット本体（カード／ブロック／エントリ）の編集 UI は v1.2.0 の後続工程で別途追加予定。
    /// </summary>
    private void mnuCreditMasters_Click(object? sender, EventArgs e)
    {
        using var f = new CreditMastersEditorForm(
            _personsRepo,
            _companiesRepo,
            _charactersRepo,
            _voiceCastingsRepo,
            _rolesRepo,
            _roleOverridesRepo,
            _episodeThemeSongsRepo,
            _seriesKindsRepo,
            _partTypesRepo,
            _seriesRepo,
            _episodesRepo,
            // v1.2.0 工程 A 追加
            _personAliasesRepo,
            _personAliasPersonsRepo,
            _companyAliasesRepo,
            _logosRepo,
            _characterAliasesRepo,
            // v1.2.0 工程 C 追加：歌録音ピッカー用に既存リポジトリを流用
            _songRecRepo);
        f.ShowDialog(this);
    }

    /// <summary>
    /// 「クレジット編集」メニュー（v1.2.0 工程 B-1 新設）：<see cref="CreditEditorForm"/> を開く。
    /// シリーズ／エピソード／リリース文脈で絞ったクレジットを左ペインで選び、中央ペインで
    /// カード→役職→ブロック→エントリの 4 階層構造を TreeView で確認・編集できる 3 ペイン UI。
    /// 工程 B-1 では表示のみ。編集機能は B-2（構造の追加・並べ替え・削除）と
    /// B-3（エントリ編集 UI と「+ 新規...」によるマスタ自動投入）で順次追加される。
    /// </summary>
    private void mnuCreditEditor_Click(object? sender, EventArgs e)
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
            _songRecRepo);
        f.ShowDialog(this);
    }
}
