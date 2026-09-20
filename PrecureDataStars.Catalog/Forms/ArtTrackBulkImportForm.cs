using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Common.Services;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 配信音源（アートトラック）の一括取り込み画面。
/// <para>
/// プレイリスト ID は自動では探せない（自動生成のアルバムプレイリストは Data API の検索対象外）ため、
/// 人が YouTube Music のアルバムページを開いて URL を貼る作業が必ず要る。本画面はその作業を
/// 商品 1 件ずつフォームを開き直さずに上から順に流せるようにするためのもので、貼った URL を
/// 行ごとに保持し、まとめて展開・書き込みまで実行する。
/// </para>
/// <para>
/// 展開と照合の中身は <see cref="ArtTrackImportService"/> と共通で、商品編集の「展開...」ボタンと
/// 同じ結果になる。違いは「何件をまとめて流すか」だけ。
/// </para>
/// </summary>
public sealed class ArtTrackBulkImportForm : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly TracksRepository _tracksRepo;

    private DataGridView _grid = null!;
    private CheckBox _chkOnlyUnset = null!;
    private Button _btnReload = null!;
    private Button _btnRun = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progress = null!;

    private List<Product> _products = new();
    // 実行中はキャンセルできるようにする（件数が多いと数分かかるため）。
    private CancellationTokenSource? _cts;

    private const int ColCatalogNo = 0;
    private const int ColTitle = 1;
    private const int ColRelease = 2;
    private const int ColStatus = 3;
    private const int ColPlaylist = 4;
    private const int ColResult = 5;

    /// <summary><see cref="ArtTrackBulkImportForm"/> の新しいインスタンスを生成する。</summary>
    public ArtTrackBulkImportForm(ProductsRepository productsRepo, TracksRepository tracksRepo)
    {
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));

        Text = "配信音源の一括取り込み";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 640);

        BuildUi();
        Load += async (_, __) => await ReloadAsync();
    }

    private void BuildUi()
    {
        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        _chkOnlyUnset = new CheckBox
        {
            Text = "未設定の商品のみ表示",
            Checked = true,
            Location = new Point(12, 10),
            AutoSize = true
        };
        _btnReload = new Button { Text = "再読み込み", Location = new Point(190, 6), Size = new Size(90, 26) };
        pnlTop.Controls.Add(_chkOnlyUnset);
        pnlTop.Controls.Add(_btnReload);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        _grid.Columns.Add(ReadOnlyCol("CatalogNo", "品番", 110));
        _grid.Columns.Add(ReadOnlyCol("Title", "商品名", 340));
        _grid.Columns.Add(ReadOnlyCol("Release", "発売日", 90));
        _grid.Columns.Add(ReadOnlyCol("Status", "状態", 80));

        // 唯一の編集可能列。YouTube Music のアルバムページ URL をそのまま貼れる。
        var colPlaylist = new DataGridViewTextBoxColumn
        {
            Name = "Playlist",
            HeaderText = "プレイリスト URL / ID（貼り付け）",
            Width = 260,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _grid.Columns.Add(colPlaylist);
        _grid.Columns.Add(ReadOnlyCol("Result", "結果", 200));

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 56 };
        _btnRun = new Button { Text = "貼り付けた分を取り込む", Location = new Point(12, 12), Size = new Size(170, 30) };
        _progress = new ProgressBar { Location = new Point(196, 16), Size = new Size(260, 22), Visible = false };
        _lblStatus = new Label
        {
            Location = new Point(468, 20),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = ""
        };
        pnlBottom.Controls.Add(_btnRun);
        pnlBottom.Controls.Add(_progress);
        pnlBottom.Controls.Add(_lblStatus);

        Controls.Add(_grid);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);

        _btnReload.Click += async (_, __) => await ReloadAsync();
        _chkOnlyUnset.CheckedChanged += async (_, __) => await ReloadAsync();
        _btnRun.Click += async (_, __) => await RunAsync();
    }

    private static DataGridViewTextBoxColumn ReadOnlyCol(string name, string header, int width)
        => new()
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

    /// <summary>商品一覧を読み直してグリッドへ流し込む。 既に貼り付けてある URL は読み直しで消えるので、実行後の再読み込みは結果確認のあとに行う。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var all = await _productsRepo.GetAllAsync();
            _products = _chkOnlyUnset.Checked
                ? all.Where(p => string.IsNullOrWhiteSpace(p.YoutubeArtTrackPlaylistId)).ToList()
                : all.ToList();

            _grid.Rows.Clear();
            foreach (var p in _products)
            {
                _grid.Rows.Add(
                    p.ProductCatalogNo,
                    p.Title,
                    p.ReleaseDate.ToString("yyyy.M.d"),
                    p.YoutubeArtTrackStatus ?? "",
                    p.YoutubeArtTrackPlaylistId ?? "",
                    "");
            }

            _lblStatus.Text = $"{_products.Count} 件";
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>URL が入力されている行をまとめて展開し、DB へ書き込む。 1 件ずつ独立して処理し、失敗した行は結果欄にエラーを残して次へ進む （1 件の失敗で全体が止まると、長い作業を最初からやり直すことになるため）。</summary>
    private async Task RunAsync()
    {
        // 実行中の再入と、実行中のキャンセル要求を同じボタンで扱う。
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        var targets = new List<(int RowIndex, string CatalogNo, string Playlist)>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string playlist = ArtTrackImportService.NormalizePlaylistId(
                row.Cells[ColPlaylist].Value?.ToString());
            if (playlist.Length == 0) continue;

            targets.Add((row.Index, row.Cells[ColCatalogNo].Value?.ToString() ?? "", playlist));
        }

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "プレイリスト URL が入力されている行がありません。");
            return;
        }

        string? apiKey = ArtTrackApiKey.TryGet(this);
        if (apiKey is null) return;

        if (MessageBox.Show(this,
                $"{targets.Count} 件を取り込みます。よろしいですか？\r\n\r\n"
                + "各商品について、プレイリストの並び順と DB のトラック順を位置で対応させて\r\n"
                + "動画 ID を書き込みます。タイトルが一致しない行があった商品は\r\n"
                + "状態が AMBIGUOUS になるので、あとから個別に確認できます。",
                "配信音源の一括取り込み", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            != DialogResult.OK) return;

        _cts = new CancellationTokenSource();
        _btnRun.Text = "中止";
        _chkOnlyUnset.Enabled = false;
        _btnReload.Enabled = false;
        _progress.Visible = true;
        _progress.Minimum = 0;
        _progress.Maximum = targets.Count;
        _progress.Value = 0;

        int ok = 0, ambiguous = 0, failed = 0;

        try
        {
            using var api = new YouTubeDataApiClient(apiKey);
            var service = new ArtTrackImportService(api, _tracksRepo, _productsRepo);

            foreach (var (rowIndex, catalogNo, playlist) in targets)
            {
                if (_cts.IsCancellationRequested) break;

                _lblStatus.Text = $"{_progress.Value + 1} / {targets.Count}：{catalogNo}";
                try
                {
                    var preview = await service.PreviewAsync(catalogNo, playlist, _cts.Token);
                    await service.ApplyAsync(preview, _cts.Token);

                    _grid.Rows[rowIndex].Cells[ColPlaylist].Value = preview.PlaylistId;
                    _grid.Rows[rowIndex].Cells[ColStatus].Value = preview.IsFullyMatched ? "MATCHED" : "AMBIGUOUS";
                    _grid.Rows[rowIndex].Cells[ColResult].Value = preview.SummaryText;

                    if (preview.IsFullyMatched)
                    {
                        ok++;
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(226, 245, 226);
                    }
                    else
                    {
                        ambiguous++;
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 214);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    _grid.Rows[rowIndex].Cells[ColResult].Value = ex.Message;
                    _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 226, 226);
                }

                _progress.Value = Math.Min(_progress.Value + 1, _progress.Maximum);
            }
        }
        catch (OperationCanceledException) { /* 中止は正常終了として扱う */ }
        catch (Exception ex) { this.ShowError(ex); }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _btnRun.Text = "貼り付けた分を取り込む";
            _chkOnlyUnset.Enabled = true;
            _btnReload.Enabled = true;
            _progress.Visible = false;
            _lblStatus.Text = $"完了：一致 {ok} 件 / 要確認 {ambiguous} 件 / 失敗 {failed} 件";
        }
    }
}
