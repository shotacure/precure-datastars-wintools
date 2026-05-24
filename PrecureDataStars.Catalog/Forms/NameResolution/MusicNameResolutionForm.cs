using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapper;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

/// <summary>
/// 音楽の名寄せセンター。
/// フリーテキストのみが入っていて構造化エントリ（song_credits / song_recording_singers）が
/// 未登録の曲・録音を一覧表示し、フリーテキストを自動でトークン分解して
/// alias マスタへの厳密一致候補を提示する。ユーザーは候補確認と区切り選択だけで
/// ワンクリック登録できる。
/// 4 タブ構成：「作詞」「作曲」「編曲」「歌唱者」。
/// 歌唱者タブのみ CHARACTER_WITH_CV パターンも扱う。
/// マスタへの自動 INSERT は一切行わない（未マッチ語は手動 Picker を促す）。
/// 入力作業が完了し次第このフォームは撤去する方針のため、起動口は MainForm のメニュー
/// 専用とし、SongsEditorForm への埋め込みは行わない。
/// </summary>
public partial class MusicNameResolutionForm : Form
{
    private readonly IConnectionFactory _factory;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;

    // 役職コード（"LYRICS"/"COMPOSITION"/"ARRANGEMENT"）→ そのタブの UI 一式。
    private readonly Dictionary<string, RoleTabContext> _personTabs = new();
    private RoleTabContext? _vocalsTab;

    /// <summary>共通の連名区切り候補。「&amp;」「&」のような半角・全角を網羅。</summary>
    private static readonly string[] SeparatorChoices = new[]
    {
        "、", "，", "・", "／", "/", "＆", "&", ",", " ", " with ", " feat. "
    };

    public MusicNameResolutionForm(
        IConnectionFactory factory,
        PersonAliasesRepository personAliasesRepo,
        CharacterAliasesRepository characterAliasesRepo,
        SongCreditsRepository songCreditsRepo,
        SongRecordingSingersRepository songRecordingSingersRepo)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _songCreditsRepo = songCreditsRepo ?? throw new ArgumentNullException(nameof(songCreditsRepo));
        _songRecordingSingersRepo = songRecordingSingersRepo ?? throw new ArgumentNullException(nameof(songRecordingSingersRepo));

        InitializeComponent();

        BuildPersonTab(tabLyrics,      SongCreditRoles.Lyrics);
        BuildPersonTab(tabComposition, SongCreditRoles.Composition);
        BuildPersonTab(tabArrangement, SongCreditRoles.Arrangement);
        BuildVocalsTab(tabVocals);

