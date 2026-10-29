using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Common.Services;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 配信音源（アートトラック）取り込みの確認ダイアログ。
/// プレイリストの並びと DB のトラック順の対応を 1 行ずつ見せ、書き込む前に人が確かめられるようにする。
/// <para>
/// 対応付けはタイトルの類似度と再生時間による系列アライメントで決めており、完全一致は必須にしていない。
/// 判断材料として DB 側とプレイリスト側のタイトル・尺を並べ、尺の差も出す。
/// 「配信なし」は配信対象外の曲として正常に起こる状態で、その行は動画 ID を持たないまま残る。
/// </para>
/// <para>
/// 自動の対応付けが外れたときに直せるよう、動画 ID 列は手で書き換えられる（空にすれば割り当て解除）。
/// ずれが連続している場合に 1 件ずつ直すのは現実的でないので、選択行から下の割り当てをまとめて
/// 上下にずらすボタンも置いてある。
/// </para>
/// <para>
/// YouTube Music Premium 会員限定かどうかは Data API では分からないので、人が YouTube で
/// その盤の曲を 1 つ開いて確かめ、下段のチェック欄で盤ごとに決める。
/// </para>
/// </summary>
public sealed class ArtTrackImportPreviewDialog : Form
{
    private readonly ArtTrackImportPreview _preview;
    private DataGridView _grid = null!;
    private Label _lblHead = null!;
    private readonly string _productTitle;

    private const int ColPosition = 0;
    private const int ColTrack = 1;
    private const int ColDbTitle = 2;
    private const int ColDbLength = 3;
    private const int ColYouTubeTitle = 4;
    private const int ColVideoLength = 5;
    private const int ColDiff = 6;
    private const int ColVideoId = 7;
    private const int ColStatus = 8;

