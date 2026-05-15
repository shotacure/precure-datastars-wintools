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
/// 歌唱者連名（song_recording_singers）専用の編集ダイアログ。
/// <para>
/// 1 録音に対する歌唱者行リストを in-memory で編集し、OK 押下時に
/// <see cref="ResultLines"/> プロパティ経由で呼び出し側に返す。
/// billing_kind は 2 値（PERSON / CHARACTER_WITH_CV）。
/// </para>
/// </summary>
public partial class SongRecordingSingersEditDialog : Form
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly BindingList<LineRow> _lines = new();

    /// <summary>編集確定時の連名行リスト（OK で閉じたとき有効）。</summary>
    public IReadOnlyList<LineDto> ResultLines { get; private set; } = Array.Empty<LineDto>();

    public SongRecordingSingersEditDialog(
        IReadOnlyList<LineDto> initialLines,
        PersonAliasesRepository personAliasesRepo,
        CharacterAliasesRepository characterAliasesRepo)
    {
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));

        InitializeComponent();
        gridLines.DataSource = _lines;

        byte seq = 1;
        foreach (var l in initialLines)
        {
            _lines.Add(new LineRow
            {
                Seq = seq++,
                Kind = l.BillingKind,
                PersonAliasId = l.PersonAliasId,
                PersonDisplay = l.PersonDisplay,
                CharacterAliasId = l.CharacterAliasId,
                CharacterDisplay = l.CharacterDisplay,
                VoicePersonAliasId = l.VoicePersonAliasId,
                VoiceDisplay = l.VoiceDisplay,
                SlashPersonAliasId = l.SlashPersonAliasId,
                SlashPersonDisplay = l.SlashPersonDisplay,
                SlashCharacterAliasId = l.SlashCharacterAliasId,
                SlashCharacterDisplay = l.SlashCharacterDisplay,
                Separator = l.PrecedingSeparator,
                AffiliationText = l.AffiliationText,
                Notes = l.Notes
            });
        }
        foreach (var r in _lines) r.RecomputeFullDisplay();

        // イベント接続
        gridLines.SelectionChanged += (_, __) => UpdateDetailFromSelection();
        cboKind.SelectedIndexChanged += (_, __) => RefreshKindEnable();
        btnPickPerson.Click += async (_, __) => await OnPickAliasAsync(txtPersonDisplay, isPerson: true);
        btnPickSlashPerson.Click += async (_, __) => await OnPickAliasAsync(txtSlashPersonDisplay, isPerson: true);
        btnClearSlashPerson.Click += (_, __) => { txtSlashPersonDisplay.Text = ""; txtSlashPersonDisplay.Tag = null; };
        btnPickCharacter.Click += async (_, __) => await OnPickAliasAsync(txtCharacterDisplay, isPerson: false);
        btnPickVoice.Click += async (_, __) => await OnPickAliasAsync(txtVoiceDisplay, isPerson: true);
        btnPickSlashCharacter.Click += async (_, __) => await OnPickAliasAsync(txtSlashCharacterDisplay, isPerson: false);
        btnClearSlashCharacter.Click += (_, __) => { txtSlashCharacterDisplay.Text = ""; txtSlashCharacterDisplay.Tag = null; };
        btnAdd.Click += (_, __) => OnAddNewLine();
        btnApply.Click += (_, __) => OnApplyLine();
        btnDelete.Click += (_, __) => OnDeleteLine();
        btnUp.Click += (_, __) => OnMove(-1);
        btnDown.Click += (_, __) => OnMove(+1);
        btnOk.Click += (_, __) => OnOkClick();

        // 初期選択（最初の行があればそれを、無ければ「PERSON で新規作成準備」状態に）
        if (cboKind.SelectedIndex < 0) cboKind.SelectedIndex = 0;
        UpdateDetailFromSelection();
    }

    /// <summary>
    /// 名義 picker（人物 or キャラ）を開いて、対象 TextBox に表示名を、Tag に alias_id をセットする。
    /// </summary>
    private async Task OnPickAliasAsync(TextBox target, bool isPerson)
    {
        if (isPerson)
        {
            using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            var alias = await _personAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
            if (alias is null) return;
            target.Text = alias.GetDisplayName();
            target.Tag = alias.AliasId;
        }
        else
        {
            using var dlg = new CharacterAliasPickerDialog(_characterAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            var alias = await _characterAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
            if (alias is null) return;
            target.Text = alias.Name; // character_aliases には display_text_override 列なし
            target.Tag = alias.AliasId;
        }
    }

    /// <summary>billing_kind に応じて関連入力欄を有効化／無効化する。</summary>
    private void RefreshKindEnable()
    {
        bool isPerson = (cboKind.SelectedItem as string) == "PERSON";
        txtPersonDisplay.Enabled = btnPickPerson.Enabled = isPerson;
        txtSlashPersonDisplay.Enabled = btnPickSlashPerson.Enabled = btnClearSlashPerson.Enabled = isPerson;
        txtCharacterDisplay.Enabled = btnPickCharacter.Enabled = !isPerson;
        txtVoiceDisplay.Enabled = btnPickVoice.Enabled = !isPerson;
        txtSlashCharacterDisplay.Enabled = btnPickSlashCharacter.Enabled = btnClearSlashCharacter.Enabled = !isPerson;
    }

    /// <summary>選択行の値を詳細パネルに流し込む。</summary>
    private void UpdateDetailFromSelection()
    {
        if (gridLines.CurrentRow?.DataBoundItem is LineRow row)
        {
            lblSeqValue.Text = row.Seq.ToString();
            lblSeqValue.ForeColor = SystemColors.ControlText;
            cboKind.SelectedItem = row.Kind == SingerBillingKind.Person ? "PERSON" : "CHARACTER_WITH_CV";

            txtPersonDisplay.Text = row.PersonDisplay ?? "";
            txtPersonDisplay.Tag = row.PersonAliasId;
            txtCharacterDisplay.Text = row.CharacterDisplay ?? "";
            txtCharacterDisplay.Tag = row.CharacterAliasId;
            txtVoiceDisplay.Text = row.VoiceDisplay ?? "";
            txtVoiceDisplay.Tag = row.VoicePersonAliasId;

            txtSlashPersonDisplay.Text = row.SlashPersonDisplay ?? "";
            txtSlashPersonDisplay.Tag = row.SlashPersonAliasId;
            txtSlashCharacterDisplay.Text = row.SlashCharacterDisplay ?? "";
            txtSlashCharacterDisplay.Tag = row.SlashCharacterAliasId;

            txtSeparator.Text = row.Separator ?? "";
            txtSeparator.Enabled = row.Seq >= 2;
            txtAffiliation.Text = row.AffiliationText ?? "";
            txtNotes.Text = row.Notes ?? "";
        }
        else
        {
            lblSeqValue.Text = "(未選択)";
            lblSeqValue.ForeColor = Color.Gray;
            txtPersonDisplay.Text = ""; txtPersonDisplay.Tag = null;
            txtCharacterDisplay.Text = ""; txtCharacterDisplay.Tag = null;
            txtVoiceDisplay.Text = ""; txtVoiceDisplay.Tag = null;
            txtSlashPersonDisplay.Text = ""; txtSlashPersonDisplay.Tag = null;
            txtSlashCharacterDisplay.Text = ""; txtSlashCharacterDisplay.Tag = null;
            txtSeparator.Text = "";
            txtSeparator.Enabled = true;
            txtAffiliation.Text = "";
            txtNotes.Text = "";
        }
        RefreshKindEnable();
    }

    /// <summary>
    /// 詳細パネルの内容から、新規行を作って末尾に追加する。
    /// 入力チェック：必須項目（PERSON なら主名義、CHARACTER_WITH_CV なら主キャラと CV）が無ければ拒否。
    /// </summary>
    private void OnAddNewLine()
    {
        if (!TryBuildLine(out var newLine, isAdd: true)) return;
        newLine.Seq = (byte)(_lines.Count + 1);
        if (newLine.Seq < 2) newLine.Separator = null;
        newLine.RecomputeFullDisplay();
        _lines.Add(newLine);

        if (gridLines.Rows.Count > 0)
            gridLines.CurrentCell = gridLines.Rows[gridLines.Rows.Count - 1].Cells[0];
    }

    /// <summary>選択中行に詳細パネルの値を反映する（編集の保存）。</summary>
    private void OnApplyLine()
    {
        if (gridLines.CurrentRow?.DataBoundItem is not LineRow row) return;
        if (!TryBuildLine(out var updated, isAdd: false)) return;

        row.Kind = updated.Kind;
        row.PersonAliasId = updated.PersonAliasId; row.PersonDisplay = updated.PersonDisplay;
        row.CharacterAliasId = updated.CharacterAliasId; row.CharacterDisplay = updated.CharacterDisplay;
        row.VoicePersonAliasId = updated.VoicePersonAliasId; row.VoiceDisplay = updated.VoiceDisplay;
        row.SlashPersonAliasId = updated.SlashPersonAliasId; row.SlashPersonDisplay = updated.SlashPersonDisplay;
        row.SlashCharacterAliasId = updated.SlashCharacterAliasId; row.SlashCharacterDisplay = updated.SlashCharacterDisplay;
        row.Separator = row.Seq >= 2 ? updated.Separator : null;
        row.AffiliationText = updated.AffiliationText;
        row.Notes = updated.Notes;
        row.RecomputeFullDisplay();

        _lines.ResetItem(_lines.IndexOf(row));
    }

    /// <summary>詳細パネルの値を読み取り、整合性をチェックして新行 LineRow を返す。</summary>
    private bool TryBuildLine(out LineRow line, bool isAdd)
    {
        line = new LineRow();
        bool isPerson = (cboKind.SelectedItem as string) == "PERSON";
        line.Kind = isPerson ? SingerBillingKind.Person : SingerBillingKind.CharacterWithCv;

        if (isPerson)
        {
            if (txtPersonDisplay.Tag is not int pid)
            {
                ShowInfo("主名義（PERSON）が選択されていません。");
                return false;
            }
            line.PersonAliasId = pid;
            line.PersonDisplay = txtPersonDisplay.Text;
            line.SlashPersonAliasId = txtSlashPersonDisplay.Tag as int?;
            line.SlashPersonDisplay = string.IsNullOrEmpty(txtSlashPersonDisplay.Text) ? null : txtSlashPersonDisplay.Text;
        }
        else
        {
            if (txtCharacterDisplay.Tag is not int cid)
            {
                ShowInfo("主キャラ（CHARACTER）が選択されていません。");
                return false;
            }
            if (txtVoiceDisplay.Tag is not int vid)
            {
                ShowInfo("CV（声優）が選択されていません。CHARACTER_WITH_CV では CV が必須です。");
                return false;
            }
            line.CharacterAliasId = cid;
            line.CharacterDisplay = txtCharacterDisplay.Text;
            line.VoicePersonAliasId = vid;
            line.VoiceDisplay = txtVoiceDisplay.Text;
            line.SlashCharacterAliasId = txtSlashCharacterDisplay.Tag as int?;
            line.SlashCharacterDisplay = string.IsNullOrEmpty(txtSlashCharacterDisplay.Text) ? null : txtSlashCharacterDisplay.Text;
        }

        line.Separator = string.IsNullOrWhiteSpace(txtSeparator.Text) ? null : txtSeparator.Text;
        line.AffiliationText = string.IsNullOrWhiteSpace(txtAffiliation.Text) ? null : txtAffiliation.Text;
        line.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text;
        return true;
    }

    private void OnDeleteLine()
    {
        if (gridLines.CurrentRow?.DataBoundItem is not LineRow row) return;
        _lines.Remove(row);
        ReassignSeqs();
    }

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

    private void ReassignSeqs()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            _lines[i].Seq = (byte)(i + 1);
            if (_lines[i].Seq == 1) _lines[i].Separator = null;
        }
        gridLines.DataSource = null;
        gridLines.DataSource = _lines;
    }

    private void OnOkClick()
    {
        ResultLines = _lines.Select(l => new LineDto
        {
            BillingKind = l.Kind,
            PersonAliasId = l.PersonAliasId,
            PersonDisplay = l.PersonDisplay,
            CharacterAliasId = l.CharacterAliasId,
            CharacterDisplay = l.CharacterDisplay,
            VoicePersonAliasId = l.VoicePersonAliasId,
            VoiceDisplay = l.VoiceDisplay,
            SlashPersonAliasId = l.SlashPersonAliasId,
            SlashPersonDisplay = l.SlashPersonDisplay,
            SlashCharacterAliasId = l.SlashCharacterAliasId,
            SlashCharacterDisplay = l.SlashCharacterDisplay,
            PrecedingSeparator = l.Separator,
            AffiliationText = l.AffiliationText,
            Notes = l.Notes
        }).ToList();
    }

    private void ShowInfo(string msg)
        => MessageBox.Show(msg, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>呼び出し側との受け渡し DTO。</summary>
    public sealed class LineDto
    {
        public SingerBillingKind BillingKind { get; set; }
        public int? PersonAliasId { get; set; }
        public string? PersonDisplay { get; set; }
        public int? CharacterAliasId { get; set; }
        public string? CharacterDisplay { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public string? VoiceDisplay { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public string? SlashPersonDisplay { get; set; }
        public int? SlashCharacterAliasId { get; set; }
        public string? SlashCharacterDisplay { get; set; }
        public string? PrecedingSeparator { get; set; }
        public string? AffiliationText { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>DataGridView バインド用内部行クラス。</summary>
    private sealed class LineRow
    {
        public byte Seq { get; set; }
        public SingerBillingKind Kind { get; set; }
        public int? PersonAliasId { get; set; }
        public string? PersonDisplay { get; set; }
        public int? CharacterAliasId { get; set; }
        public string? CharacterDisplay { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public string? VoiceDisplay { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public string? SlashPersonDisplay { get; set; }
        public int? SlashCharacterAliasId { get; set; }
        public string? SlashCharacterDisplay { get; set; }
        public string? Separator { get; set; }
        public string? AffiliationText { get; set; }
        public string? Notes { get; set; }

        /// <summary>グリッド表示用の Kind 文字列。</summary>
        public string KindLabel => Kind == SingerBillingKind.Person ? "PERSON" : "CHARACTER_WITH_CV";

        /// <summary>グリッドに 1 行で出す結合表示テキスト。</summary>
        public string FullDisplay { get; private set; } = "";

        /// <summary>各種値変更時に呼び出して FullDisplay を更新する。</summary>
        public void RecomputeFullDisplay()
        {
            var sb = new System.Text.StringBuilder();
            if (Kind == SingerBillingKind.Person)
            {
                sb.Append(PersonDisplay ?? "");
                if (!string.IsNullOrEmpty(SlashPersonDisplay)) sb.Append(" / ").Append(SlashPersonDisplay);
            }
            else
            {
                sb.Append(CharacterDisplay ?? "");
                if (!string.IsNullOrEmpty(SlashCharacterDisplay)) sb.Append(" / ").Append(SlashCharacterDisplay);
                if (!string.IsNullOrEmpty(VoiceDisplay)) sb.Append("(CV:").Append(VoiceDisplay).Append(')');
            }
            if (!string.IsNullOrEmpty(AffiliationText)) sb.Append(' ').Append(AffiliationText);
            FullDisplay = sb.ToString();
        }
    }
}