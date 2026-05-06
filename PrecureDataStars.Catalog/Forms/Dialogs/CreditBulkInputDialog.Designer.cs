using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class CreditBulkInputDialog
{
    private IContainer components = null!;

    // 左ペイン
    private SplitContainer splitMain = null!;
    private Label lblInputHint = null!;
    private TextBox txtInput = null!;

    // 右ペイン（ツリーと警告リスト）
    private SplitContainer splitRight = null!;
    private Label lblPreviewHint = null!;
    private TreeView treePreview = null!;
    private Label lblWarningsHint = null!;
    private ListBox lstWarnings = null!;

    // 下段ボタン
    private Panel pnlButtons = null!;
    private Button btnApply = null!;
    private Button btnCancel = null!;

    /// <summary>
    /// クレジット一括入力ダイアログのレイアウト初期化（v1.2.1）。
    /// 左右 SplitContainer：左=入力テキスト、右=プレビュー(上)+警告(下) の上下 SplitContainer。
    /// </summary>
    private void InitializeComponent()
    {
        components = new Container();

        SuspendLayout();

        // ── ルート ──
        Text = "クレジット一括入力";
        ClientSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        MinimumSize = new Size(800, 540);

        // ── 下段ボタンパネル ──
        pnlButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8),
        };

        btnApply = new Button
        {
            Text = "💾 適用",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(120, 32),
            Location = new Point(pnlButtons.ClientSize.Width - 260, 8),
            Enabled = false,
        };

        btnCancel = new Button
        {
            Text = "キャンセル",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(120, 32),
            Location = new Point(pnlButtons.ClientSize.Width - 130, 8),
            DialogResult = DialogResult.Cancel,
        };

        pnlButtons.Controls.Add(btnApply);
        pnlButtons.Controls.Add(btnCancel);

        // ── メイン左右分割 ──
        // ── メイン左右分割 ──
        // 注意: SplitContainer は親に Add する前は内部的に既定サイズ（150×150 程度）。
        // この段階で SplitterDistance や Panel*MinSize を初期化子で大きく設定すると
        // 「SplitterDistance は Panel1MinSize と Panel2MinSize の間でなければなりません」
        // という InvalidOperationException が出る。
        // → ここでは Dock / Orientation / SplitterWidth だけ設定しておき、
        //   SplitterDistance / Panel1MinSize / Panel2MinSize は親に Controls.Add して
        //   実サイズが確定してから設定する（本メソッドの末尾で行う）。
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
        };

        // ─── 左ペイン: 入力テキスト ───
        lblInputHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(8, 6, 8, 6),
            Text = "クレジットをまとめて貼り付け／入力してください。\r\n"
                + "書式: 行末コロン=役職, -/--/---/---- = ブロック/グループ/ティア/カード区切り,\r\n"
                + "[XXX]=企業エントリ, [[XXX]]=ブロック先頭のグループトップ屋号,\r\n"
                + "<キャラ>声優=声の出演, <*キャラ>=モブ強制新規",
            AutoSize = false,
        };

        txtInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AcceptsTab = true,
            AcceptsReturn = true,
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
        };

        splitMain.Panel1.Controls.Add(txtInput);
        splitMain.Panel1.Controls.Add(lblInputHint);

        // ─── 右ペイン: 上下分割（プレビュー + 警告） ───
        // splitMain と同じ理由で SplitterDistance / Panel*MinSize は後回し。
        splitRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
        };

        // 上半: プレビューツリー
        lblPreviewHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 8, 4),
            Text = "プレビュー（パース結果）",
        };

        treePreview = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            Font = new Font("Yu Gothic UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        };

        splitRight.Panel1.Controls.Add(treePreview);
        splitRight.Panel1.Controls.Add(lblPreviewHint);

        // 下半: 警告リスト
        lblWarningsHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 8, 4),
            Text = "警告 / 情報",
        };

        lstWarnings = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
            Font = new Font("Yu Gothic UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        };

        splitRight.Panel2.Controls.Add(lstWarnings);
        splitRight.Panel2.Controls.Add(lblWarningsHint);

        // ── 組み立て ──
        splitMain.Panel2.Controls.Add(splitRight);

        Controls.Add(splitMain);
        Controls.Add(pnlButtons);

        AcceptButton = btnApply;
        CancelButton = btnCancel;

        ResumeLayout(false);

        // ── 親に Add し終わってから SplitterDistance / Min サイズを設定 ──
        // この時点で splitMain は親フォーム配下、splitRight は splitMain.Panel2 配下に
        // 入っているので、Dock=Fill により実サイズが確定している。
        //
        // 設定順は重要：MinSize を先にセットして「SplitterDistance はその範囲内」を満たす状態にしてから、
        // SplitterDistance を最終値に動かす。順番を間違えると古い MinSize と新 SplitterDistance の
        // 整合性チェックで InvalidOperationException が出る。
        //
        // 値が SplitContainer の現在サイズに対して大きすぎる場合の保険として、
        // 上限・下限でクランプしてから設定する。
        ApplySplitterLayout(splitMain, panel1Min: 320, panel2Min: 320, splitterDistance: 480);
        // v1.2.1 仕様: 右ペインのプレビュー（上）：警告（下）比率を 3:1 に。
        // 右ペインの利用可能高さはおおよそ 672px（720 ClientSize − 48 ボタン下段）。
        // 3:1 で割ると上が約 504、下が約 168。SplitterWidth を考慮してきり良く 500 にする。
        // panel2Min を 120 に上げ、警告リストが極端に潰れないよう保険を効かせる。
        ApplySplitterLayout(splitRight, panel1Min: 200, panel2Min: 120, splitterDistance: 500);
    }

    /// <summary>
    /// SplitContainer に対して Panel1MinSize / Panel2MinSize / SplitterDistance を
    /// 安全な順序で適用する（v1.2.1 ホットフィックス）。
    /// 親への Add 後・Dock=Fill による実サイズ確定後に呼ぶ前提。
    /// </summary>
    private static void ApplySplitterLayout(SplitContainer split, int panel1Min, int panel2Min, int splitterDistance)
    {
        // SplitContainer の「分割可能な軸方向の長さ」を取得する。
        // Vertical（左右分割）なら Width、Horizontal（上下分割）なら Height。
        int axisSize = split.Orientation == Orientation.Vertical ? split.Width : split.Height;

        // 軸長が 0 のままなら（描画前など）何もしない。
        if (axisSize <= 0) return;

        // MinSize 合計が軸長を超えるとそもそも成立しないので、超える場合は両方を縮める。
        // splitter 自体の幅 (SplitterWidth) も考慮する。
        int splitter = Math.Max(0, split.SplitterWidth);
        int maxAllowed = axisSize - splitter;
        if (panel1Min + panel2Min > maxAllowed)
        {
            // 縮め方は半々（保険的なフォールバック）。
            panel1Min = Math.Max(0, maxAllowed / 2);
            panel2Min = Math.Max(0, maxAllowed - panel1Min);
        }

        split.Panel1MinSize = panel1Min;
        split.Panel2MinSize = panel2Min;

        // SplitterDistance は [panel1Min, axisSize - splitter - panel2Min] の範囲にクランプ。
        int min = panel1Min;
        int max = Math.Max(min, axisSize - splitter - panel2Min);
        int clamped = Math.Max(min, Math.Min(max, splitterDistance));
        split.SplitterDistance = clamped;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }
}
