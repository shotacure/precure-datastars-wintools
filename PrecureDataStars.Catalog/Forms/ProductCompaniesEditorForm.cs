#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品社名マスタ管理フォーム（v1.3.0 ブラッシュアップ stage 20 新設）。
/// <para>
/// <c>product_companies</c> テーブルに対する CRUD GUI。商品の発売元（label）／販売元
/// （distributor）として紐付ける社名を和名・かな・英名で登録する。
/// </para>
/// <para>
/// 本マスタはクレジット系の <see cref="Company"/> / <see cref="CompanyAlias"/> とは独立した
/// 商品メタ情報専用のマスタなので、メインメニューには「クレジット系マスタ管理」とは別に
/// 「商品社名マスタ管理...」として配置している。
/// </para>
/// <para>
/// 画面構成:
/// <list type="bullet">
///   <item>左: 社名一覧（かな昇順）</item>
///   <item>右: 詳細パネル（和名・かな・英名・既定フラグ 2 つ・備考 + 新規/保存/削除ボタン）</item>
/// </list>
/// 数件〜数十件規模のマスタなので、検索ボックスは設けない（一覧スクロールで足りる）。
/// </para>
/// <para>
/// 既定フラグ（v1.3.0 stage 20 確定版で追加）：
/// 「新規商品作成時のレーベル既定にする」「同 販売元既定にする」のチェックボックスで指定する。
/// 排他性（マスタ全体で同フラグが立つのは最大 1 行）はリポジトリ側で担保するため、
/// チェック ON にして保存するだけで他社のフラグは自動的に 0 に落ちる。
/// </para>
/// </summary>
public partial class ProductCompaniesEditorForm : Form
{
    private readonly ProductCompaniesRepository _repo;

    // 表示中の社名一覧（is_deleted=0 のみ）
    private List<ProductCompany> _items = new();

    /// <summary>
    /// <see cref="ProductCompaniesEditorForm"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="repo">商品社名マスタリポジトリ。</param>
    public ProductCompaniesEditorForm(ProductCompaniesRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();
        SetupGridColumns();

        Load += (_, __) => InitializeSplitterLayout();
        Load += async (_, __) => await ReloadAsync();

        gridItems.SelectionChanged += (_, __) => OnItemSelected();

        btnNew.Click += (_, __) => ClearForm();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnDelete.Click += async (_, __) => await DeleteAsync();
        btnReload.Click += async (_, __) => await ReloadAsync();
    }

    /// <summary>
    /// <see cref="splitMain"/> のスプリッタ実値を ClientSize 確定後に設定する。
    /// 左 60% / 右 40% を初期値とし、MinSize（左 200px / 右 280px）は表示後に
    /// 手動ドラッグで畳まれすぎないためのガードとして設定する。
    /// </summary>
    private void InitializeSplitterLayout()
    {
        int usable = splitMain.Width - splitMain.SplitterWidth;
        if (usable < 2) return;

        int wantedPanel1Min = 200;
        int wantedPanel2Min = 280;
        int maxAllowedMin = Math.Max(25, usable / 3);
        splitMain.Panel1MinSize = Math.Min(wantedPanel1Min, maxAllowedMin);
        splitMain.Panel2MinSize = Math.Min(wantedPanel2Min, maxAllowedMin);

        int target = (int)(usable * 0.6);
        int min = splitMain.Panel1MinSize;
        int max = Math.Max(min, usable - splitMain.Panel2MinSize);
        if (target < min) target = min;
        else if (target > max) target = max;
        splitMain.SplitterDistance = target;
    }

