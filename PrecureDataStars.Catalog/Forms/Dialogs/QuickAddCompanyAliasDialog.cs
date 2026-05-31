using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 企業屋号の即時追加ダイアログ。
/// クレジット編集中に「マスタにまだ無い企業屋号」を即座に登録するためのダイアログ。
/// 屋号は <c>company_aliases</c> に 1 行 INSERT し、親 <c>companies</c> 行は
/// 「新規企業として作成」または「既存企業に新 alias を追加」のいずれかから選ぶ。
/// OK で閉じたとき、新規 alias_id が <see cref="CreatedAliasId"/> にセットされる。
/// 呼び出し側（<see cref="CreditEditorForm"/> の警告ペイン）はこれを受けて
/// マスタ系キャッシュを破棄し、テキストを再パースして警告を消す。
/// </summary>
public sealed class QuickAddCompanyAliasDialog : Form
{
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _aliasesRepo;

    private readonly TextBox _txtAliasName;
    private readonly TextBox _txtAliasKana;
    private readonly RadioButton _rbNewCompany;
    private readonly RadioButton _rbExistingCompany;
    private readonly TextBox _txtCompanyName;
    private readonly TextBox _txtCompanyKana;
    private readonly ComboBox _cboExistingCompany;
    private readonly Label _lblPreview;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    /// <summary>OK で確定したときに発行された新規 alias_id。キャンセル時は null。</summary>
    public int? CreatedAliasId { get; private set; }

    public QuickAddCompanyAliasDialog(
        CompaniesRepository companiesRepo,
        CompanyAliasesRepository aliasesRepo,
        string initialAliasName)
    {
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _aliasesRepo = aliasesRepo ?? throw new ArgumentNullException(nameof(aliasesRepo));

        Text = "企業屋号を新規登録";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(500, 360);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        // ── 屋号名 + よみ ──
        layout.Controls.Add(LabelOf("屋号名"), 0, 0);
        _txtAliasName = new TextBox { Dock = DockStyle.Fill, Text = initialAliasName ?? string.Empty };
        layout.Controls.Add(_txtAliasName, 1, 0);

        layout.Controls.Add(LabelOf("よみ"), 0, 1);
        _txtAliasKana = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtAliasKana, 1, 1);

