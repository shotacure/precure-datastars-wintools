#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ディスク・トラック閲覧フォーム（読み取り専用）。
/// <para>
/// 上段にディスク一覧、下段に選択ディスクのトラック一覧を表示する。
/// 編集機能は持たず、表示値はすべて翻訳済み（種別コードではなく日本語名、
/// シリーズ ID ではなくシリーズ名、など）。
/// </para>
/// <para>
/// 尺は M:SS.mmm 形式（ミリ秒まで）。CD-DA は length_frames (1/75 秒) から算出、
/// 非 CD は length_seconds のみあるため .000 固定で表示する。
/// </para>
/// </summary>
public partial class DiscBrowserForm : Form
{
    private readonly DiscsRepository _discsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly SeriesRepository _seriesRepo;

    // 現在保持しているディスク全件（フィルタ前）
    private List<DiscBrowserRow> _allDiscs = new();

    // バインド用のソースリスト（フィルタ後）
    private readonly BindingSource _bindingDiscs = new();
    private readonly BindingSource _bindingTracks = new();

    /// <summary>
    /// <see cref="DiscBrowserForm"/> の新しいインスタンスを生成する。
    /// </summary>
    public DiscBrowserForm(
        DiscsRepository discsRepo,
        TracksRepository tracksRepo,
        SeriesRepository seriesRepo)
    {
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();
        SetupGridColumns();

        gridDiscs.DataSource = _bindingDiscs;
        gridTracks.DataSource = _bindingTracks;

        // イベント結線
        Load += async (_, __) => await InitialLoadAsync();
        btnReload.Click += async (_, __) => await ReloadAsync();
        txtSearch.TextChanged += (_, __) => ApplyFilter();
        cboSeries.SelectedIndexChanged += (_, __) => ApplyFilter();
        gridDiscs.SelectionChanged += async (_, __) => await OnDiscSelectionChangedAsync();
    }

    // =========================================================================
    // 列定義
    // =========================================================================

