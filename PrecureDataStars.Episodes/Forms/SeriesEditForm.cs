using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using System.Linq;

namespace PrecureDataStars.Episodes.Forms;

/// <summary>
/// シリーズ（TV シリーズ・劇場版・OVA 等）の CRUD 編集フォーム。
/// <para>
/// 左ペインのリストで対象シリーズを選択し、右ペインで各フィールドを編集して保存する。
/// 親シリーズ・関係種別・種別コードは DB マスタから動的に構築した ComboBox で選択する。
/// </para>
/// </summary>
public partial class SeriesEditorForm : Form
{
    /// <summary>監査列 (created_by / updated_by) に記録するユーザー識別子。</summary>
    private const string AuditUser = "series_editor";
    private readonly SeriesRepository _seriesRepo;

    // ルックアップ用
    private readonly SeriesKindsRepository _kindsRepo;
    private readonly SeriesRelationKindsRepository _relKindsRepo;
    private IReadOnlyList<SeriesKind> _kinds = Array.Empty<SeriesKind>();
    private IReadOnlyList<SeriesRelationKind> _relKinds = Array.Empty<SeriesRelationKind>();

    private List<Series> _series = new();         // 画面上の全シリーズ（is_deleted=0）
    private Series? _current;                     // 現在編集中のシリーズ

