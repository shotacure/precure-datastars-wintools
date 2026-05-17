// このファイルでは Nullable 注釈付きの型（IContainer? 等）と null 寛容演算子（null!）を併用する。
// プロジェクト設定の Nullable コンテキストに依存せず、Designer 自動生成扱いされた場合でも
// 注釈構文が有効になるよう、ファイル冒頭で明示的に #nullable enable を宣言する。
#nullable enable

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// <see cref="NodePropertiesEditorPanel"/> の Designer 部分。
/// クレジット編集画面の右ペインに置かれ、Card / Tier / Group / Role の選択時に
/// Notes（備考）プロパティを編集できるようにする。レイアウトは縦積みのシンプルな構成:
/// 上から「対象ノードラベル」「種別補足ラベル」「Notes 編集 TextBox（複数行）」「保存ボタン」。
/// </summary>
partial class NodePropertiesEditorPanel
{
    /// <summary>選択中ノードの情報を表示するラベル（例: 「選択中: カード #2」）。</summary>
    private Label lblNodeHeader = null!;

    /// <summary>種別の補足説明ラベル（例: 「カード備考は credit_cards.notes に保存されます」）。</summary>
    private Label lblNodeKindNote = null!;

    /// <summary>「備考」見出しラベル。</summary>
    private Label lblNotesCaption = null!;

    /// <summary>Notes 編集用の複数行 TextBox。</summary>
    private TextBox txtNotes = null!;

    /// <summary>「💾 保存」ボタン（押下で Draft.Entity.Notes を更新 + MarkModified）。</summary>
    private Button btnSave = null!;

    /// <summary>各コントロールの上下ストレッチ用の親パネル。</summary>
    private Panel pnlOuter = null!;

    private System.ComponentModel.IContainer? components = null;

    private void InitializeComponent()
    {
        // ── ルートのフレーム ──
        // パネル全体を Dock=Fill で受け、内部要素を縦に積み上げる構成。
        // ボタンは Dock=Bottom、見出し系は Dock=Top、TextBox は Dock=Fill で残り全部を埋める。
        SuspendLayout();

        Dock = DockStyle.Fill;
        // BackColor をやや薄い色にして、他のエディタパネル（EntryEditorPanel / BlockEditorPanel）と
        // 視覚的に同列の存在であることを示しつつ、Notes 編集に集中できるよう装飾は最小限に抑える。
        BackColor = SystemColors.Control;

        pnlOuter = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        // ── ヘッダラベル：選択中ノード ──
        lblNodeHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(2, 4, 2, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "選択中: (なし)",
        };

        // ── 補足ラベル：保存先テーブル等の説明 ──
        lblNodeKindNote = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(2, 0, 2, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Yu Gothic UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Notes（備考）を編集して保存ボタンを押してください。",
        };

        // ── 「備考」見出し ──
        lblNotesCaption = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(2, 4, 2, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "備考 (notes):",
        };

        // ── Notes 編集 TextBox ──
        // 複数行・縦スクロール・等幅フォントで識別記号や記号列が崩れにくくする。
        // AcceptsReturn=true で Enter キーを改行扱いにし、メッセージボックス的なシングルライン挙動を避ける。
        txtNotes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            // 既定の WordWrap は true（折返しあり）。長い説明文を読みやすくする。
            WordWrap = true,
        };

        // ── 保存ボタン ──
        // Dock=Bottom にすることで、TextBox（Fill）の下に固定配置される。
        // Anchor は親が縮んだ際にボタンが右下に追従する設定。
        btnSave = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Text = "💾 保存",
            Font = new Font("Yu Gothic UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Enabled = false,
        };

        // ── 組み立て ──
        // Controls.Add の順序で Dock の重なりが決まる：
        // 「最後に Add したものが内側」になるため、内側に詰まるべきもの（TextBox=Fill）を最後に。
        pnlOuter.Controls.Add(txtNotes);            // Fill（残り全部）
        pnlOuter.Controls.Add(lblNotesCaption);     // Top（TextBox の直前）
        pnlOuter.Controls.Add(lblNodeKindNote);     // Top（さらに上）
        pnlOuter.Controls.Add(lblNodeHeader);       // Top（最上段）
        pnlOuter.Controls.Add(btnSave);             // Bottom

        Controls.Add(pnlOuter);

        ResumeLayout(false);
    }

    /// <summary>
    /// Designer 標準の Dispose 補完。<see cref="components"/> は現状未使用だが、
    /// 将来 ToolTip 等を追加する場合に備えて保持しておく。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }
}