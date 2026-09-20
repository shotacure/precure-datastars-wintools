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
/// 対応付けはタイトルの類似度による系列アライメントで決めており、完全一致は必須にしていない。
/// そのため「要確認」行が出ても対応が誤りとは限らず、表記揺れであることが多い。
/// 判断材料として DB 側とプレイリスト側のタイトルを並べて出す。
/// 「配信なし」は配信対象外の曲として正常に起こる状態で、その行は動画 ID を持たないまま残る。
/// </para>
/// </summary>
public sealed class ArtTrackImportPreviewDialog : Form
{
    private readonly ArtTrackImportPreview _preview;

    /// <summary><see cref="ArtTrackImportPreviewDialog"/> の新しいインスタンスを生成する。</summary>
    /// <param name="preview">表示する取り込みプレビュー。</param>
    /// <param name="productTitle">確認しやすいよう見出しに出す商品名。</param>
    public ArtTrackImportPreviewDialog(ArtTrackImportPreview preview, string productTitle)
    {
        _preview = preview;

        Text = "配信音源の取り込み確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(700, 400);
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

        var lblHead = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 8),
            Text = productTitle + Environment.NewLine + preview.SummaryText
        };

        var grid = BuildGrid();
        grid.Margin = new Padding(6, 0, 6, 0);

        // ボタンは右寄せ。FlowLayoutPanel の RightToLeft 配置なので、
        // 先に追加したものほど右端に並ぶ（キャンセルが右、書き込むがその左）。
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 8, 10, 10)
        };

        var btnCancel = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 30),
            Margin = new Padding(6, 0, 0, 0)
        };
        var btnApply = new Button
        {
            Text = "書き込む",
            DialogResult = DialogResult.OK,
            Size = new Size(130, 30),
            Margin = new Padding(6, 0, 0, 0),
            // 対応が 1 件も付かなかったときは書き込んでも意味がないので押せなくする。
            Enabled = preview.PairedCount > 0
        };

        pnlButtons.Controls.Add(btnCancel);
        pnlButtons.Controls.Add(btnApply);

        layout.Controls.Add(lblHead, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(pnlButtons, 0, 2);
        Controls.Add(layout);

        AcceptButton = btnApply;
        CancelButton = btnCancel;
    }

    /// <summary>対応表グリッドを組み立てる。行の状態に応じて背景色を変え、確認すべき行が目に入るようにする。</summary>
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        grid.Columns.Add(NewColumn("Position", "#", 40, DataGridViewAutoSizeColumnMode.None));
        grid.Columns.Add(NewColumn("Track", "トラック", 130, DataGridViewAutoSizeColumnMode.None));
        grid.Columns.Add(NewColumn("DbTitle", "DB のタイトル", 0, DataGridViewAutoSizeColumnMode.Fill));
        grid.Columns.Add(NewColumn("YouTubeTitle", "プレイリストのタイトル", 0, DataGridViewAutoSizeColumnMode.Fill));
        grid.Columns.Add(NewColumn("VideoId", "動画 ID", 110, DataGridViewAutoSizeColumnMode.None));
        grid.Columns.Add(NewColumn("Status", "状態", 100, DataGridViewAutoSizeColumnMode.None));

        foreach (var r in _preview.Rows)
        {
            string trackLabel = r.CatalogNo.Length == 0
                ? "—"
                : $"{r.CatalogNo}-Tr.{r.TrackNo}" + (r.SubOrder > 0 ? $"-{r.SubOrder}" : "");

            int idx = grid.Rows.Add(r.Position, trackLabel, r.DbTitle, r.YouTubeTitle, r.VideoId, r.StatusLabel);

            // 一致行は既定色のまま。それ以外を、対応が必要な度合いに応じて 3 段階で塗り分ける。
            // 「配信なし」は配信されていない曲として正常に起こる状態なので、警告色ではなく
            // 灰色で「対象外」であることだけを示す（赤にすると毎回エラーのように見えてしまう）。
            if (r.CatalogNo.Length > 0 && r.VideoId.Length == 0)
                grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            else if (r.CatalogNo.Length == 0)
                grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 226, 226);
            else if (!r.Embeddable)
                grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 192);
            else if (!r.TitleMatches)
                grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 214);
        }

        return grid;
    }

    private static DataGridViewTextBoxColumn NewColumn(
        string name, string header, int width, DataGridViewAutoSizeColumnMode mode)
        => new()
        {
            Name = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = mode,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
}