        Load += async (_, __) => await ReloadAllAsync();
        tabRoles.SelectedIndexChanged += async (_, __) => await ReloadActiveTabAsync();
    }

    /// <summary>全タブの未解決リストを一括ロードする（初回表示用）。</summary>
    private async Task ReloadAllAsync()
    {
        try
        {
            foreach (var (_, ctx) in _personTabs) await ReloadPersonTabAsync(ctx);
            if (_vocalsTab is not null) await ReloadVocalsTabAsync(_vocalsTab);
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>現在表示中のタブだけ再読込（タブ切替時の鮮度確保用）。</summary>
    private async Task ReloadActiveTabAsync()
    {
        try
        {
            if (ReferenceEquals(tabRoles.SelectedTab, tabVocals))
            {
                if (_vocalsTab is not null) await ReloadVocalsTabAsync(_vocalsTab);
            }
            else
            {
                foreach (var (_, ctx) in _personTabs)
                {
                    if (ReferenceEquals(tabRoles.SelectedTab, ctx.Page))
                    {
                        await ReloadPersonTabAsync(ctx);
                        break;
                    }
                }
            }
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>ステータスバーの残件数を再計算して表示する。</summary>
    private void UpdateStatus()
    {
        int total = _personTabs.Values.Sum(c => c.UnresolvedItems.Count)
                  + (_vocalsTab?.VocalsUnresolvedItems.Count ?? 0);
        lblStatus.Text = $"未解決件数 合計: {total} 件（作詞: {_personTabs[SongCreditRoles.Lyrics].UnresolvedItems.Count} / "
            + $"作曲: {_personTabs[SongCreditRoles.Composition].UnresolvedItems.Count} / "
            + $"編曲: {_personTabs[SongCreditRoles.Arrangement].UnresolvedItems.Count} / "
            + $"歌唱者: {_vocalsTab?.VocalsUnresolvedItems.Count ?? 0}）";
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    //  PERSON 系タブ（作詞 / 作曲 / 編曲）

    /// <summary>PERSON 系タブの UI を構築する。</summary>
    private void BuildPersonTab(TabPage page, string role)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 480,
            FixedPanel = FixedPanel.Panel1
        };
        page.Controls.Add(split);

        // 左：未解決リスト
        var gridList = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "曲ID",       DataPropertyName = nameof(PersonItem.SongId),    Width = 60 });
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "タイトル",    DataPropertyName = nameof(PersonItem.Title),     AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "フリーテキスト", DataPropertyName = nameof(PersonItem.FreeText),  Width = 240 });
        split.Panel1.Controls.Add(gridList);

        // 右：トークン分解 + 操作ボタン
        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.Panel2.Controls.Add(rightLayout);

        var lblFreeText = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "（曲を選択するとフリーテキストが表示されます）",
            ForeColor = SystemColors.GrayText
        };
        rightLayout.Controls.Add(lblFreeText, 0, 0);

        var gridTokens = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EditMode = DataGridViewEditMode.EditOnEnter
        };

        // Sep / Raw / Alias-display / 選択ボタン / 状態
        var colSep = new DataGridViewComboBoxColumn
        {
            HeaderText = "区切り",
            DataPropertyName = nameof(PersonTokenRow.PrecedingSeparator),
            Width = 80,
            FlatStyle = FlatStyle.Flat
        };
        foreach (var s in SeparatorChoices) colSep.Items.Add(s);
        gridTokens.Columns.Add(colSep);

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "テキスト",
            DataPropertyName = nameof(PersonTokenRow.RawText),
            ReadOnly = true,
            Width = 180
        });

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名義（解決後）",
            DataPropertyName = nameof(PersonTokenRow.AliasDisplay),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        gridTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "選択",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 50
        });

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状態",
            DataPropertyName = nameof(PersonTokenRow.StatusGlyph),
            ReadOnly = true,
            Width = 50
        });

        rightLayout.Controls.Add(gridTokens, 0, 1);

        var btnApply = new Button { Text = "この内容で登録", Dock = DockStyle.Right, Width = 200, Enabled = false };
        var btnReParse = new Button { Text = "再分解", Dock = DockStyle.Left, Width = 100 };
        var btnPanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        btnPanel.Controls.Add(btnReParse);
        btnPanel.Controls.Add(btnApply);
        rightLayout.Controls.Add(btnPanel, 0, 2);

        var ctx = new RoleTabContext
        {
            Page = page,
            Role = role,
            GridList = gridList,
            GridTokens = gridTokens,
            LblFreeText = lblFreeText,
            BtnApply = btnApply
        };
        _personTabs[role] = ctx;

        gridList.SelectionChanged += async (_, __) => await OnPersonItemSelectedAsync(ctx);
        gridTokens.CellClick += async (s, e) =>
        {
            if (e.RowIndex < 0) return;
            // 「選択」ボタン列のクリック時に Picker を開く。
            if (gridTokens.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
                await OnPickPersonAsync(ctx, e.RowIndex);
        };
        // ComboBox セルの編集確定で即時保持する。
        gridTokens.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            RefreshApplyButton(ctx);
        };
        gridTokens.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (gridTokens.IsCurrentCellDirty) gridTokens.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        btnApply.Click += async (_, __) => await OnApplyPersonAsync(ctx);
        btnReParse.Click += async (_, __) => await OnPersonItemSelectedAsync(ctx);
    }

    /// <summary>PERSON 系タブの未解決リストを再ロードする。</summary>
    private async Task ReloadPersonTabAsync(RoleTabContext ctx)
    {
        string col = ctx.Role switch
        {
            SongCreditRoles.Lyrics      => "lyricist_name",
            SongCreditRoles.Composition => "composer_name",
            SongCreditRoles.Arrangement => "arranger_name",
            _ => throw new InvalidOperationException()
        };
        string sql = $"""
            SELECT
              s.song_id AS SongId,
              s.title   AS Title,
              s.{col}   AS FreeText
            FROM songs s
            WHERE s.is_deleted = 0
              AND s.{col} IS NOT NULL AND s.{col} <> ''
              AND NOT EXISTS (
                SELECT 1 FROM song_credits sc
                WHERE sc.song_id = s.song_id AND sc.credit_role = @role
              )
            ORDER BY s.song_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync();
        var rows = (await conn.QueryAsync<PersonItem>(new CommandDefinition(sql, new { role = ctx.Role }))).ToList();

        ctx.UnresolvedItems.Clear();
        foreach (var r in rows) ctx.UnresolvedItems.Add(r);
        ctx.GridList.DataSource = null;
        ctx.GridList.DataSource = ctx.UnresolvedItems;

        ctx.TokenRows.Clear();
        ctx.GridTokens.DataSource = null;
        ctx.LblFreeText.Text = "（曲を選択するとフリーテキストが表示されます）";
        ctx.LblFreeText.ForeColor = SystemColors.GrayText;
        ctx.BtnApply.Enabled = false;
    }

    /// <summary>PERSON 系タブで曲が選択されたときの処理。フリーテキストを分解して候補を提示する。</summary>
    private async Task OnPersonItemSelectedAsync(RoleTabContext ctx)
    {
        if (ctx.GridList.CurrentRow?.DataBoundItem is not PersonItem item) return;

        ctx.LblFreeText.Text = $"フリーテキスト: {item.FreeText}";
        ctx.LblFreeText.ForeColor = SystemColors.ControlText;

        var tokens = MusicNameTokenizer.Tokenize(item.FreeText);
        ctx.TokenRows.Clear();
        foreach (var tok in tokens)
        {
            // CV パターンが検出された場合でも、PERSON 系タブでは MainPart を「テキスト」として扱う。
            string text = tok.MainPart ?? tok.RawText;
            var candidates = await _personAliasesRepo.FindByExactNameAsync(text);
            var row = new PersonTokenRow
            {
                PrecedingSeparator = ctx.TokenRows.Count == 0 ? null : (tok.PrecedingSeparator ?? "、"),
                RawText = text,
                AliasId = candidates.Count == 1 ? candidates[0].AliasId : (int?)null,
                AliasDisplay = candidates.Count == 1
                    ? await _personAliasesRepo.GetDisplayNameAsync(candidates[0].AliasId)
                    : (candidates.Count == 0 ? "(未マッチ)" : $"({candidates.Count} 候補：選択してください)")
            };
            ctx.TokenRows.Add(row);
        }
        ctx.GridTokens.DataSource = null;
        ctx.GridTokens.DataSource = ctx.TokenRows;
        RefreshApplyButton(ctx);
    }

    /// <summary>「...」ボタン押下時に PersonAliasPicker を開いて 1 行に alias_id を確定する。</summary>
    private async Task OnPickPersonAsync(RoleTabContext ctx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= ctx.TokenRows.Count) return;
        var row = ctx.TokenRows[rowIndex];

        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        row.AliasId = dlg.SelectedId.Value;
        row.AliasDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
        ctx.GridTokens.Refresh();
        RefreshApplyButton(ctx);
    }

    /// <summary>PERSON 系タブの「登録」ボタン。song_credits にトランザクション一括 INSERT。</summary>
    private async Task OnApplyPersonAsync(RoleTabContext ctx)
    {
        if (ctx.GridList.CurrentRow?.DataBoundItem is not PersonItem item) return;
        if (ctx.TokenRows.Count == 0 || ctx.TokenRows.Any(r => !r.AliasId.HasValue)) return;

        try
        {
            var credits = ctx.TokenRows.Select((r, i) => new SongCredit
            {
                SongId = item.SongId,
                CreditRole = ctx.Role,
                CreditSeq = (byte)(i + 1),
                PersonAliasId = r.AliasId!.Value,
                PrecedingSeparator = i == 0 ? null : r.PrecedingSeparator
            }).ToList();

            await _songCreditsRepo.ReplaceAllByRoleAsync(item.SongId, ctx.Role, credits, Environment.UserName);

            // 登録できたら未解決リストから除外。
            ctx.UnresolvedItems.Remove(item);
            ctx.GridList.DataSource = null;
            ctx.GridList.DataSource = ctx.UnresolvedItems;
            ctx.TokenRows.Clear();
            ctx.GridTokens.DataSource = null;
            ctx.LblFreeText.Text = "（登録完了。次の曲を選択してください）";
            ctx.LblFreeText.ForeColor = SystemColors.GrayText;
            ctx.BtnApply.Enabled = false;
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    //  歌唱者タブ（VOCALS）

    /// <summary>歌唱者タブの UI を構築する。CV パターンへの対応で列が増える。</summary>
    private void BuildVocalsTab(TabPage page)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 480,
            FixedPanel = FixedPanel.Panel1
        };
        page.Controls.Add(split);

        var gridList = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "録音ID",     DataPropertyName = nameof(VocalsItem.SongRecordingId), Width = 60 });
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "曲タイトル", DataPropertyName = nameof(VocalsItem.Title),           AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "フリーテキスト", DataPropertyName = nameof(VocalsItem.FreeText),    Width = 240 });
        split.Panel1.Controls.Add(gridList);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.Panel2.Controls.Add(rightLayout);

        var lblFreeText = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "（録音を選択するとフリーテキストが表示されます）",
            ForeColor = SystemColors.GrayText
        };
        rightLayout.Controls.Add(lblFreeText, 0, 0);

        var gridTokens = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EditMode = DataGridViewEditMode.EditOnEnter
        };

        // 区切り
        var colSep = new DataGridViewComboBoxColumn
        {
            HeaderText = "区切り",
            DataPropertyName = nameof(VocalsTokenRow.PrecedingSeparator),
            Width = 70,
            FlatStyle = FlatStyle.Flat
        };
        foreach (var s in SeparatorChoices) colSep.Items.Add(s);
        gridTokens.Columns.Add(colSep);

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "原テキスト",
            DataPropertyName = nameof(VocalsTokenRow.RawText),
            ReadOnly = true,
            Width = 150
        });

        var colKind = new DataGridViewComboBoxColumn
        {
            HeaderText = "種別",
            DataPropertyName = nameof(VocalsTokenRow.BillingKindStr),
            Width = 100,
            FlatStyle = FlatStyle.Flat
        };
        colKind.Items.Add("PERSON");
        colKind.Items.Add("CHARACTER_WITH_CV");
        gridTokens.Columns.Add(colKind);

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "主名義（人物 or キャラ）",
            DataPropertyName = nameof(VocalsTokenRow.MainDisplay),
            ReadOnly = true,
            Width = 180
        });
        gridTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "主",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "スラッシュ相方",
            DataPropertyName = nameof(VocalsTokenRow.SlashDisplay),
            ReadOnly = true,
            Width = 160
        });
        gridTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "/",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "CV（人物名義）",
            DataPropertyName = nameof(VocalsTokenRow.VoiceDisplay),
            ReadOnly = true,
            Width = 160
        });
        gridTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "CV",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });

        gridTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状態",
            DataPropertyName = nameof(VocalsTokenRow.StatusGlyph),
            ReadOnly = true,
            Width = 50
        });

        rightLayout.Controls.Add(gridTokens, 0, 1);

        var btnApply = new Button { Text = "この内容で登録", Dock = DockStyle.Right, Width = 200, Enabled = false };
        var btnReParse = new Button { Text = "再分解", Dock = DockStyle.Left, Width = 100 };
        var btnPanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        btnPanel.Controls.Add(btnReParse);
        btnPanel.Controls.Add(btnApply);
        rightLayout.Controls.Add(btnPanel, 0, 2);

        var ctx = new RoleTabContext
        {
            Page = page,
            Role = SongRecordingSingerRoles.Vocals,
            GridList = gridList,
            GridTokens = gridTokens,
            LblFreeText = lblFreeText,
            BtnApply = btnApply
        };
        _vocalsTab = ctx;

        gridList.SelectionChanged += async (_, __) => await OnVocalsItemSelectedAsync(ctx);
        gridTokens.CellClick += async (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var col = gridTokens.Columns[e.ColumnIndex];
            if (col is DataGridViewButtonColumn)
            {
                // 列のヘッダで分岐：主／スラッシュ／CV
                string header = col.HeaderText;
                if (header == "主") await OnPickVocalsMainAsync(ctx, e.RowIndex);
                else if (header == "/") await OnPickVocalsSlashAsync(ctx, e.RowIndex);
                else if (header == "CV") await OnPickVocalsCvAsync(ctx, e.RowIndex);
            }
        };
        gridTokens.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (gridTokens.IsCurrentCellDirty) gridTokens.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        gridTokens.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            // 種別変更があったときは状態を再計算する。
            RefreshApplyButton(ctx);
        };
        btnApply.Click += async (_, __) => await OnApplyVocalsAsync(ctx);
        btnReParse.Click += async (_, __) => await OnVocalsItemSelectedAsync(ctx);
    }

    /// <summary>歌唱者タブの未解決リストを再ロードする。</summary>
    private async Task ReloadVocalsTabAsync(RoleTabContext ctx)
    {
        const string sql = """
            SELECT
              sr.song_recording_id AS SongRecordingId,
              sr.song_id           AS SongId,
              s.title              AS Title,
              sr.singer_name       AS FreeText
            FROM song_recordings sr
            LEFT JOIN songs s ON s.song_id = sr.song_id
            WHERE sr.is_deleted = 0
              AND sr.singer_name IS NOT NULL AND sr.singer_name <> ''
              AND NOT EXISTS (
                SELECT 1 FROM song_recording_singers srs
                WHERE srs.song_recording_id = sr.song_recording_id
                  AND srs.role_code = @role
              )
            ORDER BY sr.song_recording_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync();
        var rows = (await conn.QueryAsync<VocalsItem>(new CommandDefinition(sql, new { role = SongRecordingSingerRoles.Vocals }))).ToList();

        ctx.VocalsUnresolvedItems.Clear();
        foreach (var r in rows) ctx.VocalsUnresolvedItems.Add(r);
        ctx.GridList.DataSource = null;
        ctx.GridList.DataSource = ctx.VocalsUnresolvedItems;

        ctx.VocalsTokenRows.Clear();
        ctx.GridTokens.DataSource = null;
        ctx.LblFreeText.Text = "（録音を選択するとフリーテキストが表示されます）";
        ctx.LblFreeText.ForeColor = SystemColors.GrayText;
        ctx.BtnApply.Enabled = false;
    }

    /// <summary>歌唱者タブで録音が選択されたときの処理。CV パターンも含めて自動解決する。</summary>
    private async Task OnVocalsItemSelectedAsync(RoleTabContext ctx)
    {
        if (ctx.GridList.CurrentRow?.DataBoundItem is not VocalsItem item) return;

        ctx.LblFreeText.Text = $"フリーテキスト: {item.FreeText}";
        ctx.LblFreeText.ForeColor = SystemColors.ControlText;

        var tokens = MusicNameTokenizer.Tokenize(item.FreeText);
        ctx.VocalsTokenRows.Clear();
        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            var row = new VocalsTokenRow
            {
                PrecedingSeparator = i == 0 ? null : (tok.PrecedingSeparator ?? "、"),
                RawText = tok.RawText,
                BillingKindStr = tok.IsCharacterWithCv ? "CHARACTER_WITH_CV" : "PERSON"
            };

            if (tok.IsCharacterWithCv)
            {
                // メイン：キャラ名義
                var mainCands = await _characterAliasesRepo.FindByExactNameAsync(tok.MainPart!);
                if (mainCands.Count == 1)
                {
                    row.CharacterAliasId = mainCands[0].AliasId;
                    row.MainDisplay = mainCands[0].Name;
                }
                else
                {
                    row.MainDisplay = mainCands.Count == 0 ? "(未マッチ)" : $"({mainCands.Count} 候補：選択)";
                }

                // スラッシュ相方：キャラ名義
                if (tok.SlashPart is not null)
                {
                    var slashCands = await _characterAliasesRepo.FindByExactNameAsync(tok.SlashPart);
                    if (slashCands.Count == 1)
                    {
                        row.SlashCharacterAliasId = slashCands[0].AliasId;
                        row.SlashDisplay = slashCands[0].Name;
                    }
                    else
                    {
                        row.SlashDisplay = slashCands.Count == 0 ? "(未マッチ)" : $"({slashCands.Count} 候補：選択)";
                    }
                }

                // CV：人物名義
                var voiceCands = await _personAliasesRepo.FindByExactNameAsync(tok.VoicePart!);
                if (voiceCands.Count == 1)
                {
                    row.VoicePersonAliasId = voiceCands[0].AliasId;
                    row.VoiceDisplay = await _personAliasesRepo.GetDisplayNameAsync(voiceCands[0].AliasId);
                }
                else
                {
                    row.VoiceDisplay = voiceCands.Count == 0 ? "(未マッチ)" : $"({voiceCands.Count} 候補：選択)";
                }
            }
            else
            {
                // PERSON: 主名義のみ
                var cands = await _personAliasesRepo.FindByExactNameAsync(tok.RawText);
                if (cands.Count == 1)
                {
                    row.PersonAliasId = cands[0].AliasId;
                    row.MainDisplay = await _personAliasesRepo.GetDisplayNameAsync(cands[0].AliasId);
                }
                else
                {
                    row.MainDisplay = cands.Count == 0 ? "(未マッチ)" : $"({cands.Count} 候補：選択)";
                }
            }

            ctx.VocalsTokenRows.Add(row);
        }

        ctx.GridTokens.DataSource = null;
        ctx.GridTokens.DataSource = ctx.VocalsTokenRows;
        RefreshApplyButton(ctx);
    }

    /// <summary>主名義 Picker。種別に応じて人物名義 or キャラ名義を開く。</summary>
    private async Task OnPickVocalsMainAsync(RoleTabContext ctx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= ctx.VocalsTokenRows.Count) return;
        var row = ctx.VocalsTokenRows[rowIndex];

        if (row.BillingKindStr == "CHARACTER_WITH_CV")
        {
            using var dlg = new CharacterAliasPickerDialog(_characterAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            var alias = await _characterAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
            row.CharacterAliasId = dlg.SelectedId.Value;
            row.MainDisplay = alias?.Name ?? "";
            row.PersonAliasId = null;
        }
        else
        {
            using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            row.PersonAliasId = dlg.SelectedId.Value;
            row.MainDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
            row.CharacterAliasId = null;
        }

        ctx.GridTokens.Refresh();
        RefreshApplyButton(ctx);
    }

    /// <summary>スラッシュ相方 Picker。種別に応じて人物名義 or キャラ名義を開く。</summary>
    private async Task OnPickVocalsSlashAsync(RoleTabContext ctx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= ctx.VocalsTokenRows.Count) return;
        var row = ctx.VocalsTokenRows[rowIndex];

        if (row.BillingKindStr == "CHARACTER_WITH_CV")
        {
            using var dlg = new CharacterAliasPickerDialog(_characterAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            var alias = await _characterAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
            row.SlashCharacterAliasId = dlg.SelectedId.Value;
            row.SlashDisplay = alias?.Name ?? "";
            row.SlashPersonAliasId = null;
        }
        else
        {
            using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
            row.SlashPersonAliasId = dlg.SelectedId.Value;
            row.SlashDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
            row.SlashCharacterAliasId = null;
        }

        ctx.GridTokens.Refresh();
        RefreshApplyButton(ctx);
    }

    /// <summary>CV（声優）Picker。常に人物名義。</summary>
    private async Task OnPickVocalsCvAsync(RoleTabContext ctx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= ctx.VocalsTokenRows.Count) return;
        var row = ctx.VocalsTokenRows[rowIndex];
        if (row.BillingKindStr != "CHARACTER_WITH_CV") return;

        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        row.VoicePersonAliasId = dlg.SelectedId.Value;
        row.VoiceDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
        ctx.GridTokens.Refresh();
        RefreshApplyButton(ctx);
    }

    /// <summary>歌唱者タブの「登録」ボタン。song_recording_singers にトランザクション一括 INSERT。</summary>
    private async Task OnApplyVocalsAsync(RoleTabContext ctx)
    {
        if (ctx.GridList.CurrentRow?.DataBoundItem is not VocalsItem item) return;
        if (!AreAllVocalsRowsResolved(ctx)) return;

        try
        {
            var singers = ctx.VocalsTokenRows.Select((r, i) =>
            {
                var kind = r.BillingKindStr == "CHARACTER_WITH_CV"
                    ? SingerBillingKind.CharacterWithCv
                    : SingerBillingKind.Person;
                return new SongRecordingSinger
                {
                    SongRecordingId = item.SongRecordingId,
                    RoleCode = SongRecordingSingerRoles.Vocals,
                    SingerSeq = (byte)(i + 1),
                    BillingKind = kind,
                    PersonAliasId = kind == SingerBillingKind.Person ? r.PersonAliasId : null,
                    CharacterAliasId = kind == SingerBillingKind.CharacterWithCv ? r.CharacterAliasId : null,
                    VoicePersonAliasId = kind == SingerBillingKind.CharacterWithCv ? r.VoicePersonAliasId : null,
                    SlashPersonAliasId = kind == SingerBillingKind.Person ? r.SlashPersonAliasId : null,
                    SlashCharacterAliasId = kind == SingerBillingKind.CharacterWithCv ? r.SlashCharacterAliasId : null,
                    PrecedingSeparator = i == 0 ? null : r.PrecedingSeparator
                };
            }).ToList();

            await _songRecordingSingersRepo.ReplaceAllByRoleAsync(
                item.SongRecordingId, SongRecordingSingerRoles.Vocals, singers, Environment.UserName);

            ctx.VocalsUnresolvedItems.Remove(item);
            ctx.GridList.DataSource = null;
            ctx.GridList.DataSource = ctx.VocalsUnresolvedItems;
            ctx.VocalsTokenRows.Clear();
            ctx.GridTokens.DataSource = null;
            ctx.LblFreeText.Text = "（登録完了。次の録音を選択してください）";
            ctx.LblFreeText.ForeColor = SystemColors.GrayText;
            ctx.BtnApply.Enabled = false;
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>歌唱者トークンの全行が登録可能か（必須の alias_id が揃っているか）を判定する。</summary>
    private static bool AreAllVocalsRowsResolved(RoleTabContext ctx)
    {
        if (ctx.VocalsTokenRows.Count == 0) return false;
        foreach (var r in ctx.VocalsTokenRows)
        {
            if (r.BillingKindStr == "CHARACTER_WITH_CV")
            {
                if (!r.CharacterAliasId.HasValue || !r.VoicePersonAliasId.HasValue) return false;
            }
            else
            {
                if (!r.PersonAliasId.HasValue) return false;
            }
        }
        return true;
    }

    /// <summary>RefreshApplyButton の歌唱者用オーバーロード。</summary>
    private static void RefreshApplyButton(RoleTabContext ctx)
    {
        bool ok;
        if (ctx.VocalsTokenRows.Count > 0)
            ok = AreAllVocalsRowsResolved(ctx);
        else
            ok = ctx.TokenRows.Count > 0 && ctx.TokenRows.All(r => r.AliasId.HasValue);

        // 各行の状態列を更新。
        foreach (var r in ctx.TokenRows) r.StatusGlyph = r.AliasId.HasValue ? "✓" : "✗";
        foreach (var r in ctx.VocalsTokenRows)
        {
            if (r.BillingKindStr == "CHARACTER_WITH_CV")
                r.StatusGlyph = (r.CharacterAliasId.HasValue && r.VoicePersonAliasId.HasValue) ? "✓" : "✗";
            else
                r.StatusGlyph = r.PersonAliasId.HasValue ? "✓" : "✗";
        }
        ctx.GridTokens.Refresh();
        ctx.BtnApply.Enabled = ok;
        ctx.BtnApply.BackColor = ok ? Color.LightGreen : SystemColors.Control;
    }

    //  内部データ構造

    /// <summary>1 タブ分の UI と状態をまとめて保持するコンテキスト。</summary>
    private sealed class RoleTabContext
    {
        public required TabPage Page { get; init; }
        public required string Role { get; init; }
        public required DataGridView GridList { get; init; }
        public required DataGridView GridTokens { get; init; }
        public required Label LblFreeText { get; init; }
        public required Button BtnApply { get; init; }

        // PERSON 系タブ用。
        public BindingList<PersonItem> UnresolvedItems { get; } = new();
        public BindingList<PersonTokenRow> TokenRows { get; } = new();

        // 歌唱者タブ用（重複保持を避けるため UnresolvedItems は使い分け）。
        public BindingList<VocalsItem> VocalsUnresolvedItems { get; } = new();
        public BindingList<VocalsTokenRow> VocalsTokenRows { get; } = new();
    }

    /// <summary>PERSON 系タブの未解決リスト行（曲単位）。</summary>
    private sealed class PersonItem
    {
        public int SongId { get; set; }
        public string Title { get; set; } = "";
        public string FreeText { get; set; } = "";
    }

    /// <summary>歌唱者タブの未解決リスト行（録音単位）。</summary>
    private sealed class VocalsItem
    {
        public int SongRecordingId { get; set; }
        public int SongId { get; set; }
        public string Title { get; set; } = "";
        public string FreeText { get; set; } = "";
    }

    /// <summary>PERSON 系トークン行（INotifyPropertyChanged で AliasDisplay 等の変更を即時反映）。</summary>
    private sealed class PersonTokenRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChange(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string? _sep;
        public string? PrecedingSeparator
        {
            get => _sep;
            set { _sep = value; OnChange(nameof(PrecedingSeparator)); }
        }

        public string RawText { get; set; } = "";

        public int? AliasId { get; set; }

        private string? _aliasDisplay;
        public string? AliasDisplay
        {
            get => _aliasDisplay;
            set { _aliasDisplay = value; OnChange(nameof(AliasDisplay)); }
        }

        private string _glyph = "";
        public string StatusGlyph
        {
            get => _glyph;
            set { _glyph = value; OnChange(nameof(StatusGlyph)); }
        }
    }

    /// <summary>歌唱者トークン行。CV パターンに合わせて 3 系統の alias_id を保持する。</summary>
    private sealed class VocalsTokenRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChange(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string? _sep;
        public string? PrecedingSeparator
        {
            get => _sep;
            set { _sep = value; OnChange(nameof(PrecedingSeparator)); }
        }

        public string RawText { get; set; } = "";

        private string _kind = "PERSON";
        public string BillingKindStr
        {
            get => _kind;
            set
            {
                if (_kind == value) return;
                _kind = value;
                OnChange(nameof(BillingKindStr));
                // 種別変更で持っている alias_id をリセット（PERSON↔CHARACTER_WITH_CV で意味が変わるため）。
                PersonAliasId = null;
                CharacterAliasId = null;
                VoicePersonAliasId = null;
                SlashPersonAliasId = null;
                SlashCharacterAliasId = null;
                MainDisplay = "";
                SlashDisplay = "";
                VoiceDisplay = "";
            }
        }

        public int? PersonAliasId { get; set; }
        public int? CharacterAliasId { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public int? SlashCharacterAliasId { get; set; }

        private string _main = "";
        public string MainDisplay
        {
            get => _main;
            set { _main = value; OnChange(nameof(MainDisplay)); }
        }

        private string _slash = "";
        public string SlashDisplay
        {
            get => _slash;
            set { _slash = value; OnChange(nameof(SlashDisplay)); }
        }

        private string _voice = "";
        public string VoiceDisplay
        {
            get => _voice;
            set { _voice = value; OnChange(nameof(VoiceDisplay)); }
        }

        private string _glyph = "";
        public string StatusGlyph
        {
            get => _glyph;
            set { _glyph = value; OnChange(nameof(StatusGlyph)); }
        }
    }
}