        // ── 種別ラジオ ──
        layout.Controls.Add(LabelOf("種別"), 0, 2);
        _rbNewCompany = new RadioButton { Text = "新規企業を作成", Checked = true, AutoSize = true };
        _rbExistingCompany = new RadioButton { Text = "既存企業に追加", AutoSize = true };
        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };
        modeRow.Controls.Add(_rbNewCompany);
        modeRow.Controls.Add(_rbExistingCompany);
        layout.Controls.Add(modeRow, 1, 2);

        // ── 新規企業フィールド ──
        // 既定では「新規企業」モード。屋号名と同じものを既定で企業名にも入れておく。
        layout.Controls.Add(LabelOf("企業名（正式名称）"), 0, 3);
        _txtCompanyName = new TextBox { Dock = DockStyle.Fill, Text = initialAliasName ?? string.Empty };
        layout.Controls.Add(_txtCompanyName, 1, 3);

        layout.Controls.Add(LabelOf("企業よみ"), 0, 4);
        _txtCompanyKana = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtCompanyKana, 1, 4);

        // ── 既存企業ドロップダウン（既存モード時のみ有効） ──
        layout.Controls.Add(LabelOf("既存企業"), 0, 5);
        _cboExistingCompany = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false,
        };
        layout.Controls.Add(_cboExistingCompany, 1, 5);

        // モード切替時に有効/無効を切り替える。
        _rbNewCompany.CheckedChanged += (_, __) =>
        {
            bool isNew = _rbNewCompany.Checked;
            _txtCompanyName.Enabled = isNew;
            _txtCompanyKana.Enabled = isNew;
            _cboExistingCompany.Enabled = !isNew;
            UpdatePreview();
        };
        _txtAliasName.TextChanged += (_, __) =>
        {
            // 屋号名が変わったら新規企業名の初期値も追従させる（ユーザーが企業名を編集していなければ）。
            UpdatePreview();
        };

        // ── プレビュー ──
        layout.Controls.Add(LabelOf("登録内容"), 0, 6);
        _lblPreview = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 60,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Text = "",
        };
        layout.Controls.Add(_lblPreview, 1, 6);

        // ── OK / Cancel ──
        _btnOk = new Button { Text = "登録", AutoSize = true };
        _btnCancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };
        buttonRow.Controls.Add(_btnOk);
        buttonRow.Controls.Add(_btnCancel);
        layout.Controls.Add(new Label(), 0, 7);
        layout.Controls.Add(buttonRow, 1, 7);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _btnOk.Click += async (_, __) => await OnOkAsync();
        Load += async (_, __) => await OnLoadAsync();
    }

    private static Label LabelOf(string text) => new()
    {
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = 24,
    };

    /// <summary>初期表示：既存企業ドロップダウンを埋める + プレビュー更新。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var existing = await _companiesRepo.GetAllAsync();
            _cboExistingCompany.DisplayMember = "Label";
            _cboExistingCompany.ValueMember = "Id";
            _cboExistingCompany.DataSource = existing
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => new IdLabel(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
                .ToList();
        }
        catch
        {
            // 取得失敗時は既存モードを使えないようにする（ラジオを無効化）。
            _rbExistingCompany.Enabled = false;
        }
        UpdatePreview();
        _txtAliasName.SelectAll();
        _txtAliasName.Focus();
    }

    /// <summary>登録ボタン処理：必須チェック → INSERT → 戻り値セット。</summary>
    private async Task OnOkAsync()
    {
        try
        {
            string aliasName = (_txtAliasName.Text ?? "").Trim();
            string? aliasKana = string.IsNullOrWhiteSpace(_txtAliasKana.Text) ? null : _txtAliasKana.Text.Trim();
            if (string.IsNullOrEmpty(aliasName))
            {
                MessageBox.Show(this, "屋号名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtAliasName.Focus();
                return;
            }

            int companyId;
            if (_rbNewCompany.Checked)
            {
                string companyName = (_txtCompanyName.Text ?? "").Trim();
                if (string.IsNullOrEmpty(companyName)) companyName = aliasName;
                string? companyKana = string.IsNullOrWhiteSpace(_txtCompanyKana.Text) ? null : _txtCompanyKana.Text.Trim();
                // 新規企業を作る：companies.name = 正式名称、ない場合は屋号名と同じ。
                var newCompany = new Company
                {
                    Name = companyName,
                    NameKana = companyKana,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName,
                };
                companyId = await _companiesRepo.InsertAsync(newCompany);
            }
            else
            {
                if (_cboExistingCompany.SelectedValue is not int existingId)
                {
                    MessageBox.Show(this, "既存企業を選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _cboExistingCompany.Focus();
                    return;
                }
                companyId = existingId;
            }

            // company_aliases に屋号を INSERT。
            var newAlias = new CompanyAlias
            {
                CompanyId = companyId,
                Name = aliasName,
                NameKana = aliasKana,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName,
            };
            int newAliasId = await _aliasesRepo.InsertAsync(newAlias);

            CreatedAliasId = newAliasId;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdatePreview()
    {
        string aliasName = (_txtAliasName.Text ?? "").Trim();
        if (_rbNewCompany.Checked)
        {
            string companyName = (_txtCompanyName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(companyName)) companyName = aliasName;
            _lblPreview.Text = $"・companies に「{companyName}」を新規登録\n・company_aliases に「{aliasName}」を新規登録（親 = 上記企業）";
        }
        else
        {
            string existing = _cboExistingCompany.SelectedItem is IdLabel sel ? sel.Label : "（未選択）";
            _lblPreview.Text = $"・既存企業「{existing}」に追加\n・company_aliases に「{aliasName}」を新規登録";
        }
    }

    /// <summary>コンボ表示用のシンプルな (Id, Label) ペア。</summary>
    private sealed record IdLabel(int Id, string Label);
}
