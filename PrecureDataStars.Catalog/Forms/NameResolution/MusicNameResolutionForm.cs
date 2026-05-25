using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
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
/// 未登録の曲・録音を <b>4 役職（作詞 / 作曲 / 編曲 / 歌唱者）横断のフラットな 1 リスト</b>として
/// 表示し、各行を選択すると右ペインで token 編集 UI が役職に応じて切り替わる。
/// マスタへの自動 INSERT は一切行わず、未マッチ語は手動 Picker を促す。
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

    // 左：4 役職横断の未解決一覧。
    private DataGridView _gridList = null!;
    private readonly BindingList<UnresolvedItem> _items = new();

    // 右：選択行のフリーテキスト表示と「再分解」ボタン。
    private Label _lblFreeText = null!;
    private Button _btnReParse = null!;

    // 右：役職に応じて Visible を切り替える 2 種類の編集パネル。
    private Panel _personPanel = null!;
    private DataGridView _gridPersonTokens = null!;
    private Button _btnApplyPerson = null!;
    private readonly BindingList<PersonTokenRow> _personTokens = new();

    private Panel _vocalsPanel = null!;
    private DataGridView _gridVocalsTokens = null!;
    private Button _btnApplyVocals = null!;
    private readonly BindingList<VocalsTokenRow> _vocalsTokens = new();

    // 「選択ハンドラの非同期競合（初期描画で SelectionChanged が複数回連発した結果、
    // 古いハンドラが同じ token を 2 回・3 回と重複追加してしまう）」を防ぐ世代カウンタ。
    // ハンドラ先頭で Interlocked.Increment し、書き戻し直前に最新世代と一致するか確認する。
    private int _selectionGen;

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
        BuildLayout();

        Load += async (_, __) => await ReloadAsync();
    }

    /// <summary>左の未解決一覧と右の token 編集パネル 2 種を組み立てる。</summary>
    private void BuildLayout()
    {
        // ── 左：未解決一覧（4 役職横断） ──
        _gridList = new DataGridView
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
        _gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "役職", DataPropertyName = nameof(UnresolvedItem.RoleLabel), Width = 70 });
        _gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", DataPropertyName = nameof(UnresolvedItem.SourceId), Width = 60 });
        _gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "曲タイトル", DataPropertyName = nameof(UnresolvedItem.Title), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridList.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "フリーテキスト", DataPropertyName = nameof(UnresolvedItem.FreeText), Width = 240 });
        split.Panel1.Controls.Add(_gridList);

        // ── 右：選択行ごとの編集 UI を縦に積む（フリーテキスト表示 / token 編集 / 操作行）。 ──
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

        // 上段：フリーテキストのプレビュー。
        _lblFreeText = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "（左の一覧から行を選択するとフリーテキストが表示されます）",
            ForeColor = SystemColors.GrayText
        };
        rightLayout.Controls.Add(_lblFreeText, 0, 0);

        // 中段：役職別の 2 種の token 編集パネルを縦に重ね、選択行の役職で Visible を切り替える。
        var stack = new Panel { Dock = DockStyle.Fill };
        BuildPersonPanel(stack);
        BuildVocalsPanel(stack);
        rightLayout.Controls.Add(stack, 0, 1);

        // 下段：「再分解」のみ共通配置。Apply ボタンは各パネル内に持つ（役職別の登録先 SP が異なるため）。
        _btnReParse = new Button { Text = "再分解", Dock = DockStyle.Left, Width = 100 };
        _btnReParse.Click += async (_, __) => await OnSelectionChangedAsync();
        var btnPanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        btnPanel.Controls.Add(_btnReParse);
        rightLayout.Controls.Add(btnPanel, 0, 2);

        // 左一覧の選択変更でハンドラを発火（race 抑止は OnSelectionChangedAsync 側の世代カウンタで担保）。
        _gridList.SelectionChanged += async (_, __) => await OnSelectionChangedAsync();
    }

    /// <summary>右ペインの「PERSON 系（作詞 / 作曲 / 編曲）」用 token 編集パネルを stack に積む。</summary>
    private void BuildPersonPanel(Panel stack)
    {
        _personPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        stack.Controls.Add(_personPanel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _personPanel.Controls.Add(layout);

        _gridPersonTokens = new DataGridView
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

        var colSep = new DataGridViewComboBoxColumn
        {
            HeaderText = "区切り",
            DataPropertyName = nameof(PersonTokenRow.PrecedingSeparator),
            Width = 80,
            FlatStyle = FlatStyle.Flat
        };
        foreach (var s in SeparatorChoices) colSep.Items.Add(s);
        _gridPersonTokens.Columns.Add(colSep);

        _gridPersonTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "テキスト",
            DataPropertyName = nameof(PersonTokenRow.RawText),
            ReadOnly = true,
            Width = 180
        });
        _gridPersonTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名義（解決後）",
            DataPropertyName = nameof(PersonTokenRow.AliasDisplay),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _gridPersonTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "選択",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 50
        });
        _gridPersonTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状態",
            DataPropertyName = nameof(PersonTokenRow.StatusGlyph),
            ReadOnly = true,
            Width = 50
        });
        layout.Controls.Add(_gridPersonTokens, 0, 0);

        _btnApplyPerson = new Button { Text = "この内容で登録", Dock = DockStyle.Right, Width = 200, Enabled = false };
        var apPanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        apPanel.Controls.Add(_btnApplyPerson);
        layout.Controls.Add(apPanel, 0, 1);

        _gridPersonTokens.CellClick += async (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_gridPersonTokens.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
                await OnPickPersonAsync(e.RowIndex);
        };
        _gridPersonTokens.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            RefreshApplyButtons();
        };
        _gridPersonTokens.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (_gridPersonTokens.IsCurrentCellDirty) _gridPersonTokens.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _btnApplyPerson.Click += async (_, __) => await OnApplyPersonAsync();
    }

    /// <summary>右ペインの「歌唱者」用 token 編集パネルを stack に積む（PERSON 系とは CV/スラッシュ列の有無で構造が違う）。</summary>
    private void BuildVocalsPanel(Panel stack)
    {
        _vocalsPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        stack.Controls.Add(_vocalsPanel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _vocalsPanel.Controls.Add(layout);

        _gridVocalsTokens = new DataGridView
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

        var colSep = new DataGridViewComboBoxColumn
        {
            HeaderText = "区切り",
            DataPropertyName = nameof(VocalsTokenRow.PrecedingSeparator),
            Width = 70,
            FlatStyle = FlatStyle.Flat
        };
        foreach (var s in SeparatorChoices) colSep.Items.Add(s);
        _gridVocalsTokens.Columns.Add(colSep);

        _gridVocalsTokens.Columns.Add(new DataGridViewTextBoxColumn
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
        _gridVocalsTokens.Columns.Add(colKind);

        _gridVocalsTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "主名義（人物 or キャラ）",
            DataPropertyName = nameof(VocalsTokenRow.MainDisplay),
            ReadOnly = true,
            Width = 180
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "主",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "スラッシュ相方",
            DataPropertyName = nameof(VocalsTokenRow.SlashDisplay),
            ReadOnly = true,
            Width = 160
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "/",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "CV（人物名義）",
            DataPropertyName = nameof(VocalsTokenRow.VoiceDisplay),
            ReadOnly = true,
            Width = 160
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "CV",
            Text = "...",
            UseColumnTextForButtonValue = true,
            Width = 40
        });
        _gridVocalsTokens.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "状態",
            DataPropertyName = nameof(VocalsTokenRow.StatusGlyph),
            ReadOnly = true,
            Width = 50
        });
        layout.Controls.Add(_gridVocalsTokens, 0, 0);

        _btnApplyVocals = new Button { Text = "この内容で登録", Dock = DockStyle.Right, Width = 200, Enabled = false };
        var avPanel = new Panel { Dock = DockStyle.Fill, Height = 36 };
        avPanel.Controls.Add(_btnApplyVocals);
        layout.Controls.Add(avPanel, 0, 1);

        _gridVocalsTokens.CellClick += async (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var col = _gridVocalsTokens.Columns[e.ColumnIndex];
            if (col is DataGridViewButtonColumn)
            {
                string header = col.HeaderText;
                if (header == "主") await OnPickVocalsMainAsync(e.RowIndex);
                else if (header == "/") await OnPickVocalsSlashAsync(e.RowIndex);
                else if (header == "CV") await OnPickVocalsCvAsync(e.RowIndex);
            }
        };
        _gridVocalsTokens.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (_gridVocalsTokens.IsCurrentCellDirty) _gridVocalsTokens.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _gridVocalsTokens.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            RefreshApplyButtons();
        };
        _btnApplyVocals.Click += async (_, __) => await OnApplyVocalsAsync();
    }

    // ───────────────────── 未解決一覧の一括ロード ─────────────────────

    /// <summary>
    /// 4 役職（LYRICS / COMPOSITION / ARRANGEMENT / VOCALS）横断で未解決行を 1 度に集めて
    /// フラットな <see cref="_items"/> に詰める。並び順は (役職表示順, ID 昇順)。
    /// </summary>
    private async Task ReloadAsync()
    {
        try
        {
            _items.Clear();

            // PERSON 系 3 役職：songs テーブルのフリーテキスト列ごとに走査。
            // 役職表示順は「作詞 → 作曲 → 編曲」固定。
            var personRoles = new (string Code, string Label, string Col)[]
            {
                (SongCreditRoles.Lyrics,      "作詞", "lyricist_name"),
                (SongCreditRoles.Composition, "作曲", "composer_name"),
                (SongCreditRoles.Arrangement, "編曲", "arranger_name")
            };
            await using var conn = await _factory.CreateOpenedAsync();
            foreach (var (code, label, col) in personRoles)
            {
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
                var rows = await conn.QueryAsync<(int SongId, string Title, string FreeText)>(
                    new CommandDefinition(sql, new { role = code }));
                foreach (var r in rows)
                {
                    _items.Add(new UnresolvedItem
                    {
                        Kind = ItemKind.PersonRole,
                        RoleCode = code,
                        RoleLabel = label,
                        SourceId = r.SongId,
                        Title = r.Title ?? "",
                        FreeText = r.FreeText ?? ""
                    });
                }
            }

            // VOCALS：song_recordings テーブルの singer_name を走査。
            const string vocalsSql = """
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
            var vrows = await conn.QueryAsync<(int SongRecordingId, int SongId, string Title, string FreeText)>(
                new CommandDefinition(vocalsSql, new { role = SongRecordingSingerRoles.Vocals }));
            foreach (var r in vrows)
            {
                _items.Add(new UnresolvedItem
                {
                    Kind = ItemKind.Vocals,
                    RoleCode = SongRecordingSingerRoles.Vocals,
                    RoleLabel = "歌唱者",
                    SourceId = r.SongRecordingId,
                    Title = r.Title ?? "",
                    FreeText = r.FreeText ?? ""
                });
            }

            // DataSource 再バインド（race を避けるため SelectionChanged が初回発火する前に世代を進めておく）。
            Interlocked.Increment(ref _selectionGen);
            _gridList.DataSource = null;
            _gridList.DataSource = _items;
            _gridList.ClearSelection();

            ResetRightPanes();
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>未選択状態の右ペイン表示に戻す（フリーテキストヒント + 両パネル非表示）。</summary>
    private void ResetRightPanes()
    {
        _personPanel.Visible = false;
        _vocalsPanel.Visible = false;
        _lblFreeText.Text = "（左の一覧から行を選択するとフリーテキストが表示されます）";
        _lblFreeText.ForeColor = SystemColors.GrayText;
        _personTokens.Clear();
        _vocalsTokens.Clear();
        _btnApplyPerson.Enabled = false;
        _btnApplyVocals.Enabled = false;
    }

    /// <summary>ステータスバーの残件数を再計算して表示する。</summary>
    private void UpdateStatus()
    {
        int total = _items.Count;
        int lyrics = _items.Count(i => i.RoleCode == SongCreditRoles.Lyrics);
        int comp = _items.Count(i => i.RoleCode == SongCreditRoles.Composition);
        int arr = _items.Count(i => i.RoleCode == SongCreditRoles.Arrangement);
        int voc = _items.Count(i => i.RoleCode == SongRecordingSingerRoles.Vocals);
        lblStatus.Text = $"未解決件数 合計: {total} 件（作詞: {lyrics} / 作曲: {comp} / 編曲: {arr} / 歌唱者: {voc}）";
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ───────────────────── 左一覧の選択変更ハンドラ ─────────────────────

    /// <summary>
    /// 左一覧の選択行に応じて、右ペインの該当パネルを表示しトークン分解 + 候補マッチを行う。
    /// 初期データバインド時など短時間に複数回連発する SelectionChanged の async 競合は、
    /// 世代カウンタ <see cref="_selectionGen"/> で「最後の呼び出しだけ書き戻す」方式で抑止する
    /// （古い世代のハンドラが await から戻った時点で別の選択に切り替わっていたら何もせず終了）。
    /// </summary>
    private async Task OnSelectionChangedAsync()
    {
        int myGen = Interlocked.Increment(ref _selectionGen);

        UnresolvedItem? item = _gridList.CurrentRow?.DataBoundItem as UnresolvedItem;
        if (item is null)
        {
            ResetRightPanes();
            return;
        }

        _lblFreeText.Text = $"フリーテキスト: {item.FreeText}";
        _lblFreeText.ForeColor = SystemColors.ControlText;

        // 役職別にパネルを切り替えてトークン解析。
        if (item.Kind == ItemKind.PersonRole)
        {
            _personPanel.Visible = true;
            _vocalsPanel.Visible = false;
            await LoadPersonTokensAsync(item, myGen).ConfigureAwait(true);
        }
        else
        {
            _personPanel.Visible = false;
            _vocalsPanel.Visible = true;
            await LoadVocalsTokensAsync(item, myGen).ConfigureAwait(true);
        }
    }

    // ───────────────────── PERSON 系（作詞 / 作曲 / 編曲） ─────────────────────

    /// <summary>選択された PERSON 系項目のフリーテキストを分解して、候補マッチ結果と共にトークン行を組み立てる。</summary>
    private async Task LoadPersonTokensAsync(UnresolvedItem item, int myGen)
    {
        var tokens = MusicNameTokenizer.Tokenize(item.FreeText);
        var resolved = new List<PersonTokenRow>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            // CV パターンが検出された場合でも、PERSON 系では MainPart を「テキスト」として扱う。
            string text = tok.MainPart ?? tok.RawText;
            var candidates = await _personAliasesRepo.FindByExactNameAsync(text);
            var row = new PersonTokenRow
            {
                PrecedingSeparator = i == 0 ? null : (tok.PrecedingSeparator ?? "、"),
                RawText = text,
                AliasId = candidates.Count == 1 ? candidates[0].AliasId : (int?)null,
                AliasDisplay = candidates.Count == 1
                    ? await _personAliasesRepo.GetDisplayNameAsync(candidates[0].AliasId)
                    : (candidates.Count == 0 ? "(未マッチ)" : $"({candidates.Count} 候補：選択してください)")
            };
            resolved.Add(row);
        }

        // 古い世代のハンドラは書き戻さない（race 抑止）。
        if (myGen != _selectionGen) return;

        _personTokens.Clear();
        foreach (var r in resolved) _personTokens.Add(r);
        _gridPersonTokens.DataSource = null;
        _gridPersonTokens.DataSource = _personTokens;
        RefreshApplyButtons();
    }

    private async Task OnPickPersonAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _personTokens.Count) return;
        var row = _personTokens[rowIndex];

        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        row.AliasId = dlg.SelectedId.Value;
        row.AliasDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
        _gridPersonTokens.Refresh();
        RefreshApplyButtons();
    }

    private async Task OnApplyPersonAsync()
    {
        if (_gridList.CurrentRow?.DataBoundItem is not UnresolvedItem item) return;
        if (item.Kind != ItemKind.PersonRole) return;
        if (_personTokens.Count == 0 || _personTokens.Any(r => !r.AliasId.HasValue)) return;

        try
        {
            var credits = _personTokens.Select((r, i) => new SongCredit
            {
                SongId = item.SourceId,
                CreditRole = item.RoleCode,
                CreditSeq = (byte)(i + 1),
                PersonAliasId = r.AliasId!.Value,
                PrecedingSeparator = i == 0 ? null : r.PrecedingSeparator
            }).ToList();

            await _songCreditsRepo.ReplaceAllByRoleAsync(item.SourceId, item.RoleCode, credits, Environment.UserName);

            // 登録できたら未解決リストから除外。次の行への自動遷移はしない（混乱を避けるため）。
            _items.Remove(item);
            ResetRightPanes();
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ───────────────────── 歌唱者（VOCALS） ─────────────────────

    /// <summary>選択された VOCALS 項目のフリーテキストを分解。CV パターンも検出してメイン / スラッシュ / CV を別々に解決する。</summary>
    private async Task LoadVocalsTokensAsync(UnresolvedItem item, int myGen)
    {
        var tokens = MusicNameTokenizer.Tokenize(item.FreeText);
        var resolved = new List<VocalsTokenRow>(tokens.Count);
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

            resolved.Add(row);
        }

        if (myGen != _selectionGen) return;

        _vocalsTokens.Clear();
        foreach (var r in resolved) _vocalsTokens.Add(r);
        _gridVocalsTokens.DataSource = null;
        _gridVocalsTokens.DataSource = _vocalsTokens;
        RefreshApplyButtons();
    }

    private async Task OnPickVocalsMainAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _vocalsTokens.Count) return;
        var row = _vocalsTokens[rowIndex];

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

        _gridVocalsTokens.Refresh();
        RefreshApplyButtons();
    }

    private async Task OnPickVocalsSlashAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _vocalsTokens.Count) return;
        var row = _vocalsTokens[rowIndex];

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

        _gridVocalsTokens.Refresh();
        RefreshApplyButtons();
    }

    private async Task OnPickVocalsCvAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _vocalsTokens.Count) return;
        var row = _vocalsTokens[rowIndex];
        if (row.BillingKindStr != "CHARACTER_WITH_CV") return;

        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        row.VoicePersonAliasId = dlg.SelectedId.Value;
        row.VoiceDisplay = await _personAliasesRepo.GetDisplayNameAsync(dlg.SelectedId.Value);
        _gridVocalsTokens.Refresh();
        RefreshApplyButtons();
    }

    private async Task OnApplyVocalsAsync()
    {
        if (_gridList.CurrentRow?.DataBoundItem is not UnresolvedItem item) return;
        if (item.Kind != ItemKind.Vocals) return;
        if (!AreAllVocalsRowsResolved()) return;

        try
        {
            var singers = _vocalsTokens.Select((r, i) =>
            {
                var kind = r.BillingKindStr == "CHARACTER_WITH_CV"
                    ? SingerBillingKind.CharacterWithCv
                    : SingerBillingKind.Person;
                return new SongRecordingSinger
                {
                    SongRecordingId = item.SourceId,
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
                item.SourceId, SongRecordingSingerRoles.Vocals, singers, Environment.UserName);

            _items.Remove(item);
            ResetRightPanes();
            UpdateStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ───────────────────── 共通：Apply ボタン活性化と状態反映 ─────────────────────

    /// <summary>
    /// 各 token グリッドの状態列（✓/✗）を更新し、現在のパネルの Apply ボタンの活性化を再計算する。
    /// PERSON 系は AliasId が全行揃っていれば活性化、VOCALS は AreAllVocalsRowsResolved の判定。
    /// </summary>
    private void RefreshApplyButtons()
    {
        foreach (var r in _personTokens) r.StatusGlyph = r.AliasId.HasValue ? "✓" : "✗";
        foreach (var r in _vocalsTokens)
        {
            if (r.BillingKindStr == "CHARACTER_WITH_CV")
                r.StatusGlyph = (r.CharacterAliasId.HasValue && r.VoicePersonAliasId.HasValue) ? "✓" : "✗";
            else
                r.StatusGlyph = r.PersonAliasId.HasValue ? "✓" : "✗";
        }
        _gridPersonTokens.Refresh();
        _gridVocalsTokens.Refresh();

        bool personOk = _personTokens.Count > 0 && _personTokens.All(r => r.AliasId.HasValue);
        _btnApplyPerson.Enabled = personOk;
        _btnApplyPerson.BackColor = personOk ? Color.LightGreen : SystemColors.Control;

        bool vocalsOk = AreAllVocalsRowsResolved();
        _btnApplyVocals.Enabled = vocalsOk;
        _btnApplyVocals.BackColor = vocalsOk ? Color.LightGreen : SystemColors.Control;
    }

    private bool AreAllVocalsRowsResolved()
    {
        if (_vocalsTokens.Count == 0) return false;
        foreach (var r in _vocalsTokens)
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

    // ───────────────────── 内部データ構造 ─────────────────────

    /// <summary>未解決一覧の種別。役職コードと組で扱う。</summary>
    private enum ItemKind
    {
        PersonRole, // 作詞 / 作曲 / 編曲（songs テーブルの song_id × song_credits）
        Vocals      // 歌唱者（song_recordings の song_recording_id × song_recording_singers）
    }

    /// <summary>4 役職横断の未解決行 1 件。SourceId は PersonRole なら song_id、Vocals なら song_recording_id。</summary>
    private sealed class UnresolvedItem
    {
        public ItemKind Kind { get; init; }
        public string RoleCode { get; init; } = "";
        /// <summary>左一覧の「役職」列に出す日本語ラベル（作詞 / 作曲 / 編曲 / 歌唱者）。</summary>
        public string RoleLabel { get; init; } = "";
        /// <summary>song_id または song_recording_id。</summary>
        public int SourceId { get; init; }
        public string Title { get; init; } = "";
        public string FreeText { get; init; } = "";
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
