using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 人物名義のみで構成される作家連名（song_credits / bgm_cue_credits 両方）を編集する
/// 汎用ダイアログ。
/// 1 つの「対象 + 役」（例: 曲 #123 の作詞）に対する連名行リストを
/// in-memory で編集し、OK 押下時に <see cref="ResultLines"/> プロパティ経由で
/// 呼び出し側に返す。リポジトリ呼び出しは行わない（呼び出し側が
/// <c>ReplaceAllByRoleAsync</c> 等でまとめて保存する）。
/// 会社名義のリポジトリを渡したとき（歌の作家連名）だけ、名義ごとの所属（会社名義または自由記述）の欄を出す。
/// </summary>
public partial class PersonAliasCreditsEditDialog : Form
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    /// <summary>所属の会社名義 picker 用。null のときは所属の欄を出さない（劇伴の作家連名など）。</summary>
    private readonly CompanyAliasesRepository? _companyAliasesRepo;
    private readonly BindingList<LineRow> _lines = new();

    /// <summary>編集確定時の連名行リスト（OK で閉じたとき有効）。</summary>
    public IReadOnlyList<LineDto> ResultLines { get; private set; } = Array.Empty<LineDto>();

    /// <summary>既存値から編集状態を立ち上げる。</summary>
    /// <param name="title">タイトル文字列（例: "作詞クレジット編集"）。</param>
    /// <param name="initialLines">初期表示する連名行（読み取り専用）。空でもよい。</param>
    /// <param name="personAliasesRepo">名義 picker 用リポジトリ。</param>
    /// <param name="companyAliasesRepo">所属の会社名義 picker 用リポジトリ。渡したときだけ所属の欄を出す。</param>
    public PersonAliasCreditsEditDialog(
        string title,
        IReadOnlyList<LineDto> initialLines,
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository? companyAliasesRepo = null)
    {
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _companyAliasesRepo = companyAliasesRepo;

        InitializeComponent();
        Text = title;

        // 所属の欄は歌の作家連名のときだけ使う
        bool withAffiliation = _companyAliasesRepo is not null;
        lblAffiliation.Visible = txtAffiliationCompany.Visible = btnPickAffiliation.Visible = btnClearAffiliation.Visible = withAffiliation;
        lblAffiliationText.Visible = txtAffiliationText.Visible = withAffiliation;
        lblAffiliationMode.Visible = cboAffiliationMode.Visible = withAffiliation;
        cboAffiliationMode.DisplayMember = nameof(AffiliationModeItem.Label);
        cboAffiliationMode.ValueMember = nameof(AffiliationModeItem.Code);
        cboAffiliationMode.DataSource = AffiliationModeItems;
        gridLines.Columns["colAffiliation"]!.Visible = withAffiliation;

        // バインド：BindingList を DataSource として直接バインド
        gridLines.DataSource = _lines;

        // 初期データを流し込み
        byte seq = 1;
        foreach (var l in initialLines)
        {
            _lines.Add(new LineRow
            {
                Seq = seq++,
                AliasId = l.AliasId,
                AliasDisplay = l.AliasDisplay,
                Separator = l.PrecedingSeparator,
                AffiliationCompanyAliasId = l.AffiliationCompanyAliasId,
                AffiliationCompanyDisplay = l.AffiliationCompanyDisplay,
                AffiliationText = l.AffiliationText,
                AffiliationMode = l.AffiliationMode,
                Notes = l.Notes
            });
        }

        // イベント
        gridLines.SelectionChanged += (_, __) => OnGridSelectionChanged();
        btnPickAlias.Click += async (_, __) => await OnPickAliasAsync();
        btnPickAffiliation.Click += async (_, __) => await OnPickAffiliationAsync();
        btnClearAffiliation.Click += (_, __) => SetAffiliationCompany(null, "");
        btnAdd.Click += (_, __) => OnAddNewLine();
        btnApply.Click += (_, __) => OnApplyLine();
        btnDelete.Click += (_, __) => OnDeleteLine();
        btnUp.Click += (_, __) => OnMove(-1);
        btnDown.Click += (_, __) => OnMove(+1);
        btnOk.Click += (_, __) => OnOkClick();

        UpdateDetailFromSelection();
    }

    /// <summary>選択行の内容を詳細編集パネルに流し込む。</summary>
    private void OnGridSelectionChanged() => UpdateDetailFromSelection();

    private void UpdateDetailFromSelection()
    {
        if (gridLines.CurrentRow?.DataBoundItem is LineRow row)
        {
            lblSeqValue.Text = row.Seq.ToString();
            lblSeqValue.ForeColor = SystemColors.ControlText;
            txtAliasDisplay.Text = row.AliasDisplay ?? "";
            txtAliasDisplay.Tag = row.AliasId;
            txtSeparator.Text = row.Separator ?? "";
            txtSeparator.Enabled = row.Seq >= 2;
            SetAffiliationCompany(row.AffiliationCompanyAliasId, row.AffiliationCompanyDisplay ?? "");
            txtAffiliationText.Text = row.AffiliationText ?? "";
            cboAffiliationMode.SelectedValue = row.AffiliationMode ?? SongCreditAffiliationDisplay.NameWithCompany;
            txtNotes.Text = row.Notes ?? "";
        }
        else
        {
            lblSeqValue.Text = "(未選択)";
            lblSeqValue.ForeColor = Color.Gray;
            txtAliasDisplay.Text = "";
            txtAliasDisplay.Tag = null;
            txtSeparator.Text = "";
            txtSeparator.Enabled = true;
            SetAffiliationCompany(null, "");
            txtAffiliationText.Text = "";
            cboAffiliationMode.SelectedValue = SongCreditAffiliationDisplay.NameWithCompany;
            txtNotes.Text = "";
        }
    }

    /// <summary>詳細パネルの所属の会社名義を設定する（null で未設定）。</summary>
    private void SetAffiliationCompany(int? aliasId, string display)
    {
        txtAffiliationCompany.Tag = aliasId;
        txtAffiliationCompany.Text = aliasId is null ? "" : display;
    }

    /// <summary>所属の会社名義 picker を開いて詳細パネルに反映する（まだリストには戻していない）。</summary>
    private async Task OnPickAffiliationAsync()
    {
        if (_companyAliasesRepo is null) return;
        using var dlg = new CompanyAliasPickerDialog(_companyAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        var alias = await _companyAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
        if (alias is null) return;
        SetAffiliationCompany(alias.AliasId, alias.Name);
    }

    /// <summary>詳細パネルの所属の自由記述（空なら null）。</summary>
    private string? AffiliationTextInput()
        => string.IsNullOrWhiteSpace(txtAffiliationText.Text) ? null : txtAffiliationText.Text.Trim();

    /// <summary>詳細パネルで選んでいる所属の出し方（未選択なら既定の名義＋所属）。</summary>
    private string AffiliationModeInput()
        => cboAffiliationMode.SelectedValue as string ?? SongCreditAffiliationDisplay.NameWithCompany;

    /// <summary>名義 picker を開いて選択結果を詳細パネルに反映する（まだリストには戻していない）。</summary>
    private async Task OnPickAliasAsync()
    {
        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        var alias = await _personAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
        if (alias is null) return;

        txtAliasDisplay.Text = alias.GetDisplayName();
        txtAliasDisplay.Tag = alias.AliasId;
    }

    /// <summary>新規行をリスト末尾に追加し、選択中にする。</summary>
    private void OnAddNewLine()
    {
        if (txtAliasDisplay.Tag is not int aliasId)
        {
            MessageBox.Show("名義が選択されていません。「選択...」から名義を選んでください。", "未入力",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        byte newSeq = (byte)(_lines.Count + 1);
        var row = new LineRow
        {
            Seq = newSeq,
            AliasId = aliasId,
            AliasDisplay = txtAliasDisplay.Text,
            // seq=1 では区切りは無効化されているが、念のため強制 NULL
            Separator = newSeq >= 2 ? (string.IsNullOrWhiteSpace(txtSeparator.Text) ? null : txtSeparator.Text) : null,
            AffiliationCompanyAliasId = txtAffiliationCompany.Tag as int?,
            AffiliationCompanyDisplay = txtAffiliationCompany.Tag is int ? txtAffiliationCompany.Text : null,
            AffiliationText = AffiliationTextInput(),
            AffiliationMode = AffiliationModeInput(),
            Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text
        };
        _lines.Add(row);

        // 末尾の追加行を選択
        if (gridLines.Rows.Count > 0)
            gridLines.CurrentCell = gridLines.Rows[gridLines.Rows.Count - 1].Cells[0];
    }

    /// <summary>選択中行に詳細パネルの内容を反映する（編集の保存）。</summary>
    private void OnApplyLine()
    {
        if (gridLines.CurrentRow?.DataBoundItem is not LineRow row) return;
        if (txtAliasDisplay.Tag is not int aliasId)
        {
            MessageBox.Show("名義が選択されていません。", "未入力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        row.AliasId = aliasId;
        row.AliasDisplay = txtAliasDisplay.Text;
        row.Separator = row.Seq >= 2
            ? (string.IsNullOrWhiteSpace(txtSeparator.Text) ? null : txtSeparator.Text)
            : null;
        row.AffiliationCompanyAliasId = txtAffiliationCompany.Tag as int?;
        row.AffiliationCompanyDisplay = txtAffiliationCompany.Tag is int ? txtAffiliationCompany.Text : null;
        row.AffiliationText = AffiliationTextInput();
        row.AffiliationMode = AffiliationModeInput();
        row.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text;
        _lines.ResetItem(_lines.IndexOf(row));
    }

    /// <summary>選択行を削除し、seq を 1 から振り直す。</summary>
    private void OnDeleteLine()
    {
        if (gridLines.CurrentRow?.DataBoundItem is not LineRow row) return;
        _lines.Remove(row);
        ReassignSeqs();
    }

    /// <summary>選択行を上下に移動し、seq を振り直す。</summary>
    private void OnMove(int direction)
    {
        if (gridLines.CurrentRow?.DataBoundItem is not LineRow row) return;
        int idx = _lines.IndexOf(row);
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= _lines.Count) return;
        _lines.RemoveAt(idx);
        _lines.Insert(newIdx, row);
        ReassignSeqs();
        gridLines.CurrentCell = gridLines.Rows[newIdx].Cells[0];
    }

    /// <summary>並び順変更後に seq を 1..N に振り直す（seq=1 は区切り強制 NULL）。</summary>
    private void ReassignSeqs()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            _lines[i].Seq = (byte)(i + 1);
            if (_lines[i].Seq == 1) _lines[i].Separator = null;
        }
        // バインドを完全リセット（個別 ResetItem だと先頭セル選択が外れることがある）
        gridLines.DataSource = null;
        gridLines.DataSource = _lines;
    }

    /// <summary>OK で閉じる前に、結果を <see cref="ResultLines"/> に確定する。</summary>
    private void OnOkClick()
    {
        ResultLines = _lines.Select(l => new LineDto
        {
            AliasId = l.AliasId,
            AliasDisplay = l.AliasDisplay ?? "",
            PrecedingSeparator = l.Separator,
            AffiliationCompanyAliasId = l.AffiliationCompanyAliasId,
            AffiliationCompanyDisplay = l.AffiliationCompanyDisplay,
            AffiliationText = l.AffiliationText,
            AffiliationMode = l.AffiliationMode,
            Notes = l.Notes
        }).ToList();
    }

    /// <summary>所属の出し方の選択肢（song_credits.affiliation_display の値と表示名）。</summary>
    private static readonly List<AffiliationModeItem> AffiliationModeItems = new()
    {
        new(SongCreditAffiliationDisplay.NameWithCompany, "名義＋所属（既定）"),
        new(SongCreditAffiliationDisplay.CompanyOnly, "所属の会社名だけ"),
        new(SongCreditAffiliationDisplay.NameOnly, "名義だけ（所属は持つ）"),
    };

    /// <summary>所属の出し方のコンボ表示用。</summary>
    private sealed record AffiliationModeItem(string Code, string Label);

    /// <summary>呼び出し側との受け渡し用 DTO。</summary>
    public sealed class LineDto
    {
        public int AliasId { get; set; }
        public string AliasDisplay { get; set; } = "";
        /// <summary>seq>=2 のとき有効。seq=1 では NULL を呼び出し側で扱う。</summary>
        public string? PrecedingSeparator { get; set; }
        /// <summary>所属の会社名義（→ company_aliases.alias_id）。所属の欄を出すときだけ使う。</summary>
        public int? AffiliationCompanyAliasId { get; set; }
        /// <summary>所属の会社名義の表示名（グリッド・詳細パネル表示用）。</summary>
        public string? AffiliationCompanyDisplay { get; set; }
        /// <summary>会社マスタに無い所属の自由記述。</summary>
        public string? AffiliationText { get; set; }
        /// <summary>所属の出し方（song_credits.affiliation_display の値。null は既定の名義＋所属）。</summary>
        public string? AffiliationMode { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>DataGridView バインド用の内部行クラス。</summary>
    private sealed class LineRow
    {
        public byte Seq { get; set; }
        public int AliasId { get; set; }
        public string? AliasDisplay { get; set; }
        public string? Separator { get; set; }
        public int? AffiliationCompanyAliasId { get; set; }
        public string? AffiliationCompanyDisplay { get; set; }
        public string? AffiliationText { get; set; }
        /// <summary>所属の出し方（song_credits.affiliation_display の値。null は既定の名義＋所属）。</summary>
        public string? AffiliationMode { get; set; }
        /// <summary>グリッドの「所属」列の表示（会社名義を優先し、無ければ自由記述）。出し方が既定でなければ〔会社名だけ〕〔名義だけ〕を添える。</summary>
        public string AffiliationDisplay
        {
            get
            {
                string aff = AffiliationCompanyAliasId is not null ? AffiliationCompanyDisplay ?? "" : AffiliationText ?? "";
                return AffiliationMode switch
                {
                    SongCreditAffiliationDisplay.CompanyOnly => aff + "〔会社名だけ〕",
                    SongCreditAffiliationDisplay.NameOnly => aff + "〔名義だけ〕",
                    _ => aff
                };
            }
        }
        public string? Notes { get; set; }
    }
}
