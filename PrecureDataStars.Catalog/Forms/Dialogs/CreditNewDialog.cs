using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 新規クレジット作成ダイアログ（v1.2.0 工程 B-2 追加）。
/// <para>
/// scope_kind と series_id / episode_id は呼び出し側で確定済みの状態で渡され、
/// 本ダイアログでは「OP/ED の選択 / presentation / 本放送限定フラグ / part_type / notes」のみを
/// 入力する。OK 押下時に <see cref="Result"/> プロパティに値が組まれた <see cref="Credit"/> を
/// 返すが、DB への INSERT は呼び出し側で行う（責務分離のため）。
/// </para>
/// <para>
/// part_type コンボには <c>part_types</c> マスタ全件＋先頭の「（規定位置）」を流し込む。
/// 「（規定位置）」を選んだ場合は <see cref="Credit.PartType"/> = null となる。
/// </para>
/// </summary>
public partial class CreditNewDialog : Form
{
    private readonly PartTypesRepository _partTypesRepo;

    private readonly string _scopeKind;
    private readonly int? _seriesId;
    private readonly int? _episodeId;

    /// <summary>OK 押下時に組まれる結果（credit_id は未割り当て、INSERT は呼び出し側で行う）。</summary>
    public Credit? Result { get; private set; }

    /// <summary>
    /// 新規クレジット作成ダイアログのコンストラクタ。
    /// </summary>
    /// <param name="partTypesRepo">part_types マスタを引くリポジトリ。</param>
    /// <param name="scopeKind">"SERIES" or "EPISODE"。</param>
    /// <param name="seriesId">scope=SERIES のときの対象シリーズ ID（scope=EPISODE のときは null）。</param>
    /// <param name="episodeId">scope=EPISODE のときの対象エピソード ID（scope=SERIES のときは null）。</param>
    /// <param name="targetLabel">「作成対象」欄に表示する人間可読ラベル（例：「第3話 友達100人」）。</param>
    public CreditNewDialog(
        PartTypesRepository partTypesRepo,
        string scopeKind,
        int? seriesId,
        int? episodeId,
        string targetLabel)
    {
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _scopeKind = scopeKind;
        _seriesId = seriesId;
        _episodeId = episodeId;

        InitializeComponent();
        lblTargetValue.Text = targetLabel;

        btnOk.Click += (_, __) => OnOkClick();
        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>part_type コンボ初期化（先頭に「（規定位置）」=空コードを置く）。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var pts = await _partTypesRepo.GetAllAsync();
            var items = new List<CodeLabel> { new CodeLabel("", "（規定位置）") };
            items.AddRange(pts.Select(p => new CodeLabel(p.PartTypeCode, $"{p.PartTypeCode}  {p.NameJa}")));
            cboPartType.DisplayMember = "Label";
            cboPartType.ValueMember = "Code";
            cboPartType.DataSource = items;
            cboPartType.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>OK ボタン処理：入力値を Credit に詰めて <see cref="Result"/> にセット。</summary>
    private void OnOkClick()
    {
        // 入力値を Credit インスタンスに転写（credit_id は AUTO_INCREMENT のため 0 のまま）
        Result = new Credit
        {
            ScopeKind = _scopeKind,
            SeriesId = _seriesId,
            EpisodeId = _episodeId,
            IsBroadcastOnly = chkBroadcastOnly.Checked,
            CreditKind = rbKindOp.Checked ? "OP" : "ED",
            PartType = (cboPartType.SelectedValue as string) is { Length: > 0 } code ? code : null,
            Presentation = rbPresentationCards.Checked ? "CARDS" : "ROLL",
            Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim(),
            CreatedBy = Environment.UserName,
            UpdatedBy = Environment.UserName
        };
    }

    private sealed class CodeLabel
    {
        public string Code { get; }
        public string Label { get; }
        public CodeLabel(string code, string label) { Code = code; Label = label; }
        public override string ToString() => Label;
    }
}
