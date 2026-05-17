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

        // 上下ペイン（ディスク一覧 / トラック一覧）を常に半々で維持する。
        // splitMain は Designer 上 SplitterDistance=320 の固定値で生成されるが、
        // それだと初期高さ 700 のうち上が 320 / 下が 374 と若干偏り、しかもウインドウを
        // 縦方向に拡大した際に WinForms の SplitContainer 既定挙動では下ペイン側だけが
        // 大きく伸びて上下のバランスが崩れる。ユーザの「半々で自動的に追従してほしい」要望に
        // 合わせ、SizeChanged の都度 (高さ - スプリッタ幅) / 2 を SplitterDistance に書き戻す。
        // ユーザがバーを手動でドラッグすることは引き続き可能だが、次のリサイズで再び
        // 半々にリセットされる挙動になる（要望どおりの動作）。
        splitMain.SizeChanged += (_, __) => RecenterSplitter();
        // フォーム初期表示時にも一度合わせる（Load より前に SizeChanged が来ないケースの保険）。
        Load += (_, __) => RecenterSplitter();
    }

    /// <summary>
    /// 上下ペイン（ディスク一覧 / トラック一覧）の SplitterDistance を、
    /// 利用可能な高さ（splitMain.Height からスプリッタ自身の幅を引いた値）の半分に設定する。
    /// 高さがスプリッタ幅以下の極端なリサイズ時はクランプして例外を防ぐ。
    /// </summary>
    private void RecenterSplitter()
    {
        // 利用可能領域がスプリッタ幅以下になるケース（最小化途中など）では SplitterDistance の代入が
        // ArgumentOutOfRangeException を投げるため、最低値 1 を保証してから代入する。
        int usable = splitMain.Height - splitMain.SplitterWidth;
        if (usable < 2) return;
        int half = usable / 2;
        // SplitContainer の Min/MaxSize 制約に収まる範囲にクランプ
        int min = splitMain.Panel1MinSize;
        int max = Math.Max(min, usable - splitMain.Panel2MinSize);
        if (half < min) half = min;
        else if (half > max) half = max;
        splitMain.SplitterDistance = half;
    }

    // =========================================================================
    // 列定義
    // =========================================================================

    /// <summary>グリッドの列を AutoGenerate に頼らず明示定義する（順序・幅・表示名を固定するため）。</summary>
    private void SetupGridColumns()
    {
        // ── ディスクグリッド ──
        //   ・「組中」「枚数」は 1 カラムに統合し、2 枚組以上のときだけ "n / m" を表示（単品時は空欄）
        //   ・「曲数」→「トラック数」にリネーム（CD 以外でも使う語に合わせる）
        //   ・「総尺」カラムを新設。m:ss.fff 形式。CD は length_frames、BD/DVD は length_ms から算出
        gridDiscs.Columns.Clear();
        gridDiscs.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.CatalogNo),          HeaderText = "品番",       Width = 110 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.DisplayTitle),       HeaderText = "タイトル",   Width = 360, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.SeriesName),         HeaderText = "シリーズ",   Width = 140 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.ProductKindName),    HeaderText = "商品種別",   Width = 100 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.MediaFormat),        HeaderText = "メディア",   Width = 70  },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.ReleaseDate),        HeaderText = "発売日",     Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            // 組中／枚数統合カラム：2 枚組以上のみ "n / m"、単品は空。DiscBrowserRow 側の計算プロパティを使用。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.DiscCountDisplay),   HeaderText = "枚数",       Width = 70,  DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.TotalTracks),        HeaderText = "トラック数", Width = 75,  DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            // 総尺カラム：m:ss.fff。CD/BD/DVD を跨いで統一形式で表示。DiscBrowserRow 側の計算プロパティを使用。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(DiscBrowserRow.TotalLengthDisplay), HeaderText = "総尺",       Width = 95,  DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
        });

        // ── トラックグリッド ──
        // //   ・タイトル列の幅を縮め、残余は「備考」を Fill で確保する配分へ変更
        //   ・作詞・作曲・編曲の独立カラムを追加（劇伴は作詞が空欄）
        // 尺列は内部プロパティ LengthFrames / LengthSecondsFallback を計算した結果 (LengthDisplay) を表示するため、
        // 計算結果を表示用プロパティにキャッシュする別 DTO に差し替えずに、CellFormatting で動的に整形する。
        gridTracks.Columns.Clear();
        gridTracks.Columns.AddRange(new DataGridViewColumn[]
        {
            // # 列は集約後の文字列（"24" / "24-2" 等）。DB の TrackNo そのままではなく TrackNoDisplay にバインドする。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.TrackNoDisplay),    HeaderText = "#",          Width = 52,  DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.ContentKindName),  HeaderText = "種別",       Width = 70  },
            // タイトル列の幅は 220。作詞/作曲/編曲カラムを並べる分を考慮した幅。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.DisplayTitle),     HeaderText = "タイトル",   Width = 220 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Artist),           HeaderText = "アーティスト", Width = 180 },
            // 新設カラム。歌は songs 側、劇伴は bgm_cues 側から引いた値がそのまま入る。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Lyricist),         HeaderText = "作詞",       Width = 110 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Composer),         HeaderText = "作曲",       Width = 110 },
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Arranger),         HeaderText = "編曲",       Width = 110 },
            // 尺は length_frames ベースで計算するため DataPropertyName にはダミーを入れておき、CellFormatting で上書きする
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.LengthFrames),     HeaderText = "尺",         Width = 90,  DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            // 備考は Fill。残余を吸収する。
            new DataGridViewTextBoxColumn { DataPropertyName = nameof(TrackBrowserRow.Notes),            HeaderText = "備考",       Width = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
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
            // 閲覧 UI 全体で「正式名（series.title）優先 → 短縮名フォールバック」に統一する。
            // 編集系フォームでは短縮名を優先する設計だが、閲覧画面では情報量の多い正式名が望ましいため。
            string name = !string.IsNullOrWhiteSpace(s.Title) ? s.Title : (s.TitleShort ?? "");
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

    /// <summary>ディスク選択変更時: 下段のトラック一覧を読み直し、表示用に集約・整形して流し込む。</summary>
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
            var rawTracks = await _tracksRepo.GetBrowserListByCatalogNoAsync(d.CatalogNo);
            // BGM メドレー集約・枝番付与など、表示専用の整形を C# 側で行う
            var displayRows = BuildDisplayRows(rawTracks);
            _bindingTracks.DataSource = displayRows;
            // ラベルには「集約前のトラック件数 / 集約後に見える行数」を両方出すと体感と一致する
            int distinctTracks = rawTracks.Select(r => r.TrackNo).Distinct().Count();
            lblTracks.Text = $"トラック一覧 - {d.CatalogNo} {d.DisplayTitle} （{distinctTracks} トラック / {displayRows.Count} 行）";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // 表示行の集約・整形
    // =========================================================================

    /// <summary>
    /// DB から取得した生トラック行（sub_order ごとに分かれた複数行を含む）を、表示用の行リストに変換する。
    /// <list type="bullet">
    ///   <item>BGM で同一 track_no に複数 sub_order 行がある場合は 1 行に集約し、タイトル注釈を
    ///     <c>(M1 [Menu1] + M2 [Menu2] + ...)</c> 形式で連結する（メドレーの一括表示）</item>
    ///   <item>BGM の単独行（sub_order=0 のみ）でも、タイトル注釈 <c>(M1 [Menu1])</c> を付与する</item>
    ///   <item>非 BGM（SONG 等）で sub_order &gt;= 1 の子行は、そのまま別行として残し、
    ///     <see cref="TrackBrowserRow.TrackNoDisplay"/> に <c>"{TrackNo}-{SubOrder+1}"</c>（例: "24-2"）を入れる</item>
    ///   <item>sub_order=0 の単独行は <c>TrackNoDisplay = "{TrackNo}"</c>（例: "24"）</item>
    /// </list>
    /// <para>集約時に採用する属性（作詞/作曲/編曲/尺/備考/アーティスト）は全て sub_order=0 の行のもの。
    /// sub_order &gt;= 1 の行が異なる bgm_cues 参照を持ち、結果として異なる作曲者/編曲者が割り当たっているケースでは、
    /// その違いは集約後に表示されないことに注意（現状の運用では同一セッション内で作曲者も同一なのが通常）。</para>
    /// </summary>
    private static List<TrackBrowserRow> BuildDisplayRows(IReadOnlyList<TrackBrowserRow> raw)
    {
        var result = new List<TrackBrowserRow>();

        // track_no でグルーピングして処理する。SQL 側で既に ORDER BY track_no, sub_order で揃えているが、
        // GroupBy は順序非保証なので明示的に track_no 昇順でソートし直す。
        var groups = raw.GroupBy(r => r.TrackNo).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            // sub_order 昇順で並べ、0（または最小値）を親として扱う
            var sorted = group.OrderBy(r => r.SubOrder).ToList();
            if (sorted.Count == 0) continue;

            var head = sorted[0];
            bool headIsBgm = string.Equals(head.ContentKindCode, "BGM", StringComparison.Ordinal);

            if (headIsBgm && sorted.Count >= 2)
            {
                // === BGM メドレー集約：複数 sub_order を 1 行にまとめる ===
                // タイトルは head.DisplayTitle（SQL の COALESCE で組んだベース部分）に、
                // 全 sub_order 行分の "(M番号 [メニュー])" を " + " で連結した注釈を添える。
                var annotations = sorted
                    .Select(BuildBgmAnnotationFragment)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                string baseTitle = head.DisplayTitle ?? "";
                head.DisplayTitle = annotations.Count > 0
                    ? $"{baseTitle} ({string.Join(" + ", annotations)})"
                    : baseTitle;
                // 集約後の 1 行なので、枝番はつけずトラック番号そのまま
                head.TrackNoDisplay = head.TrackNo.ToString(CultureInfo.InvariantCulture);
                result.Add(head);
            }
            else
            {
                // === 個別行：BGM 単独（注釈付与）、または非 BGM（sub_order ごとに別行＋枝番） ===
                foreach (var row in sorted)
                {
                    // BGM 単独行は (m_no_detail [menu_title]) の注釈を付与する
                    if (string.Equals(row.ContentKindCode, "BGM", StringComparison.Ordinal))
                    {
                        string frag = BuildBgmAnnotationFragment(row);
                        if (!string.IsNullOrEmpty(frag))
                        {
                            row.DisplayTitle = $"{row.DisplayTitle} ({frag})";
                        }
                    }

                    // トラック番号表記：sub_order=0 はそのまま、sub_order>=1 は "{TrackNo}-{SubOrder+1}"
                    // 例）track_no=24 / sub_order=1 → "24-2"、sub_order=2 → "24-3"
                    row.TrackNoDisplay = row.SubOrder == 0
                        ? row.TrackNo.ToString(CultureInfo.InvariantCulture)
                        : $"{row.TrackNo}-{row.SubOrder + 1}";

                    result.Add(row);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// BGM 行 1 件の「M番号 [メニュー]」断片を作る。タイトル注釈の連結パーツ。
    /// <list type="bullet">
    ///   <item>m_no_detail が無い: 空文字（呼び出し側で注釈なし扱い）</item>
    ///   <item>menu_title が非空: <c>"M84(スローテンポ) [危機]"</c> のように [...] を付ける</item>
    ///   <item>menu_title が NULL/空: m_no_detail のみ</item>
    /// </list>
    /// </summary>
    private static string BuildBgmAnnotationFragment(TrackBrowserRow row)
    {
        if (string.IsNullOrEmpty(row.BgmMNoDetail)) return "";
        return !string.IsNullOrEmpty(row.BgmMenuTitle)
            ? $"{row.BgmMNoDetail} [{row.BgmMenuTitle}]"
            : row.BgmMNoDetail!;
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