    /// <summary>親シリーズ ComboBox 用の選択肢モデル（ID + 表示テキスト）。</summary>
    private sealed class ParentOption
    {
        public int? Id { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>relation_to_parent ComboBox 用の汎用選択肢モデル。</summary>
    private sealed class ComboOption<T>
    {
        public T? Value { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>
    /// <see cref="SeriesEditorForm"/> の新しいインスタンスを生成する。
    /// 初期化時にマスタのロードとシリーズ一覧の取得を非同期で開始する。
    /// </summary>
    /// <param name="seriesRepo">シリーズリポジトリ。</param>
    /// <param name="kindsRepo">シリーズ種別マスタリポジトリ。</param>
    /// <param name="relKindsRepo">シリーズ関係種別マスタリポジトリ。</param>
    public SeriesEditorForm(
        SeriesRepository seriesRepo,
        SeriesKindsRepository kindsRepo,
        SeriesRelationKindsRepository relKindsRepo)
    {
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _kindsRepo = kindsRepo ?? throw new ArgumentNullException(nameof(kindsRepo));
        _relKindsRepo = relKindsRepo ?? throw new ArgumentNullException(nameof(relKindsRepo));

        InitializeComponent();

        // events
        lstSeries.SelectedIndexChanged += (_, __) => BindSelected();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnAdd.Click += async (_, __) => await AddAsync();

        // 終了日 DTP の “NULL風表示” 制御（チェックON/OFFや日付変更で発火）
        dtEnd.ValueChanged += (_, __) => ApplyNullableFormat(dtEnd);

        // 初期ロード
        _ = LoadAsync();
    }

    /// <summary>初期ロード: ルックアップ（種別・関係種別）取得 → シリーズ一覧取得 → リストバインド。</summary>
    private async Task LoadAsync()
    {
        // ルックアップ取得
        _kinds = await _kindsRepo.GetAllAsync();
        _relKinds = await _relKindsRepo.GetAllAsync();

        // テーブル由来でコンボ構築
        PopulateCombosFromLookups();

        // DBから全シリーズを取得
        _series = (await _seriesRepo.GetAllAsync()).ToList();
        BindList();
    }

    /// <summary>シリーズ一覧を ListBox にバインドする（Title を表示、SeriesId を値に設定）。</summary>
    private void BindList()
    {
        lstSeries.DataSource = null;
        lstSeries.DataSource = _series;
        lstSeries.DisplayMember = nameof(Series.Title);
        lstSeries.ValueMember = nameof(Series.SeriesId);
        if (_series.Count > 0 && lstSeries.SelectedIndex < 0)
            lstSeries.SelectedIndex = 0;
    }

    /// <summary>series_kinds / series_relation_kinds テーブルの内容でコンボボックスを構築する。</summary>
    private void PopulateCombosFromLookups()
    {
        // kind：コード文字列のみ（既存ロジックが string を期待）
        cmbKind.Items.Clear();
        foreach (var k in _kinds)
            cmbKind.Items.Add(k.KindCode);

        // relation：先頭に（なし）
        var relOptions = new List<ComboOption<string?>> { new() { Value = null, Text = "（なし）" } };
        relOptions.AddRange(_relKinds.Select(r => new ComboOption<string?> { Value = r.RelationCode, Text = $"{r.RelationCode} - {r.NameJa}" }));

        cmbRelation.DisplayMember = nameof(ComboOption<string?>.Text);
        cmbRelation.ValueMember = nameof(ComboOption<string?>.Value);
        cmbRelation.DataSource = relOptions;
    }

    /// <summary>
    /// 親シリーズ ComboBox を構築する（自分自身は除外）。
    /// </summary>
    /// <param name="currentSeriesId">現在編集中のシリーズ ID（除外対象）。新規の場合は NULL。</param>
    private void PopulateParentCombo(int? currentSeriesId)
    {
        // 親シリーズは「自分自身は選べない」。
        // 先頭に（なし）を追加。
        var options = new List<ParentOption>
        {
            new()
            {
                Id = null,
                Text = "（なし）"
            }
        };
        foreach (var s in _series)
        {
            if (currentSeriesId.HasValue && s.SeriesId == currentSeriesId.Value) continue;
            options.Add(new ParentOption { Id = s.SeriesId, Text = $"[{s.SeriesId}] {s.Title}" });
        }

        cmbParent.DisplayMember = nameof(ParentOption.Text);
        cmbParent.ValueMember = nameof(ParentOption.Id);
        cmbParent.DataSource = options;
    }

    /// <summary>
    /// Nullable な DateTimePicker の表示制御。Checked=false なら CustomFormat を空白にし、
    /// 視覚的に「未設定」を表現する。
    /// </summary>
    /// <param name="dtp">対象の <see cref="DateTimePicker"/>（ShowCheckBox = true 前提）。</param>
    private static void ApplyNullableFormat(DateTimePicker dtp)
    {
        if (!dtp.ShowCheckBox) return;
        if (dtp.Checked)
        {
            if (string.IsNullOrEmpty(dtp.CustomFormat) || dtp.CustomFormat == " ")
                dtp.CustomFormat = "yyyy-MM-dd";
        }
        else
        {
            dtp.CustomFormat = " ";
        }
    }

    /// <summary>リストで選択されたシリーズの全フィールドを右ペインの各コントロールにバインドする。</summary>
    private void BindSelected()
    {
        _current = lstSeries.SelectedItem as Series;
        if (_current is null)
        {
            // 空クリア
            txtTitle.Text = "";
            txtSlug.Text = "";
            cmbKind.SelectedIndex = -1;
            cmbParent.DataSource = null;
            cmbRelation.SelectedIndex = 0; // （なし）
            numSeqInParent.Value = 0;
            dtStart.Value = DateTime.Today;
            dtEnd.Checked = false;
            dtEnd.Value = DateTime.Today;
            ApplyNullableFormat(dtEnd);
            numEpisodes.Value = 0;
            numRunTimeSeconds.Value = 0;
            txtTitleKana.Text = "";
            txtTitleShort.Text = "";
            txtTitleShortKana.Text = "";
            txtTitleEn.Text = "";
            txtTitleShortEn.Text = "";
            txtToeiSite.Text = "";
            txtToeiLineup.Text = "";
            txtAbcSite.Text = "";
            txtAmazonPrime.Text = "";
            return;
        }

        // 親シリーズ ComboBox を再構築（自分自身を除外してから選択値をセット）
        PopulateParentCombo(_current.SeriesId);

        // 値のバインド
        txtTitle.Text = _current.Title;
        txtSlug.Text = _current.Slug;

        // kind_code
        var kindIndex = cmbKind.Items.IndexOf(_current.KindCode);
        cmbKind.SelectedIndex = kindIndex >= 0 ? kindIndex : -1;

        // parent_series_id
        if (_current.ParentSeriesId is { } pid)
        {
            var hasPid = (cmbParent.DataSource as IEnumerable<ParentOption>)?
                         .Any(o => o.Id == pid) == true;
            if (hasPid) cmbParent.SelectedValue = (int?)pid;
            else if (cmbParent.Items.Count > 0) cmbParent.SelectedIndex = 0;
        }
        else
        {
            if (cmbParent.Items.Count > 0) cmbParent.SelectedIndex = 0; // （なし）
        }

        // relation_to_parent（DataSource 利用）
        cmbRelation.SelectedValue = _current.RelationToParent ?? (object)DBNull.Value;

        // seq_in_parent
        numSeqInParent.Value = _current.SeqInParent.HasValue ? _current.SeqInParent.Value : 0;

        // dates
        dtStart.Value = _current.StartDate.ToDateTime(TimeOnly.MinValue);
        if (_current.EndDate is { } ed)
        {
            dtEnd.Checked = true;
            dtEnd.CustomFormat = "yyyy-MM-dd";
            dtEnd.Value = ed.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            dtEnd.Checked = false;
            dtEnd.Value = DateTime.Today; // 値はダミー
            dtEnd.CustomFormat = " ";
        }
        ApplyNullableFormat(dtEnd);

        // 数値
        numEpisodes.Value = _current.Episodes.HasValue ? _current.Episodes.Value : 0;
        numRunTimeSeconds.Value = _current.RunTimeSeconds.HasValue ? _current.RunTimeSeconds.Value : 0;

        // 文字列群
        txtTitleKana.Text = _current.TitleKana ?? "";
        txtTitleShort.Text = _current.TitleShort ?? "";
        txtTitleShortKana.Text = _current.TitleShortKana ?? "";
        txtTitleEn.Text = _current.TitleEn ?? "";
        txtTitleShortEn.Text = _current.TitleShortEn ?? "";
        txtToeiSite.Text = _current.ToeiAnimOfficialSiteUrl ?? "";
        txtToeiLineup.Text = _current.ToeiAnimLineupUrl ?? "";
        txtAbcSite.Text = _current.AbcOfficialSiteUrl ?? "";
        txtAmazonPrime.Text = _current.AmazonPrimeDistributionUrl ?? "";
    }

    /// <summary>右ペインの編集内容を Series モデルに反映し、DB へ UPDATE する。</summary>
    private async Task SaveAsync()
    {
        if (_current is null) return;

        // UI コントロールの値 → Series モデルへ反映（空文字は NULL 扱い）
        _current.Title = NormalizeWaveDash(txtTitle.Text.Trim());
        _current.Slug = txtSlug.Text.Trim();

        // kind_code
        _current.KindCode = (cmbKind.SelectedItem as string) ?? "TV";

        // parent_series_id（自分自身は選ばせていない前提だが、念のため防御）
        var parentOption = cmbParent.SelectedItem as ParentOption;
        var selectedParentId = parentOption?.Id;
        if (selectedParentId.HasValue && selectedParentId.Value == _current.SeriesId)
        {
            MessageBox.Show("自分自身を親シリーズには指定できません。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _current.ParentSeriesId = selectedParentId;

        // relation_to_parent と seq_in_parent（DataSource）
        var rel = cmbRelation.SelectedValue as string;
        _current.RelationToParent = string.IsNullOrWhiteSpace(rel) ? null : rel;
        _current.SeqInParent = (byte?)(numSeqInParent.Value == 0 ? null : (byte)numSeqInParent.Value);

        // 日付
        _current.StartDate = DateOnly.FromDateTime(dtStart.Value.Date);
        _current.EndDate = dtEnd.Checked ? DateOnly.FromDateTime(dtEnd.Value.Date) : null;

        // 数値
        _current.Episodes = numEpisodes.Value == 0 ? null : (ushort?)numEpisodes.Value;
        _current.RunTimeSeconds = numRunTimeSeconds.Value == 0 ? null : (ushort?)numRunTimeSeconds.Value;

        // 文字列群（空文字は NULL 扱い）
        _current.TitleKana = string.IsNullOrWhiteSpace(txtTitleKana.Text) ? null : txtTitleKana.Text.Trim();
        _current.TitleShort = string.IsNullOrWhiteSpace(txtTitleShort.Text) ? null : txtTitleShort.Text.Trim();
        _current.TitleShortKana = string.IsNullOrWhiteSpace(txtTitleShortKana.Text) ? null : txtTitleShortKana.Text.Trim();
        _current.TitleEn = string.IsNullOrWhiteSpace(txtTitleEn.Text) ? null : txtTitleEn.Text.Trim();
        _current.TitleShortEn = string.IsNullOrWhiteSpace(txtTitleShortEn.Text) ? null : txtTitleShortEn.Text.Trim();
        _current.ToeiAnimOfficialSiteUrl = string.IsNullOrWhiteSpace(txtToeiSite.Text) ? null : txtToeiSite.Text.Trim();
        _current.ToeiAnimLineupUrl = string.IsNullOrWhiteSpace(txtToeiLineup.Text) ? null : txtToeiLineup.Text.Trim();
        _current.AbcOfficialSiteUrl = string.IsNullOrWhiteSpace(txtAbcSite.Text) ? null : txtAbcSite.Text.Trim();
        _current.AmazonPrimeDistributionUrl = string.IsNullOrWhiteSpace(txtAmazonPrime.Text) ? null : txtAmazonPrime.Text.Trim();

        _current.UpdatedBy = AuditUser;

        // INSERT 前のバリデーション（スキーマの NOT NULL / CHECK 制約に対応）
        if (string.IsNullOrWhiteSpace(_current.Title))
        {
            MessageBox.Show("タイトルは必須です。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_current.Slug))
        {
            MessageBox.Show("スラッグは必須です。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 保存
        await _seriesRepo.UpdateAsync(_current);

        // リストの表示文字（タイトル）が変化する可能性があるので再バインド
        var idx = lstSeries.SelectedIndex;
        BindList();
        if (idx >= 0 && idx < lstSeries.Items.Count) lstSeries.SelectedIndex = idx;

        MessageBox.Show("保存しました", "Series", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>仮データで新規シリーズを INSERT し、リストに追加して選択状態にする。</summary>
    private async Task AddAsync()
    {
        var s = new Series
        {
            KindCode = "TV",
            Title = "(新規)",
            Slug = $"new-{DateTime.Now:yyyyMMddHHmmss}",
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedBy = AuditUser,
            UpdatedBy = AuditUser
        };

        // ひとまず Insert
        var id = await _seriesRepo.InsertAsync(s);
        s.SeriesId = id;

        // 画面リストに追加して選択
        _series.Add(s);
        BindList();
        lstSeries.SelectedItem = s;
    }

    /// <summary>
    /// 全角チルダ(～) → 全角波ダッシュ(〜)
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private static string NormalizeWaveDash(string s)
    => string.IsNullOrEmpty(s) ? s : s.Replace('\uFF5E', '\u301C');
}
