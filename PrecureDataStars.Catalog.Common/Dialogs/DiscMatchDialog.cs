using System.ComponentModel;
using System.Data;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Dialogs;

/// <summary>
/// 既存ディスク照合ダイアログ：CDAnalyzer/BDAnalyzer が読み取ったディスクを
/// DB 内の既存ディスクと照合・選択するための共通ダイアログ。
/// <para>
/// 上段に自動照合結果（候補リスト）、下段にキーワード手動検索結果を表示する。
/// </para>
/// </summary>
public partial class DiscMatchDialog : Form
{
    private readonly DiscsRepository _discsRepo;
    private readonly IReadOnlyList<Disc> _initialCandidates;

    /// <summary>選択されたディスク（Cancel 時は null）。</summary>
    public Disc? SelectedDisc { get; private set; }

    /// <summary>新規登録が選ばれたか。true の場合は呼び出し側で NewProductDialog を起動する想定。</summary>
    public bool WantsNewRegistration { get; private set; }

    /// <summary>
    /// 既存商品への追加ディスク登録が選ばれたか（v1.1.3 追加）。
    /// true の場合、呼び出し側で <see cref="AttachToProductDialog"/> を起動して商品を選ばせ、
    /// <see cref="DiscRegistrationService.AttachDiscToExistingProductAsync"/> を呼び出して登録する。
    /// </summary>
    public bool WantsAttachToExistingProduct { get; private set; }

    /// <summary>
    /// <see cref="DiscMatchDialog"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="discsRepo">ディスクリポジトリ。</param>
    /// <param name="initialCandidates">初期候補（自動照合結果）。空の場合は手動検索のみ。</param>
    /// <param name="matchedBy">自動照合に使用したキー種別表示文字列（"MCN" / "CDDB" / "TOC" / ""）。</param>
    public DiscMatchDialog(DiscsRepository discsRepo, IReadOnlyList<Disc> initialCandidates, string matchedBy)
    {
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _initialCandidates = initialCandidates ?? Array.Empty<Disc>();

        InitializeComponent();

        // 自動照合結果の表示（null 回避のため、引数ではなく null チェック済の _initialCandidates を参照）
        lblMatchInfo.Text = _initialCandidates.Count switch
        {
            0 => "自動照合では該当するディスクが見つかりませんでした。下で手動検索できます。",
            1 => $"{matchedBy} による完全一致で 1 件見つかりました。",
            _ => $"{matchedBy} による照合で {_initialCandidates.Count} 件の候補が見つかりました。"
        };

        BindGrid(gridCandidates, _initialCandidates);

        btnUseSelected.Click += BtnUseSelected_Click;
        btnAttachToProduct.Click += BtnAttachToProduct_Click;
        btnNewRegistration.Click += BtnNewRegistration_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSearch.Click += async (_, __) => await DoSearchAsync();
        txtKeyword.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await DoSearchAsync();
            }
        };
    }

    // 検索結果 or 候補をグリッドにバインドする
    private static void BindGrid(DataGridView grid, IReadOnlyList<Disc> discs)
    {
        var table = new DataTable();
        table.Columns.Add("CatalogNo", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("Product", typeof(string));
        table.Columns.Add("Media", typeof(string));
        table.Columns.Add("Tracks", typeof(string));
        table.Columns.Add("MCN", typeof(string));

        foreach (var d in discs)
        {
            // ディスク固有タイトル優先、空なら CD-Text アルバム名で代替
            var displayTitle = d.Title ?? d.CdTextAlbumTitle ?? d.VolumeLabel ?? "(無題)";
            table.Rows.Add(
                d.CatalogNo,
                displayTitle,
                d.ProductCatalogNo,
                d.MediaFormat,
                d.TotalTracks?.ToString() ?? "—",
                d.Mcn ?? "—");
        }
        grid.DataSource = table;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
    }

    // 手動検索
    private async Task DoSearchAsync()
    {
        var kw = txtKeyword.Text?.Trim() ?? "";
        if (kw.Length == 0) return;
        try
        {
            var hits = await _discsRepo.SearchAsync(kw);
            BindGrid(gridSearch, hits);
            lblSearchInfo.Text = $"{hits.Count} 件ヒット";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "検索エラー: " + ex.Message, "検索", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 選択中の行のディスクをリポジトリから取り直して戻り値にセット
    private async void BtnUseSelected_Click(object? sender, EventArgs e)
    {
        // アクティブな方のグリッドから選択行を取得（手動検索グリッドが優先）
        DataGridView? active = gridSearch.SelectedRows.Count > 0 ? gridSearch
                             : gridCandidates.SelectedRows.Count > 0 ? gridCandidates : null;
        if (active is null)
        {
            MessageBox.Show(this, "ディスクを選択してください。", "選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var catalogNo = active.SelectedRows[0].Cells["CatalogNo"].Value?.ToString() ?? "";
        try
        {
            // 最新情報を読み直してから返す（一覧表示の時点から変わっている可能性に備える）
            SelectedDisc = await _discsRepo.GetByCatalogNoAsync(catalogNo);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "取得エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 「新規登録」→ 呼び出し元で NewProductDialog を開くためのフラグを立てて閉じる
    private void BtnNewRegistration_Click(object? sender, EventArgs e)
    {
        WantsNewRegistration = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 「既存商品に追加ディスクとして登録」→ 呼び出し元で <see cref="AttachToProductDialog"/> を開くため
    /// フラグを立てて閉じる（v1.1.3 追加）。
    /// </summary>
    private void BtnAttachToProduct_Click(object? sender, EventArgs e)
    {
        WantsAttachToExistingProduct = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}