    /// <summary>グリッドの列を AutoGenerate に頼らず明示定義する（順序・幅・表示名を固定するため）。</summary>
    private void SetupGridColumns()
    {
        // ── ディスクグリッド ──
        gridDiscs.Columns.Clear();
        gridDiscs.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.CatalogNo),         HeaderText = "品番",       Width = 110 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.DisplayTitle),      HeaderText = "タイトル",   Width = 360, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.SeriesName),        HeaderText = "シリーズ",   Width = 140 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.ProductKindName),   HeaderText = "商品種別",   Width = 100 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.MediaFormat),       HeaderText = "メディア",   Width = 70  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.ReleaseDate),       HeaderText = "発売日",     Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.DiscNoInSet),       HeaderText = "組中",       Width = 50  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.DiscCount),         HeaderText = "枚数",       Width = 50  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.TotalTracks),       HeaderText = "曲数",       Width = 50  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.Mcn),               HeaderText = "MCN",        Width = 110 },
        });

        // ── トラックグリッド ──
        // 尺列は内部プロパティ LengthFrames / LengthSecondsFallback を計算した結果 (LengthDisplay) を表示するため、
        // 計算結果を表示用プロパティにキャッシュする別 DTO に差し替えずに、CellFormatting で動的に整形する。
        gridTracks.Columns.Clear();
        gridTracks.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.TrackNo),          HeaderText = "#",          Width = 40, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.ContentKindName),  HeaderText = "種別",       Width = 70  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.DisplayTitle),     HeaderText = "タイトル",   Width = 320, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Artist),           HeaderText = "アーティスト", Width = 200 },
            // 尺は length_frames ベースで計算するため DataPropertyName にはダミーを入れておき、CellFormatting で上書きする
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.LengthFrames),     HeaderText = "尺",         Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Isrc),             HeaderText = "ISRC",       Width = 110 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Notes),            HeaderText = "備考",       Width = 200 },
        });

        // 尺列の表示整形
        gridTracks.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = gridTracks.Columns[e.ColumnIndex];
            if (col.DataPropertyName != nameof(TrackBrowserRow.LengthFrames)) return;
            if (gridTracks.Rows[e.RowIndex].DataBoundItem is not TrackBrowserRow row) return;

            e.Value = FormatLength(row);
            e.FormattingApplied = true;
        };
    }

    // =========================================================================
    // ロード処理
    // =========================================================================

    /// <summary>初回表示時の読み込み。シリーズドロップダウンとディスク一覧を両方並行取得する。</summary>
    private async Task InitialLoadAsync()
    {
        try
        {
            await PopulateSeriesFilterAsync();
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>シリーズ絞り込み用ドロップダウンを構築する。</summary>
    private async Task PopulateSeriesFilterAsync()
    {
        var allSeries = await _seriesRepo.GetAllAsync();

        cboSeries.DisplayMember = nameof(SeriesFilterItem.Display);
        cboSeries.ValueMember   = nameof(SeriesFilterItem.Value);

        var items = new List<SeriesFilterItem>
        {
            new("(全シリーズ)", null),
            new("(オールスターズ)", -1),
        };
        foreach (var s in allSeries.OrderBy(x => x.StartDate))
        {
            string name = !string.IsNullOrWhiteSpace(s.TitleShort) ? s.TitleShort! : s.Title;
            items.Add(new SeriesFilterItem($"{s.StartDate:yyyy-MM}  {name}", s.SeriesId));
        }
        cboSeries.DataSource = items;
        cboSeries.SelectedIndex = 0;
    }

    /// <summary>ディスク一覧を DB から再取得し、現在のフィルタ条件で絞り込んで反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var rows = await _discsRepo.GetBrowserListAsync();
            _allDiscs = rows.ToList();
            ApplyFilter();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // フィルタと選択
    // =========================================================================

    /// <summary>検索キーワードとシリーズドロップダウンの状態に合わせて一覧を絞り込む。</summary>
    private void ApplyFilter()
    {
        string kw = (txtSearch.Text ?? "").Trim();
        int? seriesFilter = ExtractSeriesFilter();

        IEnumerable<DiscBrowserRow> q = _allDiscs;

        // シリーズフィルタ: null=全件、-1=オールスターズ（SeriesId IS NULL）、それ以外=一致
        if (seriesFilter == -1)
        {
            q = q.Where(r => r.SeriesId is null);
        }
        else if (seriesFilter.HasValue)
        {
            int sid = seriesFilter.Value;
            q = q.Where(r => r.SeriesId == sid);
        }

        // キーワード: 品番・タイトル・シリーズ名に対する部分一致
        if (kw.Length > 0)
        {
            q = q.Where(r =>
                (r.CatalogNo?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (r.DisplayTitle?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (r.SeriesName?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        var list = q.ToList();
        _bindingDiscs.DataSource = list;
        lblCount.Text = $"{list.Count} 件 / 全 {_allDiscs.Count} 件";
    }

    /// <summary>シリーズドロップダウンの現在値から series_id を取り出す。</summary>
    /// <returns>
    /// null = 全シリーズ、-1 = オールスターズ限定（SeriesId IS NULL）、それ以外 = 指定 series_id。
    /// </returns>
    private int? ExtractSeriesFilter()
    {
        if (cboSeries.SelectedItem is SeriesFilterItem sfi) return sfi.Value;
        return null;
    }

    /// <summary>ディスク選択変更時: 下段のトラック一覧を読み直す。</summary>
    private async Task OnDiscSelectionChangedAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not DiscBrowserRow d)
        {
            _bindingTracks.DataSource = new List<TrackBrowserRow>();
            lblTracks.Text = "トラック一覧（ディスクを選択してください）";
            return;
        }

        try
        {
            var tracks = await _tracksRepo.GetBrowserListByCatalogNoAsync(d.CatalogNo);
            _bindingTracks.DataSource = tracks.ToList();
            lblTracks.Text = $"トラック一覧 - {d.CatalogNo} {d.DisplayTitle} （{tracks.Count} トラック）";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // 尺フォーマット
    // =========================================================================

    /// <summary>
    /// トラック行を M:SS.mmm 形式の尺文字列に整形する。
    /// <list type="bullet">
    ///   <item>length_frames あり（CD-DA）: フレームから秒+ミリ秒を算出（1 フレーム = 1/75 秒 ≒ 13.333ms）</item>
    ///   <item>length_frames なし、length_seconds あり: 秒のみ。ミリ秒は .000 固定</item>
    ///   <item>どちらも無し: "—"</item>
    /// </list>
    /// </summary>
    private static string FormatLength(TrackBrowserRow row)
    {
        // CD-DA フレーム優先（ミリ秒まで正確に出る）
        if (row.LengthFrames.HasValue)
        {
            uint frames = row.LengthFrames.Value;
            int totalSeconds = (int)(frames / 75);
            int subFrames = (int)(frames % 75);
            // 1 フレーム = 1000/75 ミリ秒 ≒ 13.3333...
            int millis = (int)Math.Round(subFrames * 1000.0 / 75.0);
            if (millis == 1000) { totalSeconds++; millis = 0; }
            int m = totalSeconds / 60;
            int s = totalSeconds % 60;
            return string.Create(CultureInfo.InvariantCulture, $"{m}:{s:D2}.{millis:D3}");
        }

        // 歌・劇伴に格納された秒数にフォールバック（ミリ秒情報なし）
        if (row.LengthSecondsFallback.HasValue)
        {
            int total = row.LengthSecondsFallback.Value;
            int m = total / 60;
            int s = total % 60;
            return string.Create(CultureInfo.InvariantCulture, $"{m}:{s:D2}.000");
        }

        return "—";
    }

    // =========================================================================
    // エラー表示
    // =========================================================================

    private void ShowError(Exception ex)
    {
        MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

/// <summary>
/// シリーズ絞り込みドロップダウンの 1 項目。
/// <para>
/// <see cref="Value"/> の意味:
/// <list type="bullet">
///   <item>null = 全シリーズ（絞り込みなし）</item>
///   <item>-1 = オールスターズ限定（SeriesId IS NULL のディスクのみ）</item>
///   <item>それ以外 = 指定シリーズ ID 一致</item>
/// </list>
/// </para>
/// </summary>
internal sealed record SeriesFilterItem(string Display, int? Value);
