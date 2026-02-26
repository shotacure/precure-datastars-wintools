using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Episodes.Forms;

namespace PrecureDataStars.Episodes;

/// <summary>
/// アプリケーションのメインウィンドウ。
/// <para>
/// メニューバーから「シリーズ管理」と「エピソード管理」の各子フォームを開くハブ画面。
/// すべてのリポジトリをコンストラクタ経由で受け取り、子フォームに引き渡す。
/// </para>
/// </summary>
public partial class MainForm : Form
{
    // ── リポジトリ（Program.cs から注入） ──
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly EpisodePartsRepository _partsRepo;
    private readonly SeriesKindsRepository _kindsRepo;
    private readonly SeriesRelationKindsRepository _relKindsRepo;
    private readonly PartTypesRepository _partTypesRepo;

    /// <summary>
    /// <see cref="MainForm"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="seriesRepo">シリーズリポジトリ。</param>
    /// <param name="episodesRepo">エピソードリポジトリ。</param>
    /// <param name="partsRepo">エピソードパートリポジトリ。</param>
    /// <param name="kindsRepo">シリーズ種別マスタリポジトリ。</param>
    /// <param name="relKindsRepo">シリーズ関係種別マスタリポジトリ。</param>
    /// <param name="partTypesRepo">パート種別マスタリポジトリ。</param>
    public MainForm(
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo,
        EpisodePartsRepository partsRepo,
        SeriesKindsRepository kindsRepo,
        SeriesRelationKindsRepository relKindsRepo,
        PartTypesRepository partTypesRepo)
    {
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _partsRepo = partsRepo ?? throw new ArgumentNullException(nameof(partsRepo));
        _kindsRepo = kindsRepo ?? throw new ArgumentNullException(nameof(kindsRepo));
        _relKindsRepo = relKindsRepo ?? throw new ArgumentNullException(nameof(relKindsRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        InitializeComponent();
    }

    /// <summary>メニュー「シリーズ管理」クリック時にシリーズ編集フォームを開く。</summary>
    private void mnuSeries_Click(object? sender, EventArgs e)
        => new SeriesEditorForm(_seriesRepo, _kindsRepo, _relKindsRepo).Show(this);

    /// <summary>メニュー「エピソード管理」クリック時にエピソード編集フォームを開く。</summary>
    private void mnuEpisodes_Click(object? sender, EventArgs e)
        => new EpisodesEditorForm(_seriesRepo, _episodesRepo, _partsRepo, _partTypesRepo).Show(this);
}