    /// <summary><see cref="ArtTrackImportPreviewDialog"/> の新しいインスタンスを生成する。</summary>
    /// <param name="preview">表示する取り込みプレビュー。</param>
    /// <param name="productTitle">確認しやすいよう見出しに出す商品名。</param>
    public ArtTrackImportPreviewDialog(ArtTrackImportPreview preview, string productTitle)
    {
        _preview = preview;
        _productTitle = productTitle;

        // 人が対応表を見て書き込む経路なので、会員限定かどうかも未確認のままにせず決めて書く。
        // 既定は DB にある前回の判断で、無ければ「会員限定ではない」。
        _preview.PremiumOnly ??= false;

        Text = "配信音源の取り込み確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 660);
        MinimumSize = new Size(800, 420);
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;

        // 見出し・グリッド・ボタン列の 3 段を TableLayoutPanel で明示的に配置する。
        // Dock だけで組むと重なり順しだいでボタン列がグリッドに隠れてしまうため、
        // 行の高さを宣言して取り合いが起きない形にする。
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblHead = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 8)
        };

        _grid = BuildGrid();
        _grid.Margin = new Padding(6, 0, 6, 0);
        FillGrid();

        var pnlButtons = BuildButtonRow(out var btnApply, out var btnCancel);

        layout.Controls.Add(_lblHead, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(pnlButtons, 0, 2);
        Controls.Add(layout);

        AcceptButton = btnApply;
        CancelButton = btnCancel;
    }

    /// <summary>見出しの文言を組み立て直す。</summary>
    private void UpdateHead()
    {
        if (_lblHead is null) return;
        _lblHead.Text = _productTitle + Environment.NewLine + _preview.SummaryText;
    }

    /// <summary>下段のボタン列。左に会員限定のチェック欄とずらし操作、右に確定・取り消しを置く。</summary>
    private FlowLayoutPanel BuildButtonRow(out Button btnApply, out Button btnCancel)
    {
        var pnl = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 8, 10, 10)
        };

        btnCancel = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 30),
            Margin = new Padding(6, 0, 0, 0)
        };
        var apply = new Button
        {
            Text = "書き込む",
            DialogResult = DialogResult.OK,
            Size = new Size(130, 30),
            Margin = new Padding(6, 0, 0, 0)
        };
        btnApply = apply;

        // ずらし操作。選択行から下の動画割り当てを 1 つ分だけ動かす。
        // 自動の対応付けが途中から丸ごとずれたときのリカバリ手段。
        var btnShiftUp = new Button { Text = "↑ 選択行から詰める", Size = new Size(150, 30), Margin = new Padding(6, 0, 40, 0) };
        var btnShiftDown = new Button { Text = "↓ 選択行から送る", Size = new Size(150, 30), Margin = new Padding(6, 0, 0, 0) };
        var btnClear = new Button { Text = "選択行の割り当て解除", Size = new Size(160, 30), Margin = new Padding(6, 0, 0, 0) };

        btnShiftUp.Click += (_, __) => ShiftAssignments(-1);
        btnShiftDown.Click += (_, __) => ShiftAssignments(+1);
        btnClear.Click += (_, __) => ClearSelectedAssignment();

        // 会員限定かどうかは盤ごとの配信条件なので、行ごとではなく盤に 1 つのチェック欄で決める。
        var chkPremium = new CheckBox
        {
            Text = "この盤は YouTube Music Premium 会員限定",
            AutoSize = true,
            Checked = _preview.PremiumOnly == true,
            Margin = new Padding(6, 6, 40, 0)
        };
        new ToolTip().SetToolTip(chkPremium,
            "YouTube でこの盤の曲を 1 つ開き、「Music Premium のメンバーのみ」と出たらチェックします。");
        chkPremium.CheckedChanged += (_, __) =>
        {
            _preview.PremiumOnly = chkPremium.Checked;
            FillGrid();
        };

        pnl.Controls.Add(btnCancel);
        pnl.Controls.Add(apply);
        pnl.Controls.Add(btnShiftDown);
        pnl.Controls.Add(btnShiftUp);
        pnl.Controls.Add(btnClear);
        pnl.Controls.Add(chkPremium);
        return pnl;
    }

    /// <summary>対応表グリッドを組み立てる。動画 ID 列だけ編集可能にして、手で対応を直せるようにする。</summary>
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };

        grid.Columns.Add(ReadOnlyCol("Position", "#", 40));
        grid.Columns.Add(ReadOnlyCol("Track", "トラック", 130));
        grid.Columns.Add(FillCol("DbTitle", "DB のタイトル"));
        grid.Columns.Add(ReadOnlyCol("DbLength", "尺", 60));
        grid.Columns.Add(FillCol("YouTubeTitle", "プレイリストのタイトル"));
        grid.Columns.Add(ReadOnlyCol("VideoLength", "尺", 60));
        grid.Columns.Add(ReadOnlyCol("Diff", "差", 45));

        // 唯一の編集可能列。誤った対応を手で貼り替える／空にして解除するための最終手段。
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "VideoId",
            HeaderText = "動画 ID（編集可）",
            Width = 130,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(ReadOnlyCol("Status", "状態", 90));

        grid.CellEndEdit += OnVideoIdEdited;
        return grid;
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

    private static DataGridViewTextBoxColumn FillCol(string name, string header)
        => new()
        {
            Name = name,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

    /// <summary>現在のプレビュー内容をグリッドへ流し込み、見出しの要約も更新する。 ずらし操作や手編集のあとに呼んで表示を作り直す。</summary>
    private void FillGrid()
    {
        int selected = _grid.CurrentRow?.Index ?? -1;

        _grid.Rows.Clear();
        foreach (var r in _preview.Rows)
        {
            string trackLabel = r.CatalogNo.Length == 0
                ? "—"
                : $"{r.CatalogNo}-Tr.{r.TrackNo}" + (r.SubOrder > 0 ? $"-{r.SubOrder}" : "");

            int idx = _grid.Rows.Add(
                r.Position, trackLabel, r.DbTitle, FormatSeconds(r.DbLengthSeconds),
                r.YouTubeTitle, FormatSeconds(r.VideoLengthSeconds),
                r.LengthDiffSeconds is int d ? $"{d}s" : "",
                r.VideoId, r.StatusLabel);

            ApplyRowColor(_grid.Rows[idx], r);
        }

        if (selected >= 0 && selected < _grid.Rows.Count)
            _grid.CurrentCell = _grid.Rows[selected].Cells[ColVideoId];

        UpdateHead();
    }

    /// <summary>行の状態に応じた背景色。確認が要る行だけ色を付け、一致行は既定色のまま残す。</summary>
    private static void ApplyRowColor(DataGridViewRow row, ArtTrackImportRow r)
    {
        // 「配信なし」は配信されていない曲として正常に起こる状態なので、警告色ではなく
        // 灰色で「対象外」であることだけを示す（赤にすると毎回エラーのように見えてしまう）。
        if (r.CatalogNo.Length > 0 && r.VideoId.Length == 0)
            row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        else if (r.CatalogNo.Length == 0)
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 226, 226);
        else if (!r.Embeddable)
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 192);
        // Premium 限定は割り当て自体は正しいので、再生できない警告として橙より淡い紫で示す。
        else if (r.IsPremiumOnly)
            row.DefaultCellStyle.BackColor = Color.FromArgb(236, 226, 255);
        else if (r.ManuallyEdited)
            row.DefaultCellStyle.BackColor = Color.FromArgb(224, 240, 255);
        else if (!r.LooksCorrect)
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 214);
        else
            row.DefaultCellStyle.BackColor = Color.Empty;
    }

    private static string FormatSeconds(int? seconds)
        => seconds is int s ? $"{s / 60}:{s % 60:D2}" : "";

    /// <summary>動画 ID 列が編集されたときに、プレビュー側の行へ反映する。 人の指定を自動判定より優先する印（手動指定）を立て、以後の書き込み対象に含める。</summary>
    private void OnVideoIdEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex != ColVideoId) return;
        if (e.RowIndex < 0 || e.RowIndex >= _preview.Rows.Count) return;

        var row = _preview.Rows[e.RowIndex];
        string entered = (_grid.Rows[e.RowIndex].Cells[ColVideoId].Value?.ToString() ?? "").Trim();
        if (string.Equals(entered, row.VideoId, StringComparison.Ordinal)) return;

        row.VideoId = entered;
        row.ManuallyEdited = true;
        // 手で貼り替えた動画の尺・埋め込み可否・地域制限は取得していないので、判定印は落として
        // 「手動指定」として扱う。埋め込み可否は不明のままでは再生ボタンが出ないため、
        // 人が指定した以上は再生対象として通す（どちらも次の定期の再確認で API の値に直る）。
        row.VideoLengthSeconds = null;
        row.BlockedInJapan = false;
        row.TitleMatches = false;
        row.DurationMatches = false;
        row.Embeddable = entered.Length > 0;

        FillGrid();
    }

    /// <summary>選択行から下について、DB トラックへの動画割り当てを 1 つ分ずらす。 <paramref name="delta"/> が +1 なら 1 つ後ろへ送り（間に 1 件の空きを作る）、 -1 なら 1 つ前へ詰める（直前の割り当てを 1 件捨てる）。</summary>
    private void ShiftAssignments(int delta)
    {
        int from = _grid.CurrentRow?.Index ?? -1;
        if (from < 0) { MessageBox.Show(this, "ずらす開始行を選択してください。"); return; }

        var rows = _preview.Rows;
        var videoSide = rows.Select(r => (r.VideoId, r.YouTubeTitle, r.VideoLengthSeconds, r.Embeddable, r.BlockedInJapan)).ToList();

        if (delta > 0)
        {
            // 選択行に空きを差し込み、以降を 1 つ後ろへ。末尾の 1 件は押し出されて消える。
            videoSide.Insert(from, ("", "", null, false, false));
            videoSide.RemoveAt(videoSide.Count - 1);
        }
        else
        {
            // 選択行の 1 つ前を捨てて、以降を 1 つ前へ詰める。末尾には空きができる。
            if (from == 0) { MessageBox.Show(this, "先頭行より前には詰められません。"); return; }
            videoSide.RemoveAt(from - 1);
            videoSide.Add(("", "", null, false, false));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var (id, title, len, emb, blocked) = videoSide[i];
            rows[i].VideoId = id;
            rows[i].YouTubeTitle = title;
            rows[i].VideoLengthSeconds = len;
            rows[i].Embeddable = emb;
            rows[i].BlockedInJapan = blocked;
            rows[i].ManuallyEdited = true;
            rows[i].TitleMatches = ArtTrackImportService.TitlesEquivalent(rows[i].DbTitle, title);
            rows[i].DurationMatches = false;
        }

        FillGrid();
    }

    /// <summary>選択行の動画割り当てを外す（配信なし扱いにする）。</summary>
    private void ClearSelectedAssignment()
    {
        int index = _grid.CurrentRow?.Index ?? -1;
        if (index < 0 || index >= _preview.Rows.Count) return;

        var row = _preview.Rows[index];
        row.VideoId = "";
        row.YouTubeTitle = "";
        row.VideoLengthSeconds = null;
        row.Embeddable = false;
        row.BlockedInJapan = false;
        row.TitleMatches = false;
        row.DurationMatches = false;
        row.ManuallyEdited = true;

        FillGrid();
    }
}
