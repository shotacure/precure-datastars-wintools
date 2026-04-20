using System;
using System.Windows.Forms;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms;

namespace PrecureDataStars.Catalog;

/// <summary>
/// カタログ管理 GUI のメインウィンドウ（ハブ画面）。
/// <para>
/// メニューから「商品」「ディスク / トラック」「歌」「劇伴」「マスタ」の各エディタ子フォームを開く。
/// すべてのリポジトリはコンストラクタ経由で受け取り、子フォームに引き渡す。
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
        SeriesRepository seriesRepo)
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

        InitializeComponent();
    }

    /// <summary>「ディスク・トラック閲覧」メニュー：DiscBrowserForm を開く（読み取り専用ビュー）。</summary>
    private void mnuBrowse_Click(object? sender, EventArgs e)
    {
        using var f = new DiscBrowserForm(_discsRepo, _tracksRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「商品管理」メニュー：ProductsEditorForm を開く。</summary>
    private void mnuProducts_Click(object? sender, EventArgs e)
    {
        using var f = new ProductsEditorForm(_productsRepo, _discsRepo, _productKindsRepo, _seriesRepo);
        f.ShowDialog(this);
    }

    /// <summary>「ディスク／トラック管理」メニュー：DiscsEditorForm を開く。</summary>
    private void mnuDiscs_Click(object? sender, EventArgs e)
    {
        using var f = new DiscsEditorForm(
            _discsRepo, _tracksRepo, _productsRepo,
            _discKindsRepo, _trackContentKindsRepo,
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
}
