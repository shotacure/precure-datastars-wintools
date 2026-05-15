using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット話数コピー先選択ダイアログ。
/// <para>
/// 既存クレジット（コピー元）を別シリーズ／別エピソードへ丸ごと複製する際に、コピー先を選ぶ UI。
/// </para>
/// <para>
/// クレジット種別（OP / ED など）はコピー元と同じものに固定（変更不可）。コピー元が OP なら
/// コピー先も OP を作る、という運用前提（OP を ED として作るような操作は意味が無いため、
/// ゼロから新規作成してもらう想定）。
/// </para>
/// <para>
/// scope_kind は「EPISODE 固定」とする。SERIES スコープのクレジットを別シリーズへコピーするのは
/// 設計上稀な操作なので、本ダイアログでは扱わない（必要なら個別対応）。エピソード選択時にシリーズ
/// 跨ぎが可能。
/// </para>
/// <para>
/// presentation / part_type / 備考はコピー元の値で初期表示、ダイアログ上で変更可能。
/// presentation を ROLL に変える場合、コピー元のカードが 2 枚以上あると後段のコピー処理側で
/// 拒否される可能性があるが、本ダイアログ自体ではバリデーションせず素直にユーザー入力を返す。
/// </para>
/// </summary>
public partial class CreditCopyDialog : Form
{
    private readonly EpisodesRepository _episodesRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly PartTypesRepository _partTypesRepo;

    private readonly Credit _srcCredit;

    /// <summary>OK 押下時に組まれる結果（コピー先クレジット本体の値が設定された <see cref="Credit"/>）。</summary>
    public Credit? Result { get; private set; }

    /// <summary>
    /// OK 押下時のコピー先シリーズ ID（cboSeries で選ばれた値）。<see cref="Credit.SeriesId"/> は
    /// EPISODE スコープでは null になるため、UI 側でシリーズコンボを切り替える用途には別途これを使う。
    /// で追加。
    /// </summary>
    public int? ResultSeriesId { get; private set; }

    public CreditCopyDialog(
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo,
        PartTypesRepository partTypesRepo,
        Credit srcCredit)
    {
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _srcCredit = srcCredit ?? throw new ArgumentNullException(nameof(srcCredit));

        InitializeComponent();

        btnOk.Click += async (_, __) => await OnOkAsync();
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
        cboSeries.SelectedIndexChanged += async (_, __) => await OnSeriesChangedAsync();

        Load += async (_, __) => await OnLoadAsync();
    }

    private async Task OnLoadAsync()
    {
        try
        {
            // コピー元情報を表示
            lblSrcInfo.Text =
                $"コピー元: クレジット #{_srcCredit.CreditId}  種別={_srcCredit.CreditKind}  形式={_srcCredit.Presentation}";

            // クレジット種別はコピー元と同じで固定（表示のみ）
            lblFixedCreditKind.Text = $"クレジット種別（固定）: {_srcCredit.CreditKind}";

            // シリーズコンボ全件流し込み
            var allSeries = await _seriesRepo.GetAllAsync();
            cboSeries.DisplayMember = "Label";
            cboSeries.ValueMember = "Id";
            cboSeries.DataSource = allSeries
                .Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}"))
                .ToList();

            // part_type コンボ
            var pts = await _partTypesRepo.GetAllAsync();
            var ptItems = new List<CodeLabel> { new CodeLabel("", "（規定位置）") };
            ptItems.AddRange(pts.Select(p => new CodeLabel(p.PartTypeCode, $"{p.PartTypeCode}  {p.NameJa}")));
            cboPartType.DisplayMember = "Label";
            cboPartType.ValueMember = "Code";
            cboPartType.DataSource = ptItems;
            cboPartType.SelectedValue = _srcCredit.PartType ?? "";

            // presentation の初期値はコピー元の値
            rbPresentationCards.Checked = (_srcCredit.Presentation == "CARDS");
            rbPresentationRoll.Checked  = (_srcCredit.Presentation == "ROLL");

            // 備考はコピー元の値
            txtNotes.Text = _srcCredit.Notes ?? "";

            // 初期シリーズ選択 → エピソードコンボを連動
            await OnSeriesChangedAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>シリーズ変更時：そのシリーズに属するエピソードでコンボを再構築する。</summary>
    private async Task OnSeriesChangedAsync()
    {
        try
        {
            if (cboSeries.SelectedValue is not int seriesId)
            {
                cboEpisode.DataSource = null;
                return;
            }
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            cboEpisode.DisplayMember = "Label";
            cboEpisode.ValueMember = "Id";
            cboEpisode.DataSource = eps
                .Select(e => new IdLabel(e.EpisodeId, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Task OnOkAsync()
    {
        try
        {
            if (cboEpisode.SelectedValue is not int episodeId)
            {
                MessageBox.Show(this, "コピー先エピソードを選択してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.CompletedTask;
            }
            string presentation = rbPresentationCards.Checked ? "CARDS" : "ROLL";
            string? partType = (cboPartType.SelectedValue as string) is { Length: > 0 } code ? code : null;
            string? notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim();

            // コピー先クレジット本体の値を組み立てる（CreditId は未採番、INSERT は呼び出し側に任せる）。
            Result = new Credit
            {
                CreditId = 0,
                ScopeKind = "EPISODE",     // 本ダイアログでは EPISODE 固定
                SeriesId = null,
                EpisodeId = episodeId,
                CreditKind = _srcCredit.CreditKind,   // 固定
                Presentation = presentation,
                PartType = partType,
                Notes = notes,
                IsDeleted = false,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            // コピー先シリーズ ID（呼び出し元で UI を更新するために使う）
            if (cboSeries.SelectedValue is int seriesIdSel) ResultSeriesId = seriesIdSel;
            DialogResult = DialogResult.OK;
            Close();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Task.CompletedTask;
        }
    }
}