    /// <summary>左ペインのグリッド列を定義する。既定フラグ列も視認できるよう含める。</summary>
    private void SetupGridColumns()
    {
        gridItems.Columns.Clear();
        gridItems.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ProductCompanyId", HeaderText = "ID",
                DataPropertyName = nameof(ProductCompany.ProductCompanyId), Width = 50,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "NameJa", HeaderText = "社名(和)",
                DataPropertyName = nameof(ProductCompany.NameJa),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "NameKana", HeaderText = "かな",
                DataPropertyName = nameof(ProductCompany.NameKana), Width = 140 },
            new DataGridViewTextBoxColumn { Name = "NameEn", HeaderText = "英名",
                DataPropertyName = nameof(ProductCompany.NameEn), Width = 140 },
            // v1.3.0 stage20 確定版：既定フラグの可視化。ON なら ★、OFF なら空欄で表示。
            new DataGridViewTextBoxColumn { Name = "DefLabel", HeaderText = "L既",
                DataPropertyName = nameof(DefaultLabelDisplay), Width = 36,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            new DataGridViewTextBoxColumn { Name = "DefDistr", HeaderText = "D既",
                DataPropertyName = nameof(DefaultDistributorDisplay), Width = 36,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } }
        });
    }

    /// <summary>DB から最新の社名一覧を取り直してグリッドへ反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            _items = (await _repo.GetAllAsync()).ToList();
            // 表示用ラッパに包んで、既定フラグを ★/空文字 で可視化する。
            var rows = _items.Select(c => new RowView(c)).ToList();
            gridItems.DataSource = null;
            gridItems.DataSource = rows;
            ClearForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>グリッド選択行の内容を右ペインの詳細フォームに反映する。</summary>
    private void OnItemSelected()
    {
        if (gridItems.CurrentRow?.DataBoundItem is not RowView rv)
        {
            ClearForm();
            return;
        }
        BindToForm(rv.Inner);
    }

    /// <summary>選択された <see cref="ProductCompany"/> を詳細フォームに展開する。</summary>
    private void BindToForm(ProductCompany c)
    {
        numId.Value = c.ProductCompanyId;
        txtNameJa.Text = c.NameJa;
        txtNameKana.Text = c.NameKana ?? "";
        txtNameEn.Text = c.NameEn ?? "";
        chkIsDefaultLabel.Checked = c.IsDefaultLabel;
        chkIsDefaultDistributor.Checked = c.IsDefaultDistributor;
        txtNotes.Text = c.Notes ?? "";
    }

    /// <summary>詳細フォームをクリアし、新規入力モードに戻す。</summary>
    private void ClearForm()
    {
        numId.Value = 0;
        txtNameJa.Text = "";
        txtNameKana.Text = "";
        txtNameEn.Text = "";
        chkIsDefaultLabel.Checked = false;
        chkIsDefaultDistributor.Checked = false;
        txtNotes.Text = "";
    }

    /// <summary>
    /// 詳細フォームの内容で新規登録または上書き保存を行う。既定フラグが ON で保存する場合、
    /// リポジトリ側のトランザクションでマスタ全体の同フラグが 0 に落ちてから本行が 1 になる
    /// （排他性の担保）ので、フォーム側で他行を意識する必要はない。
    /// </summary>
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(txtNameJa.Text))
        {
            MessageBox.Show(this, "社名(和)は必須です。", "入力エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            int id = (int)numId.Value;
            var c = new ProductCompany
            {
                ProductCompanyId     = id,
                NameJa               = txtNameJa.Text.Trim(),
                NameKana             = NullIfEmpty(txtNameKana.Text),
                NameEn               = NullIfEmpty(txtNameEn.Text),
                IsDefaultLabel       = chkIsDefaultLabel.Checked,
                IsDefaultDistributor = chkIsDefaultDistributor.Checked,
                Notes                = NullIfEmpty(txtNotes.Text),
                CreatedBy            = Environment.UserName,
                UpdatedBy            = Environment.UserName,
                IsDeleted            = false
            };

            if (id == 0)
            {
                int newId = await _repo.InsertAsync(c);
                MessageBox.Show(this, $"新規登録しました（ID = {newId}）。", "完了",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await ReloadAsync();
                SelectRowById(newId);
            }
            else
            {
                await _repo.UpdateAsync(c);
                MessageBox.Show(this, $"ID = {id} を更新しました。", "完了",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await ReloadAsync();
                SelectRowById(id);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の社名を論理削除する。FK 側は ON DELETE SET NULL なので関連商品は外れるだけ。</summary>
    private async Task DeleteAsync()
    {
        if (gridItems.CurrentRow?.DataBoundItem is not RowView rv) return;
        var c = rv.Inner;
        if (Confirm($"社名 [{c.NameJa}] (ID={c.ProductCompanyId}) を論理削除しますか？\n" +
                    "本社名を参照している商品の紐付けは ON DELETE SET NULL により外れます。") != DialogResult.Yes) return;

        try
        {
            await _repo.SoftDeleteAsync(c.ProductCompanyId, Environment.UserName);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>指定 ID の行をグリッドで選択し直す（保存直後のフォーカス戻し用）。</summary>
    private void SelectRowById(int id)
    {
        for (int i = 0; i < gridItems.Rows.Count; i++)
        {
            if (gridItems.Rows[i].DataBoundItem is RowView rv && rv.Inner.ProductCompanyId == id)
            {
                gridItems.ClearSelection();
                gridItems.Rows[i].Selected = true;
                gridItems.CurrentCell = gridItems.Rows[i].Cells[0];
                return;
            }
        }
    }

    // ── ヘルパ ──

    /// <summary>空白文字を NULL として扱う。</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Yes/No 確認ダイアログ。</summary>
    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    /// <summary>例外をエラーダイアログで通知する。</summary>
    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // DataGridView バインド用の表示プロパティ（既定フラグ列の星印表示）。
    // インスタンスメソッドではないので外側クラスからもアクセス可能だが、ProductCompany を直接
    // バインドした場合に bool→文字列の整形ができないため、ラッパクラス RowView 経由で表示する。
    private static string DefaultLabelDisplay => ""; // 名前参照用ダミー（nameof のみ）
    private static string DefaultDistributorDisplay => ""; // 同上

    /// <summary>
    /// グリッド表示用の翻訳済みラッパ。元 <see cref="ProductCompany"/> をそのまま保持しつつ、
    /// 既定フラグ列を ★/空文字 で見せるためのプロパティを足してある。
    /// </summary>
    private sealed class RowView
    {
        public ProductCompany Inner { get; }
        public int    ProductCompanyId           => Inner.ProductCompanyId;
        public string NameJa                     => Inner.NameJa;
        public string? NameKana                  => Inner.NameKana;
        public string? NameEn                    => Inner.NameEn;
        public string DefaultLabelDisplay        => Inner.IsDefaultLabel ? "★" : "";
        public string DefaultDistributorDisplay  => Inner.IsDefaultDistributor ? "★" : "";

        public RowView(ProductCompany inner) { Inner = inner; }
    }
}
