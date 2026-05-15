#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Controls;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// CreditMastersEditorForm の partial 拡張。
/// プリキュア／キャラクター続柄／家族関係の 3 タブの Designer 部 + ロジック部をまとめて担当する。
/// 既存の 12 タブ実装（CreditMastersEditorForm.cs / .Designer.cs）には触らず、
/// 新タブ分のフィールド宣言・Build*Tab・Wire*Events・Load*TabAsync・CRUD ハンドラを
/// プリキュア／続柄／家族関係の 3 タブ関連処理を本ファイルに集約している。
/// </summary>
partial class CreditMastersEditorForm
{
    // ════════════════════════════════════════════════════════════════════
    // フィールド宣言（Designer 部に相当）
    // ════════════════════════════════════════════════════════════════════

    // ─────────────── プリキュアタブ ───────────────
    // 一覧グリッド（PrecureListRow を直接バインド：alias / person 名を JOIN 済みで取得）
    private DataGridView gridPrecures = null!;
    // 編集パネルの 4 名義コンボ（character_aliases から「同一 character_id 内のみ」絞り込み）
    private ComboBox cboPrPreTransformAlias = null!;
    private ComboBox cboPrTransformAlias = null!;
    private ComboBox cboPrTransform2Alias = null!;
    private ComboBox cboPrAltFormAlias = null!;
    // 「キャラ選択」コンボ（4 名義コンボの選択肢を絞るための起点）。
    // ユーザーは先にここでキャラを選び、その後 4 名義コンボでそのキャラ配下の alias を選ぶ。
    private ComboBox cboPrCharacter = null!;
    // 誕生日：月・日コンボ + 和英プレビューラベル
    private ComboBox cboPrBirthMonth = null!;
    private ComboBox cboPrBirthDay = null!;
    private Label lblPrBirthdayPreview = null!;
    // 声優：person ピッカー風（数値直入力 + 検索ボタン + 表示名ラベル）
    private NumericUpDown numPrVoiceActorPersonId = null!;
    private Button btnPrPickVoiceActor = null!;
    private Label lblPrVoiceActorName = null!;
    // 肌色ピッカー（HSL/RGB 両入力 + ΔE 評価バッジ込み UserControl）
    private SkinColorPickerControl skinColorPicker = null!;
    // 学校・クラス・家業
    private TextBox txtPrSchool = null!;
    private TextBox txtPrSchoolClass = null!;
    private TextBox txtPrFamilyBusiness = null!;
    private TextBox txtPrNotes = null!;
    // 家族関係グリッド（自分 = 編集中 precure に紐付くキャラの家族）。
    // CharacterFamilyRelationListRow をバインド。
    private DataGridView gridPrFamily = null!;
    private Button btnPrFamilyAdd = null!;
    private Button btnPrFamilyRemove = null!;
    private ComboBox cboPrFamilyRelation = null!;     // 続柄コンボ（追加時の既定値）
    private ComboBox cboPrFamilyRelatedCharacter = null!; // 相手キャラコンボ（追加時の既定値）
    // CRUD ボタン
    private Button btnNewPrecure = null!;
    private Button btnSavePrecure = null!;
    private Button btnDeletePrecure = null!;

    // ─────────────── キャラクター続柄マスタタブ ───────────────
    private DataGridView gridCharacterRelationKinds = null!;
    private TextBox txtCrkRelationCode = null!;
    private TextBox txtCrkNameJa = null!;
    private TextBox txtCrkNameEn = null!;
    private NumericUpDown numCrkDisplayOrder = null!;
    private TextBox txtCrkNotes = null!;
    private Button btnNewCharacterRelationKind = null!;
    private Button btnSaveCharacterRelationKind = null!;
    private Button btnDeleteCharacterRelationKind = null!;

    // ─────────────── 家族関係タブ（汎用、プリキュア以外でも使える） ───────────────
    private ComboBox cboCfrCharacter = null!;        // 「自分」キャラ選択
    private DataGridView gridCharacterFamilyRelations = null!;
    private ComboBox cboCfrRelatedCharacter = null!; // 「相手」キャラ選択
    private ComboBox cboCfrRelation = null!;         // 続柄コンボ
    private NumericUpDown numCfrDisplayOrder = null!;
    private TextBox txtCfrNotes = null!;
    private Button btnAddCfr = null!;
    private Button btnRemoveCfr = null!;
    private Button btnSaveCfr = null!;                // 「全部保存（置き換え）」ボタン

    // 編集中の家族関係行を一時保持するメモリ DataSource。
    // gridCharacterFamilyRelations はこのリストを直接バインドし、追加・削除はリスト操作で行う。
    // 「全部保存」ボタンで character_id 単位の置き換え保存を行う。
    private List<CharacterFamilyRelationListRow> _cfrRows = new();

    // プリキュア行選択中はキャラ選択コンボの SelectedIndexChanged 自動発火（→ alias コンボ再ロード）を
    // 抑止し、OnPrecureRowSelectedAsync 内で明示的に 1 回だけ ReloadAliasCombosForCharacterAsync を
    // 呼ぶようにするためのフラグ。自動発火と明示呼び出しが二重に走ると、
    // alias コンボの DataSource 入れ替え処理が衝突して内部状態が崩れる原因になる。
    private bool _suppressPrCharacterChanged;

    // ════════════════════════════════════════════════════════════════════
    // Build*Tab メソッド群（InitializeComponent から呼ばれる）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// プリキュアタブの構築。左 50% に一覧グリッド、右 50% に詳細編集パネルの 2 カラム構成。
    /// 編集パネルは縦に「変身名義 4 本 → 誕生日 → 声優 → 肌色 → 学校情報 → 家族グリッド → ボタン」と並ぶ。
    /// </summary>
    private void BuildPrecuresTab()
    {
        tabPrecures.Padding = new Padding(8);

        // 上下 2 段：上段 = 一覧グリッド、下段 = 詳細パネル。
        // SplitContainer で上下分割し、上段に「目標 400px」を後段で設定する。
        // SplitterDistance はここでは設定しない：BuildPrecuresTab 実行時点では
        // SplitContainer がまだ親に追加されておらず実サイズを持たないため、
        // 大きな値を直接設定すると WinForms 内部で Panel2MinSize を確保するように
        // 低い値にクランプされ、後でフォームが拡大してもその値のまま据え置かれる。
        // → フォームの Load イベントで実サイズが確定してから SplitterDistance を設定する。
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = false
        };

