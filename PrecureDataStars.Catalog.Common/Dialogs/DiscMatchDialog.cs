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
/// <para>
/// アクションボタンは 4 種類:
/// <list type="bullet">
///   <item>選択したディスクに反映: 既存ディスクの物理情報を更新（<see cref="SelectedDisc"/> を返す）</item>
///   <item>選択したディスクの商品に追加: 選択ディスクの所属商品に新ディスクを追加登録する（v1.1.3 で
///     「既存商品に追加」分岐の入口を変更。<see cref="WantsAttachToExistingProduct"/> = true、
///     <see cref="AttachReferenceDisc"/> に選択ディスクを格納）</item>
///   <item>新規商品＋ディスクとして登録: 商品もディスクも新規作成（<see cref="WantsNewRegistration"/> = true）</item>
///   <item>キャンセル</item>
/// </list>
/// 「商品に追加」ボタンはディスク未選択時は Disabled。グリッドのいずれかで行が選ばれると Enabled に切り替わる。
/// </para>
/// </summary>
public partial class DiscMatchDialog : Form
{
    private readonly DiscsRepository _discsRepo;
    private readonly IReadOnlyList<Disc> _initialCandidates;

    /// <summary>選択されたディスク（Cancel 時は null）。「選択したディスクに反映」フローで使用。</summary>
    public Disc? SelectedDisc { get; private set; }

    /// <summary>新規登録が選ばれたか。true の場合は呼び出し側で NewProductDialog を起動する想定。</summary>
    public bool WantsNewRegistration { get; private set; }

    /// <summary>
    /// 既存商品への追加ディスク登録が選ばれたか（v1.1.3 追加）。
    /// true の場合、呼び出し側は <see cref="AttachReferenceDisc"/> から所属商品を引いて
    /// <see cref="ConfirmAttachDialog"/> に流し、
    /// <see cref="DiscRegistrationService.AttachDiscToExistingProductAsync"/> を呼び出して登録する。
    /// </summary>
    public bool WantsAttachToExistingProduct { get; private set; }

    /// <summary>
    /// 「商品に追加」フローで選ばれた既存ディスク（v1.1.3 追加）。
    /// このディスクの <see cref="Disc.ProductCatalogNo"/> が追加先商品の代表品番となる。
    /// 「選択したディスクに反映」フローでも対象を選ぶグリッドは同じため、用途を分けるべく
    /// プロパティ名を <see cref="SelectedDisc"/> と分けてある。
    /// </summary>
    public Disc? AttachReferenceDisc { get; private set; }

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

        // v1.1.3: 「商品に追加」ボタンはディスク未選択では押せない。
        // グリッドのどちらかで行が選ばれた瞬間に Enable する。
        btnAttachToProduct.Enabled = false;
        gridCandidates.SelectionChanged += (_, __) => UpdateAttachButtonEnabled();
        gridSearch.SelectionChanged += (_, __) => UpdateAttachButtonEnabled();

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

    /// <summary>「商品に追加」ボタンの Enabled 状態を、グリッドの選択状況から都度計算する。</summary>
    private void UpdateAttachButtonEnabled()
    {
        bool anyRowSelected = gridSearch.SelectedRows.Count > 0 || gridCandidates.SelectedRows.Count > 0;
        btnAttachToProduct.Enabled = anyRowSelected;
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

    /// <summary>
    /// 現在選択中のグリッドと選択行の品番を取り出す共通処理（v1.1.3 で「商品に追加」フローと共有するため切り出し）。
    /// 手動検索グリッドの選択を優先し、無ければ自動照合グリッドの選択を採用する。
    /// 選択が無ければ null を返す。
    /// </summary>
    private string? GetActiveSelectedCatalogNo()
    {
        DataGridView? active = gridSearch.SelectedRows.Count > 0 ? gridSearch
                             : gridCandidates.SelectedRows.Count > 0 ? gridCandidates : null;
        if (active is null) return null;
        return active.SelectedRows[0].Cells["CatalogNo"].Value?.ToString();
    }

    // 選択中の行のディスクをリポジトリから取り直して戻り値にセット
    private async void BtnUseSelected_Click(object? sender, EventArgs e)
    {
        var catalogNo = GetActiveSelectedCatalogNo();
        if (string.IsNullOrEmpty(catalogNo))
        {
            MessageBox.Show(this, "ディスクを選択してください。", "選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
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
    /// 「選択したディスクの商品に追加」（v1.1.3 改）。グリッドで選んだディスクを最新情報で読み直して
    /// <see cref="AttachReferenceDisc"/> に格納し、<see cref="WantsAttachToExistingProduct"/> を立てて閉じる。
    /// 呼び出し側ではこのディスクの <see cref="Disc.ProductCatalogNo"/> を追加先商品の代表品番として用いる。
    /// </summary>
    private async void BtnAttachToProduct_Click(object? sender, EventArgs e)
    {
        var catalogNo = GetActiveSelectedCatalogNo();
        if (string.IsNullOrEmpty(catalogNo))
        {
            // ボタンは Enabled 制御しているのでここには来ない想定だが、念のため
            MessageBox.Show(this, "追加先となるディスクを選択してください。", "選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            // 最新情報を読み直す（とくに ProductCatalogNo が一覧時点から変わっていないかを担保）
            AttachReferenceDisc = await _discsRepo.GetByCatalogNoAsync(catalogNo);
            if (AttachReferenceDisc is null || string.IsNullOrEmpty(AttachReferenceDisc.ProductCatalogNo))
            {
                MessageBox.Show(this, "選択ディスクの所属商品が確認できません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            WantsAttachToExistingProduct = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "取得エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