        // ── 上段：一覧グリッド ──
        gridPrecures = new DataGridView { Dock = DockStyle.Fill };
        ConfigureListGrid(gridPrecures);
        split.Panel1.Controls.Add(gridPrecures);

        // ── 下段：詳細編集パネル（2 カラム構成、AutoScroll で安全側に倒す） ──
        // 1500px ウインドウ前提の絶対座標：
        //   左カラム x=12（label 100 + input 360、x=12..476 帯）
        //   右カラム x=520（label 100 + input 360、x=520..984 帯）
        //   ボタン群 x=1340（width 140、横幅 1500 内に収まる範囲）
        //   フル幅項目（家業/備考/家族グリッド/肌色）は label 100 + input 868（x=12..1000 帯）
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };

        // ── 起点キャラクター（全幅）──
        AddLabeledControl(pnl, "キャラクター", cboPrCharacter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        }, 12, 14, labelWidth: 100, inputWidth: 360);

        // ── 変身名義 4 本（2x2）──
        AddLabeledControl(pnl, "変身前",   cboPrPreTransformAlias = NewAliasCombo(),                12,  46, labelWidth: 100, inputWidth: 360);
        AddLabeledControl(pnl, "変身後",   cboPrTransformAlias    = NewAliasCombo(),               520,  46, labelWidth: 100, inputWidth: 360);
        AddLabeledControl(pnl, "変身後 2", cboPrTransform2Alias   = NewAliasCombo(allowNull: true), 12,  78, labelWidth: 100, inputWidth: 360);
        AddLabeledControl(pnl, "別形態",   cboPrAltFormAlias      = NewAliasCombo(allowNull: true),520,  78, labelWidth: 100, inputWidth: 360);

        // ── 誕生日（左）と声優（右）──
        // 左：月コンボ + 「月」 + 日コンボ + 「日」 + 和英プレビュー
        var lblBday = new Label { Text = "誕生日", Location = new Point(12, 114), Size = new Size(100, 20) };
        cboPrBirthMonth = new ComboBox { Location = new Point(116, 110), Size = new Size(60, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        cboPrBirthMonth.Items.Add("(未)"); for (int i = 1; i <= 12; i++) cboPrBirthMonth.Items.Add(i);
        cboPrBirthMonth.SelectedIndex = 0;
        var lblMonthSep = new Label { Text = "月", Location = new Point(180, 114), Size = new Size(20, 20) };
        cboPrBirthDay = new ComboBox { Location = new Point(204, 110), Size = new Size(60, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        cboPrBirthDay.Items.Add("(未)"); for (int i = 1; i <= 31; i++) cboPrBirthDay.Items.Add(i);
        cboPrBirthDay.SelectedIndex = 0;
        var lblDaySep = new Label { Text = "日", Location = new Point(268, 114), Size = new Size(20, 20) };
        lblPrBirthdayPreview = new Label
        {
            Location = new Point(296, 114),
            Size = new Size(180, 20),
            ForeColor = SystemColors.GrayText,
            Text = "(未設定)"
        };
        pnl.Controls.AddRange(new Control[] { lblBday, cboPrBirthMonth, lblMonthSep, cboPrBirthDay, lblDaySep, lblPrBirthdayPreview });

        // 右：声優（person_id + 検索ボタン + 表示名ラベル）
        var lblVa = new Label { Text = "声優 (person)", Location = new Point(520, 114), Size = new Size(100, 20) };
        numPrVoiceActorPersonId = new NumericUpDown
        {
            Location = new Point(624, 110),
            Size = new Size(90, 23),
            Maximum = 9_999_999
        };
        btnPrPickVoiceActor = new Button
        {
            Text = "検索...",
            Location = new Point(720, 109),
            Size = new Size(64, 25)
        };
        lblPrVoiceActorName = new Label
        {
            Location = new Point(790, 114),
            Size = new Size(210, 20),
            ForeColor = SystemColors.GrayText
        };
        pnl.Controls.AddRange(new Control[] { lblVa, numPrVoiceActorPersonId, btnPrPickVoiceActor, lblPrVoiceActorName });

        // ── 学校（左）/ クラス（右）──
        AddLabeledControl(pnl, "学校",   txtPrSchool      = new TextBox(),  12, 146, labelWidth: 100, inputWidth: 360);
        AddLabeledControl(pnl, "クラス", txtPrSchoolClass = new TextBox(), 520, 146, labelWidth: 100, inputWidth: 360);

        // ── 家業（全幅）──
        AddLabeledControl(pnl, "家業", txtPrFamilyBusiness = new TextBox(), 12, 178, labelWidth: 100, inputWidth: 868);

        // ── 備考（全幅、複数行 60px）──
        var lblNotes = new Label { Text = "備考", Location = new Point(12, 214), Size = new Size(100, 20) };
        txtPrNotes = new TextBox
        {
            Location = new Point(116, 210),
            Size = new Size(868, 60),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
        };
        pnl.Controls.Add(lblNotes);
        pnl.Controls.Add(txtPrNotes);

        // ── 肌色ピッカー（全幅相当の左寄せ。UserControl は 580×160）──
        var lblSkin = new Label
        {
            Text = "肌色",
            Location = new Point(12, 286),
            Size = new Size(100, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        skinColorPicker = new SkinColorPickerControl
        {
            Location = new Point(116, 282),
            Size = new Size(580, 160)
        };
        pnl.Controls.Add(lblSkin);
        pnl.Controls.Add(skinColorPicker);

        // ── 家族グリッド（全幅、高さ 130px）──
        var lblFamily = new Label
        {
            Text = "家族",
            Location = new Point(12, 458),
            Size = new Size(100, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        gridPrFamily = new DataGridView
        {
            Location = new Point(116, 458),
            Size = new Size(868, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridPrFamily);
        // 家族追加用：続柄・相手キャラコンボ + 追加/削除ボタン（家族グリッド直下）
        cboPrFamilyRelation = new ComboBox
        {
            Location = new Point(116, 596),
            Size = new Size(160, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };
        cboPrFamilyRelatedCharacter = new ComboBox
        {
            Location = new Point(282, 596),
            Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };
        btnPrFamilyAdd    = new Button { Text = "+ 追加",   Location = new Point(648, 595), Size = new Size(70, 25) };
        btnPrFamilyRemove = new Button { Text = "− 削除",   Location = new Point(722, 595), Size = new Size(70, 25) };
        pnl.Controls.AddRange(new Control[] { lblFamily, gridPrFamily, cboPrFamilyRelation, cboPrFamilyRelatedCharacter, btnPrFamilyAdd, btnPrFamilyRemove });

        // ── CRUD ボタン群（右上に縦並び固定配置） ──
        btnNewPrecure    = new Button { Text = "新規",       Location = new Point(1100, 14), Size = new Size(140, 28) };
        btnSavePrecure   = new Button { Text = "保存 / 更新", Location = new Point(1100, 46), Size = new Size(140, 28) };
        btnDeletePrecure = new Button { Text = "選択行を削除", Location = new Point(1100, 78), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewPrecure, btnSavePrecure, btnDeletePrecure });

        split.Panel2.Controls.Add(pnl);
        tabPrecures.Controls.Add(split);

        // SplitterDistance を遅延設定する。
        // 理由: ここまでの BuildPrecuresTab 実行時点では tabPrecures がまだ Form.Controls に
        // 追加されておらず（追加は InitializeComponent 末尾の Controls.Add(tabControl) で起きる）、
        // SplitContainer は default 150x100 のまま。この状態で SplitterDistance=400 を直接代入すると
        // WinForms が Panel2MinSize を確保するために値を低くクランプし、後で親が拡大しても
        // クランプ後の値が「現在の高さで valid」なため戻らない。
        // Form.Load 時点では ClientSize=1500x850 が確定しており、Dock=Fill の SplitContainer も
        // 既に最終サイズ（約 1484x810）を持っているため、目標値を安全に代入できる。
        const int targetGridHeight = 400;
        EventHandler? loadHandler = null;
        loadHandler = (s, e) =>
        {
            // 何度も発火させない：1 回だけ設定したらハンドラを外す。
            this.Load -= loadHandler;
            try
            {
                if (split.Height >= targetGridHeight + split.Panel2MinSize + split.SplitterWidth)
                {
                    split.SplitterDistance = targetGridHeight;
                }
            }
            catch (InvalidOperationException) { /* SplitContainer 未配置等のレアケースは黙殺 */ }
        };
        this.Load += loadHandler;
    }

    /// <summary>
    /// キャラクター続柄マスタタブの構築。CharacterKinds タブと同じスタイル
    /// （上段：グリッド、下段：6 フィールド + 新規／保存／削除ボタン）。
    /// </summary>
    private void BuildCharacterRelationKindsTab()
    {
        tabCharacterRelationKinds.Padding = new Padding(8);
        gridCharacterRelationKinds = new DataGridView { Dock = DockStyle.Top, Height = 340 };
        ConfigureListGrid(gridCharacterRelationKinds);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtCrkRelationCode = new TextBox();
        txtCrkNameJa = new TextBox();
        txtCrkNameEn = new TextBox();
        numCrkDisplayOrder = new NumericUpDown { Maximum = 255, Minimum = 0 };
        txtCrkNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "続柄コード", txtCrkRelationCode, 18, 18, inputWidth: 200);
        AddLabeledControl(pnl, "日本語表示", txtCrkNameJa, 18, 50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表示", txtCrkNameEn, 18, 82, inputWidth: 320);
        AddLabeledControl(pnl, "表示順", numCrkDisplayOrder, 18, 114, inputWidth: 100);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtCrkNotes.Location = new Point(132, 146);
        txtCrkNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes);
        pnl.Controls.Add(txtCrkNotes);

        btnNewCharacterRelationKind = new Button { Text = "新規", Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveCharacterRelationKind = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteCharacterRelationKind = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewCharacterRelationKind, btnSaveCharacterRelationKind, btnDeleteCharacterRelationKind });

        tabCharacterRelationKinds.Controls.Add(pnl);
        tabCharacterRelationKinds.Controls.Add(gridCharacterRelationKinds);
    }

    /// <summary>
    /// 家族関係タブの構築。「自分」キャラを選ぶと、そのキャラの家族関係一覧を中央グリッドに表示し、
    /// 下部のフォーム（相手キャラ・続柄・表示順・備考）で行追加／削除を行う。
    /// 編集はメモリ上の List で行い、「保存（置き換え）」ボタンで character_id 単位の
    /// 一括置き換えを行う。
    /// </summary>
    private void BuildCharacterFamilyRelationsTab()
    {
        tabCharacterFamilyRelations.Padding = new Padding(8);

        var lblOwner = new Label { Text = "自分", Location = new Point(18, 22), Size = new Size(60, 20) };
        cboCfrCharacter = new ComboBox
        {
            Location = new Point(82, 18),
            Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };

        gridCharacterFamilyRelations = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1440, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridCharacterFamilyRelations);

        var pnl = new Panel
        {
            Location = new Point(0, 380),
            Size = new Size(1480, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        cboCfrRelatedCharacter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };
        cboCfrRelation = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };
        numCfrDisplayOrder = new NumericUpDown { Maximum = 255, Minimum = 0 };
        txtCfrNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "相手キャラ", cboCfrRelatedCharacter, 18, 18, inputWidth: 320);
        AddLabeledControl(pnl, "続柄", cboCfrRelation, 18, 50, inputWidth: 200);
        AddLabeledControl(pnl, "表示順", numCfrDisplayOrder, 18, 82, inputWidth: 100);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 118), Size = new Size(110, 20) };
        txtCfrNotes.Location = new Point(132, 114);
        txtCfrNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes);
        pnl.Controls.Add(txtCfrNotes);

        btnAddCfr = new Button { Text = "+ 追加", Location = new Point(620, 18), Size = new Size(140, 28) };
        btnRemoveCfr = new Button { Text = "− 選択行削除", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnSaveCfr = new Button { Text = "💾 保存 (置き換え)", Location = new Point(620, 82), Size = new Size(160, 28) };
        pnl.Controls.AddRange(new Control[] { btnAddCfr, btnRemoveCfr, btnSaveCfr });

        tabCharacterFamilyRelations.Controls.AddRange(new Control[] { lblOwner, cboCfrCharacter, gridCharacterFamilyRelations, pnl });
    }

    // ════════════════════════════════════════════════════════════════════
    // ヘルパ：alias コンボの新規生成（DropDownList、Label/Id 構造の Items を持つ）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// character_aliases 用のコンボボックスを生成する。allowNull が true なら先頭に
    /// 「(未設定)」項目を含む構造の DataSource を期待する。
    /// </summary>
    private ComboBox NewAliasCombo(bool allowNull = false)
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Label",
            ValueMember = "Id"
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // イベント結線
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// プリキュア／続柄／家族関係の 3 タブ用イベントを一括結線する。
    /// CreditMastersEditorForm のコンストラクタから呼ばれる。
    /// </summary>
    private void WirePrecureTabsEvents()
    {
        // ── プリキュアタブ ──
        gridPrecures.SelectionChanged += async (_, __) => await OnPrecureRowSelectedAsync();
        // fix3: 行選択中の自動再ロードを抑止。OnPrecureRowSelectedAsync 内で明示的に呼ぶ。
        cboPrCharacter.SelectedIndexChanged += async (_, __) =>
        {
            if (_suppressPrCharacterChanged) return;
            await ReloadAliasCombosForCharacterAsync();
        };
        cboPrBirthMonth.SelectedIndexChanged += (_, __) => UpdateBirthdayPreview();
        cboPrBirthDay.SelectedIndexChanged += (_, __) => UpdateBirthdayPreview();
        numPrVoiceActorPersonId.ValueChanged += async (_, __) => await ResolvePrVoiceActorNameAsync();
        btnPrPickVoiceActor.Click += (_, __) => OpenPersonPicker(numPrVoiceActorPersonId);
        btnNewPrecure.Click += (_, __) => ClearPrecureForm();
        btnSavePrecure.Click += async (_, __) => await SavePrecureAsync();
        btnDeletePrecure.Click += async (_, __) => await DeletePrecureAsync();
        btnPrFamilyAdd.Click += async (_, __) => await OnPrFamilyAddClickAsync();
        btnPrFamilyRemove.Click += async (_, __) => await OnPrFamilyRemoveClickAsync();

        // ── 続柄マスタタブ ──
        gridCharacterRelationKinds.SelectionChanged += (_, __) => OnCharacterRelationKindRowSelected();
        btnNewCharacterRelationKind.Click += (_, __) => ClearCharacterRelationKindForm();
        btnSaveCharacterRelationKind.Click += async (_, __) => await SaveCharacterRelationKindAsync();
        btnDeleteCharacterRelationKind.Click += async (_, __) => await DeleteCharacterRelationKindAsync();

        // ── 家族関係タブ ──
        cboCfrCharacter.SelectedIndexChanged += async (_, __) => await ReloadCfrGridAsync();
        gridCharacterFamilyRelations.SelectionChanged += (_, __) => OnCfrRowSelected();
        btnAddCfr.Click += (_, __) => OnCfrAddClick();
        btnRemoveCfr.Click += (_, __) => OnCfrRemoveClick();
        btnSaveCfr.Click += async (_, __) => await SaveCfrAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // 初期ロード
    // ════════════════════════════════════════════════════════════════════

    /// <summary>プリキュアタブの初期ロード。一覧 + 編集パネルのコンボ初期化まで。</summary>
    private async Task LoadPrecuresTabAsync()
    {
        // 一覧グリッド
        gridPrecures.DataSource = (await _precuresRepo.GetListAsync()).ToList();

        // キャラ選択コンボ（4 名義コンボの絞り込み起点）と、家族グリッドの「相手キャラ」コンボ。
        // どちらも characters 全件を表示する。
        await RefreshPrecureTabComboSourcesAsync().ConfigureAwait(true);

        // 続柄コンボ（家族グリッド追加用、家族関係タブと共用）
        var rels = await _characterRelationKindsRepo.GetAllAsync();
        var relItems = rels.Select(r => new IdLabel<string>(r.RelationCode, $"{r.NameJa}（{r.RelationCode}）")).ToList();
        cboPrFamilyRelation.DataSource = relItems.ToList();

        // 家族グリッドのバインド準備（Precure 行選択時に都度更新）
        gridPrFamily.DataSource = new List<CharacterFamilyRelationListRow>();

        ClearPrecureForm();
    }

    /// <summary>続柄マスタタブの初期ロード。</summary>
    private async Task LoadCharacterRelationKindsTabAsync()
    {
        gridCharacterRelationKinds.DataSource = (await _characterRelationKindsRepo.GetAllAsync()).ToList();
        ClearCharacterRelationKindForm();
    }

    /// <summary>家族関係タブの初期ロード。引数の characters は呼び出し側から共有して受け取る。</summary>
    private async Task LoadCharacterFamilyRelationsTabAsync(IReadOnlyList<Character> characters)
    {
        var charItems = characters
            .Select(c => new IdLabel<int>(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
            .ToList();
        cboCfrCharacter.DataSource = charItems.ToList();
        cboCfrRelatedCharacter.DataSource = charItems.ToList();

        var rels = await _characterRelationKindsRepo.GetAllAsync();
        cboCfrRelation.DataSource = rels
            .Select(r => new IdLabel<string>(r.RelationCode, $"{r.NameJa}（{r.RelationCode}）"))
            .ToList();

        if (charItems.Count > 0) await ReloadCfrGridAsync();
    }

    /// <summary>
    /// プリキュアタブのキャラ選択コンボ（cboPrCharacter）と家族グリッドの「相手キャラ」コンボ
    /// （cboPrFamilyRelatedCharacter）、および家族関係タブの両キャラコンボを再ロードする。
    /// キャラクタータブで CRUD があった直後に呼び出す。
    /// </summary>
    private async Task RefreshPrecureTabComboSourcesAsync()
    {
        var characters = await _charactersRepo.GetAllAsync();
        var charItems = characters
            .Select(c => new IdLabel<int>(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
            .ToList();
        // fix3: 行選択処理中に呼ばれる場合があるので、キャラ選択コンボの DataSource 入れ替えに
        // 連動した SelectedIndexChanged を抑止する（OnPrecureRowSelectedAsync が明示制御するため）。
        bool prevSuppress = _suppressPrCharacterChanged;
        _suppressPrCharacterChanged = true;
        try
        {
            cboPrCharacter.DataSource = charItems.ToList();
        }
        finally { _suppressPrCharacterChanged = prevSuppress; }
        // 家族グリッドの「相手キャラ」コンボも同じキャラ全件で更新
        cboPrFamilyRelatedCharacter.DataSource = charItems.ToList();
        // 4 名義コンボはこの後 cboPrCharacter の選択変更ハンドラ経由で再ロードされる。
    }

    /// <summary>家族関係タブのキャラコンボを再ロード。</summary>
    private async Task RefreshCharacterFamilyTabComboSourcesAsync()
    {
        var characters = await _charactersRepo.GetAllAsync();
        var charItems = characters
            .Select(c => new IdLabel<int>(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
            .ToList();
        cboCfrCharacter.DataSource = charItems.ToList();
        cboCfrRelatedCharacter.DataSource = charItems.ToList();
    }

    // ════════════════════════════════════════════════════════════════════
    // プリキュアタブ：CRUD ハンドラ
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 起点キャラ（cboPrCharacter）の選択値に応じて、4 名義コンボの選択肢を
    /// 「同じキャラ配下の alias のみ」に絞り込む。NULL 許容コンボには先頭に「(未設定)」を入れる。
    /// </summary>
    private async Task ReloadAliasCombosForCharacterAsync()
    {
        try
        {
            if (cboPrCharacter.SelectedValue is not int characterId)
            {
                cboPrPreTransformAlias.DataSource = new List<IdLabel<int>>();
                cboPrTransformAlias.DataSource = new List<IdLabel<int>>();
                cboPrTransform2Alias.DataSource = new List<IdLabel<int?>>();
                cboPrAltFormAlias.DataSource = new List<IdLabel<int?>>();
                return;
            }

            var aliases = await _characterAliasesRepo.GetByCharacterAsync(characterId);
            // 必須コンボ：そのキャラの alias 一覧を直接バインド
            var required = aliases
                .Select(a => new IdLabel<int>(a.AliasId, $"#{a.AliasId}  {a.Name}"))
                .ToList();
            cboPrPreTransformAlias.DataSource = required.ToList();
            cboPrTransformAlias.DataSource = required.ToList();
            // NULL 許容コンボ：先頭に「(未設定)」を入れる
            var optional = new List<IdLabel<int?>> { new(null, "(未設定)") };
            optional.AddRange(aliases.Select(a => new IdLabel<int?>(a.AliasId, $"#{a.AliasId}  {a.Name}")));
            cboPrTransform2Alias.DataSource = optional.ToList();
            cboPrAltFormAlias.DataSource = optional.ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>プリキュア一覧グリッドの行が変わったら、編集パネルに値を反映する。</summary>
    private async Task OnPrecureRowSelectedAsync()
    {
        try
        {
            if (gridPrecures.CurrentRow?.DataBoundItem is not PrecureListRow row) return;
            var p = await _precuresRepo.GetByIdAsync(row.PrecureId);
            if (p is null) return;

            // 起点キャラを変身前 alias の character_id に合わせ込む。
            // fix3: cboPrCharacter の SelectedValue 設定で SelectedIndexChanged が
            // 自動発火し ReloadAliasCombosForCharacterAsync が走ると、続けて明示 await でも
            // 同じメソッドを呼ぶことになり、DataSource 入れ替えの二重実行で内部状態が崩れる。
            // フラグで自動発火を抑止し、明示的に 1 回だけ ReloadAliasCombosForCharacterAsync を呼ぶ。
            var preAlias = await _characterAliasesRepo.GetByIdAsync(p.PreTransformAliasId);
            if (preAlias is not null)
            {
                _suppressPrCharacterChanged = true;
                try { cboPrCharacter.SelectedValue = preAlias.CharacterId; }
                finally { _suppressPrCharacterChanged = false; }
                await ReloadAliasCombosForCharacterAsync();
            }

            // 必須コンボ：通常の SelectedValue 設定で OK（int 型の値がそのまま見つかる）
            cboPrPreTransformAlias.SelectedValue = p.PreTransformAliasId;
            cboPrTransformAlias.SelectedValue = p.TransformAliasId;
            // NULL 許容コンボ：SelectedValue=null を回避する専用ヘルパを使う。
            // 直接 SelectedValue=null すると WinForms 内部の Dictionary key 検索で
            // ArgumentNullException(Parameter 'key') が発生する版があるため、
            // 明示的に SelectedIndex 操作で「(未設定)」項目（先頭）に逃がす。
            SetNullableAliasComboValue(cboPrTransform2Alias, p.Transform2AliasId);
            SetNullableAliasComboValue(cboPrAltFormAlias, p.AltFormAliasId);

            // 誕生日
            cboPrBirthMonth.SelectedIndex = p.BirthMonth.HasValue ? p.BirthMonth.Value : 0;
            cboPrBirthDay.SelectedIndex = p.BirthDay.HasValue ? p.BirthDay.Value : 0;
            UpdateBirthdayPreview();

            // 声優
            numPrVoiceActorPersonId.Value = p.VoiceActorPersonId.HasValue ? p.VoiceActorPersonId.Value : 0;
            await ResolvePrVoiceActorNameAsync();

            // 肌色
            skinColorPicker.SetHsl(p.SkinColorH, p.SkinColorS, p.SkinColorL);
            skinColorPicker.SetRgb(p.SkinColorR, p.SkinColorG, p.SkinColorB);

            // 学校情報・備考
            txtPrSchool.Text = p.School ?? "";
            txtPrSchoolClass.Text = p.SchoolClass ?? "";
            txtPrFamilyBusiness.Text = p.FamilyBusiness ?? "";
            txtPrNotes.Text = p.Notes ?? "";

            // 家族グリッドを変身前 alias の character_id でロード
            if (preAlias is not null)
            {
                var family = await _characterFamilyRelationsRepo.GetByCharacterWithNamesAsync(preAlias.CharacterId);
                gridPrFamily.DataSource = family.ToList();
            }
            else
            {
                gridPrFamily.DataSource = new List<CharacterFamilyRelationListRow>();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// NULL 許容名義コンボ（DataSource が <see cref="List{T}"/> of <see cref="IdLabel{T}"/> with int?）に
    /// 値をセットする。null の場合は SelectedIndex=0（先頭の「(未設定)」項目を期待）に逃がす。
    /// 値ありの場合は Items を走査して該当 alias_id の IdLabel を見つけて選択する。
    /// 直接 SelectedValue=null すると WinForms 内部の Dictionary key 検索で
    /// ArgumentNullException(Parameter 'key') が起きる版があるため、明示的に SelectedIndex 操作で行う。
    /// </summary>
    private static void SetNullableAliasComboValue(ComboBox combo, int? value)
    {
        if (combo.Items.Count == 0) return;
        if (!value.HasValue) { combo.SelectedIndex = 0; return; }
        // DataSource バインド時は Items は内部で IdLabel<int?> インスタンスを保持している。
        // ID 一致の項目を探して、そのインデックスを SelectedIndex に立てる。
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is IdLabel<int?> item && item.Id == value.Value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // 該当が無ければ「(未設定)」に逃がす（旧 alias が削除された後の整合性破れ等のフォールバック）
        combo.SelectedIndex = 0;
    }

    /// <summary>プリキュア編集パネルをクリアして「新規追加モード」にする。</summary>
    private void ClearPrecureForm()
    {
        gridPrecures.ClearSelection();
        cboPrCharacter.SelectedIndex = -1;
        cboPrBirthMonth.SelectedIndex = 0;
        cboPrBirthDay.SelectedIndex = 0;
        UpdateBirthdayPreview();
        numPrVoiceActorPersonId.Value = 0;
        lblPrVoiceActorName.Text = "";
        skinColorPicker.SetHsl(null, null, null);
        skinColorPicker.SetRgb(null, null, null);
        txtPrSchool.Text = "";
        txtPrSchoolClass.Text = "";
        txtPrFamilyBusiness.Text = "";
        txtPrNotes.Text = "";
        gridPrFamily.DataSource = new List<CharacterFamilyRelationListRow>();
    }

    /// <summary>誕生日プレビューを更新（和文・英文を 1 行で並列表示）。</summary>
    private void UpdateBirthdayPreview()
    {
        int? month = cboPrBirthMonth.SelectedIndex > 0 ? cboPrBirthMonth.SelectedIndex : null;
        int? day = cboPrBirthDay.SelectedIndex > 0 ? cboPrBirthDay.SelectedIndex : null;
        if (month.HasValue && day.HasValue)
        {
            string ja = $"{month}月{day}日";
            string en = $"{GetEnglishMonthName(month.Value)} {day}";
            lblPrBirthdayPreview.Text = $"{ja}  /  {en}";
            lblPrBirthdayPreview.ForeColor = SystemColors.ControlText;
        }
        else
        {
            lblPrBirthdayPreview.Text = "(未設定)";
            lblPrBirthdayPreview.ForeColor = SystemColors.GrayText;
        }
    }

    /// <summary>1=January ～ 12=December の英名を返す（CultureInfo.InvariantCulture）。</summary>
    private static string GetEnglishMonthName(int month)
    {
        if (month < 1 || month > 12) return "";
        return CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
    }

    /// <summary>声優 person_id 入力欄が変わるたび、人物名をラベルに表示。</summary>
    private async Task ResolvePrVoiceActorNameAsync()
    {
        try
        {
            int id = (int)numPrVoiceActorPersonId.Value;
            if (id <= 0) { lblPrVoiceActorName.Text = ""; return; }
            var p = await _personsRepo.GetByIdAsync(id);
            lblPrVoiceActorName.Text = p is null ? "(該当なし)" : $"→ {p.FullName}";
        }
        catch { lblPrVoiceActorName.Text = ""; }
    }

    /// <summary>プリキュアを保存する（既存選択中なら UPDATE、未選択なら INSERT）。</summary>
    private async Task SavePrecureAsync()
    {
        try
        {
            // バリデーション：必須 alias の確認
            if (cboPrPreTransformAlias.SelectedValue is not int preAliasId || preAliasId <= 0)
            { MessageBox.Show(this, "「変身前」の名義を選択してください。"); return; }
            if (cboPrTransformAlias.SelectedValue is not int transformAliasId || transformAliasId <= 0)
            { MessageBox.Show(this, "「変身後」の名義を選択してください。"); return; }

            // NULL 許容 alias は SelectedValue が int? として返ってくるので注意（DataSource 構造による）
            int? transform2AliasId = ExtractNullableIntFromCombo(cboPrTransform2Alias);
            int? altFormAliasId = ExtractNullableIntFromCombo(cboPrAltFormAlias);

            // 誕生日
            byte? birthMonth = cboPrBirthMonth.SelectedIndex > 0 ? (byte)cboPrBirthMonth.SelectedIndex : null;
            byte? birthDay = cboPrBirthDay.SelectedIndex > 0 ? (byte)cboPrBirthDay.SelectedIndex : null;

            // 声優
            int va = (int)numPrVoiceActorPersonId.Value;
            int? voiceActorPersonId = va > 0 ? va : null;

            // 肌色
            var (h, s, l) = skinColorPicker.GetHsl();
            var (r, g, b) = skinColorPicker.GetRgb();

            bool isUpdate = gridPrecures.CurrentRow?.DataBoundItem is PrecureListRow currentRow
                && currentRow.PrecureId > 0
                && gridPrecures.SelectedRows.Count > 0;

            if (isUpdate)
            {
                // 既存取得 → 上書き保存
                var current = gridPrecures.CurrentRow?.DataBoundItem as PrecureListRow;
                if (current is null) { MessageBox.Show(this, "保存対象が取得できません。"); return; }
                var existing = await _precuresRepo.GetByIdAsync(current.PrecureId);
                if (existing is null) { MessageBox.Show(this, "保存対象が DB から取得できません。"); return; }
                existing.PreTransformAliasId = preAliasId;
                existing.TransformAliasId = transformAliasId;
                existing.Transform2AliasId = transform2AliasId;
                existing.AltFormAliasId = altFormAliasId;
                existing.BirthMonth = birthMonth;
                existing.BirthDay = birthDay;
                existing.VoiceActorPersonId = voiceActorPersonId;
                existing.SkinColorH = h;
                existing.SkinColorS = s;
                existing.SkinColorL = l;
                existing.SkinColorR = r;
                existing.SkinColorG = g;
                existing.SkinColorB = b;
                existing.School = NullIfEmpty(txtPrSchool.Text);
                existing.SchoolClass = NullIfEmpty(txtPrSchoolClass.Text);
                existing.FamilyBusiness = NullIfEmpty(txtPrFamilyBusiness.Text);
                existing.Notes = NullIfEmpty(txtPrNotes.Text);
                existing.UpdatedBy = Environment.UserName;
                await _precuresRepo.UpdateAsync(existing);
            }
            else
            {
                var p = new Precure
                {
                    PreTransformAliasId = preAliasId,
                    TransformAliasId = transformAliasId,
                    Transform2AliasId = transform2AliasId,
                    AltFormAliasId = altFormAliasId,
                    BirthMonth = birthMonth,
                    BirthDay = birthDay,
                    VoiceActorPersonId = voiceActorPersonId,
                    SkinColorH = h,
                    SkinColorS = s,
                    SkinColorL = l,
                    SkinColorR = r,
                    SkinColorG = g,
                    SkinColorB = b,
                    School = NullIfEmpty(txtPrSchool.Text),
                    SchoolClass = NullIfEmpty(txtPrSchoolClass.Text),
                    FamilyBusiness = NullIfEmpty(txtPrFamilyBusiness.Text),
                    Notes = NullIfEmpty(txtPrNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName,
                };
                await _precuresRepo.InsertAsync(p);
            }
            gridPrecures.DataSource = (await _precuresRepo.GetListAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択行のプリキュアを論理削除する。</summary>
    private async Task DeletePrecureAsync()
    {
        try
        {
            if (gridPrecures.CurrentRow?.DataBoundItem is not PrecureListRow row)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"プリキュア #{row.PrecureId}（{row.TransformName}）を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await _precuresRepo.SoftDeleteAsync(row.PrecureId, Environment.UserName);
            gridPrecures.DataSource = (await _precuresRepo.GetListAsync()).ToList();
            ClearPrecureForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// プリキュア編集パネルの「家族グリッド」追加ボタン。続柄・相手キャラの初期値で 1 行追加し、
    /// 即座に DB に保存する（character_family_relations.UpsertAsync 経由）。
    /// </summary>
    private async Task OnPrFamilyAddClickAsync()
    {
        try
        {
            // 編集中プリキュアの「変身前 alias の character_id」を取得
            if (cboPrPreTransformAlias.SelectedValue is not int preAliasId || preAliasId <= 0)
            { MessageBox.Show(this, "プリキュアを選択（または変身前名義を入力）してから家族を追加してください。"); return; }
            var preAlias = await _characterAliasesRepo.GetByIdAsync(preAliasId);
            if (preAlias is null) { MessageBox.Show(this, "変身前名義から character_id を解決できません。"); return; }

            if (cboPrFamilyRelatedCharacter.SelectedValue is not int relatedId || relatedId <= 0)
            { MessageBox.Show(this, "相手キャラを選択してください。"); return; }
            if (cboPrFamilyRelation.SelectedValue is not string relCode || string.IsNullOrEmpty(relCode))
            { MessageBox.Show(this, "続柄を選択してください。"); return; }
            if (relatedId == preAlias.CharacterId)
            { MessageBox.Show(this, "自分自身を家族として登録することはできません。"); return; }

            await _characterFamilyRelationsRepo.UpsertAsync(new CharacterFamilyRelation
            {
                CharacterId = preAlias.CharacterId,
                RelatedCharacterId = relatedId,
                RelationCode = relCode,
                DisplayOrder = null,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName,
            });
            // グリッド更新
            var family = await _characterFamilyRelationsRepo.GetByCharacterWithNamesAsync(preAlias.CharacterId);
            gridPrFamily.DataSource = family.ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>プリキュア編集パネルの「家族グリッド」削除ボタン。</summary>
    private async Task OnPrFamilyRemoveClickAsync()
    {
        try
        {
            if (gridPrFamily.CurrentRow?.DataBoundItem is not CharacterFamilyRelationListRow target)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"{target.RelatedCharacterName}（{target.RelationName}）を家族リストから削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await _characterFamilyRelationsRepo.DeleteAsync(target.CharacterId, target.RelatedCharacterId, target.RelationCode);
            var family = await _characterFamilyRelationsRepo.GetByCharacterWithNamesAsync(target.CharacterId);
            gridPrFamily.DataSource = family.ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ════════════════════════════════════════════════════════════════════
    // 続柄マスタタブ：CRUD
    // ════════════════════════════════════════════════════════════════════

    private void OnCharacterRelationKindRowSelected()
    {
        if (gridCharacterRelationKinds.CurrentRow?.DataBoundItem is CharacterRelationKind r)
        {
            txtCrkRelationCode.Text = r.RelationCode;
            txtCrkNameJa.Text = r.NameJa;
            txtCrkNameEn.Text = r.NameEn ?? "";
            numCrkDisplayOrder.Value = r.DisplayOrder ?? 0;
            txtCrkNotes.Text = r.Notes ?? "";
        }
    }

    private void ClearCharacterRelationKindForm()
    {
        gridCharacterRelationKinds.ClearSelection();
        txtCrkRelationCode.Text = "";
        txtCrkNameJa.Text = "";
        txtCrkNameEn.Text = "";
        numCrkDisplayOrder.Value = 0;
        txtCrkNotes.Text = "";
    }

    private async Task SaveCharacterRelationKindAsync()
    {
        try
        {
            string code = (txtCrkRelationCode.Text ?? "").Trim();
            if (code.Length == 0) { MessageBox.Show(this, "続柄コードを入力してください。"); return; }
            string nameJa = (txtCrkNameJa.Text ?? "").Trim();
            if (nameJa.Length == 0) { MessageBox.Show(this, "日本語表示名を入力してください。"); return; }

            var existing = await _characterRelationKindsRepo.GetByCodeAsync(code);
            byte? displayOrder = numCrkDisplayOrder.Value > 0 ? (byte)numCrkDisplayOrder.Value : null;
            var row = new CharacterRelationKind
            {
                RelationCode = code,
                NameJa = nameJa,
                NameEn = NullIfEmpty(txtCrkNameEn.Text),
                DisplayOrder = displayOrder,
                Notes = NullIfEmpty(txtCrkNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName,
            };
            if (existing is null) await _characterRelationKindsRepo.InsertAsync(row);
            else await _characterRelationKindsRepo.UpdateAsync(row);

            gridCharacterRelationKinds.DataSource = (await _characterRelationKindsRepo.GetAllAsync()).ToList();
            // 家族関係タブとプリキュア家族コンボもマスタ追加に追随
            var rels = await _characterRelationKindsRepo.GetAllAsync();
            cboCfrRelation.DataSource = rels.Select(r => new IdLabel<string>(r.RelationCode, $"{r.NameJa}（{r.RelationCode}）")).ToList();
            cboPrFamilyRelation.DataSource = rels.Select(r => new IdLabel<string>(r.RelationCode, $"{r.NameJa}（{r.RelationCode}）")).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCharacterRelationKindAsync()
    {
        try
        {
            if (gridCharacterRelationKinds.CurrentRow?.DataBoundItem is not CharacterRelationKind r)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"続柄 [{r.RelationCode}] {r.NameJa} を削除しますか？\n（家族関係から参照されている場合は失敗します）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await _characterRelationKindsRepo.DeleteAsync(r.RelationCode);
            gridCharacterRelationKinds.DataSource = (await _characterRelationKindsRepo.GetAllAsync()).ToList();
            ClearCharacterRelationKindForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ════════════════════════════════════════════════════════════════════
    // 家族関係タブ：メモリ DataSource を介した CRUD
    // ════════════════════════════════════════════════════════════════════

    /// <summary>「自分」キャラ選択時、そのキャラの家族関係をグリッドにロードする。</summary>
    private async Task ReloadCfrGridAsync()
    {
        try
        {
            if (cboCfrCharacter.SelectedValue is not int characterId) { _cfrRows = new(); gridCharacterFamilyRelations.DataSource = _cfrRows; return; }
            var rows = await _characterFamilyRelationsRepo.GetByCharacterWithNamesAsync(characterId);
            _cfrRows = rows.ToList();
            gridCharacterFamilyRelations.DataSource = _cfrRows;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnCfrRowSelected()
    {
        if (gridCharacterFamilyRelations.CurrentRow?.DataBoundItem is CharacterFamilyRelationListRow row)
        {
            cboCfrRelatedCharacter.SelectedValue = row.RelatedCharacterId;
            cboCfrRelation.SelectedValue = row.RelationCode;
            numCfrDisplayOrder.Value = row.DisplayOrder ?? 0;
            txtCfrNotes.Text = row.Notes ?? "";
        }
    }

    /// <summary>家族関係タブの「+追加」ボタン。フォームの値で _cfrRows に新規行を追加する（メモリのみ）。</summary>
    private void OnCfrAddClick()
    {
        if (cboCfrCharacter.SelectedValue is not int characterId)
        { MessageBox.Show(this, "「自分」キャラを選択してください。"); return; }
        if (cboCfrRelatedCharacter.SelectedValue is not int relatedId)
        { MessageBox.Show(this, "相手キャラを選択してください。"); return; }
        if (cboCfrRelation.SelectedValue is not string relCode || string.IsNullOrEmpty(relCode))
        { MessageBox.Show(this, "続柄を選択してください。"); return; }
        if (characterId == relatedId) { MessageBox.Show(this, "自分自身は家族にできません。"); return; }
        if (_cfrRows.Any(x => x.RelatedCharacterId == relatedId && x.RelationCode == relCode))
        { MessageBox.Show(this, "同じ「相手キャラ + 続柄」の行は既に存在します。"); return; }

        // 表示名はコンボの選択ラベルから抽出（"#42  美墨ほのか" 形式）
        string relatedName = ExtractLabelAfterId(cboCfrRelatedCharacter.Text);
        string relationName = ExtractLabelBeforeParen(cboCfrRelation.Text);
        _cfrRows.Add(new CharacterFamilyRelationListRow
        {
            CharacterId = characterId,
            RelatedCharacterId = relatedId,
            RelatedCharacterName = relatedName,
            RelationCode = relCode,
            RelationName = relationName,
            DisplayOrder = numCfrDisplayOrder.Value > 0 ? (byte)numCfrDisplayOrder.Value : null,
            Notes = NullIfEmpty(txtCfrNotes.Text),
        });
        // BindingSource を介さない再バインドで反映
        gridCharacterFamilyRelations.DataSource = null;
        gridCharacterFamilyRelations.DataSource = _cfrRows;
    }

    private void OnCfrRemoveClick()
    {
        if (gridCharacterFamilyRelations.CurrentRow?.DataBoundItem is not CharacterFamilyRelationListRow target)
        { MessageBox.Show(this, "削除対象を選択してください。"); return; }
        _cfrRows.Remove(target);
        gridCharacterFamilyRelations.DataSource = null;
        gridCharacterFamilyRelations.DataSource = _cfrRows;
    }

    /// <summary>家族関係タブの「保存（置き換え）」ボタン。character_id 単位で全行を置き換える。</summary>
    private async Task SaveCfrAsync()
    {
        try
        {
            if (cboCfrCharacter.SelectedValue is not int characterId)
            { MessageBox.Show(this, "「自分」キャラを選択してください。"); return; }
            var entities = _cfrRows.Select(r => new CharacterFamilyRelation
            {
                CharacterId = characterId,
                RelatedCharacterId = r.RelatedCharacterId,
                RelationCode = r.RelationCode,
                DisplayOrder = r.DisplayOrder,
                Notes = r.Notes,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName,
            }).ToList();
            await _characterFamilyRelationsRepo.ReplaceAllForCharacterAsync(characterId, entities);
            MessageBox.Show(this, $"#{characterId} の家族関係を {entities.Count} 件で置き換えました。", "保存完了",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ReloadCfrGridAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ════════════════════════════════════════════════════════════════════
    // ヘルパ
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// NULL 許容コンボの SelectedValue を int? に解す。
    /// DataSource が <see cref="IdLabel{T}"/>(int?) のときは NULL も保持できる構造。
    /// </summary>
    private static int? ExtractNullableIntFromCombo(ComboBox combo)
    {
        if (combo.SelectedItem is IdLabel<int?> item) return item.Id;
        // フォールバック：SelectedValue が int として取れる場合（DataSource が IdLabel<int> 由来のとき）
        if (combo.SelectedValue is int i) return i > 0 ? i : null;
        return null;
    }

    /// <summary>「#42  美墨ほのか」のような表示文字列から ID 部分を取り除いた残りを返す。</summary>
    private static string ExtractLabelAfterId(string display)
    {
        if (string.IsNullOrEmpty(display)) return "";
        int idx = display.IndexOf("  ", StringComparison.Ordinal);
        return idx >= 0 ? display.Substring(idx + 2) : display;
    }

    /// <summary>「父（FATHER）」のような表示文字列から括弧前を取り出す。</summary>
    private static string ExtractLabelBeforeParen(string display)
    {
        if (string.IsNullOrEmpty(display)) return "";
        int idx = display.IndexOf('（');
        return idx > 0 ? display.Substring(0, idx) : display;
    }
}

/// <summary>
/// ジェネリックな (Id, Label) 組。
/// プリキュアタブ／家族関係タブのコンボで int / int? / string を ID 型として扱うためのヘルパ。
/// 既存コードでは <see cref="IdLabel"/>（非ジェネリック）を使っており、新タブのみ用途別に追加する。
/// </summary>
internal sealed record IdLabel<TId>(TId Id, string Label);