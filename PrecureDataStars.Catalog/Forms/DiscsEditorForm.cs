using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ディスク・トラック管理エディタ。
/// <para>
/// 左: ディスク一覧、右上: ディスク詳細、右下: トラック一覧 + トラック詳細。
/// トラックの <c>content_kind</c> に応じて <c>song_recording</c> または <c>bgm_recording</c>
/// への紐付けを行う UI を提供する。
/// </para>
/// <para>
/// v1.1.1 よりディスク詳細エリアにシリーズ選択コンボを追加。シリーズ所属が Disc 側の属性と
/// なったため、同一商品の複数枚組でもディスクごとに別シリーズを指定可能。
/// </para>
/// </summary>
public partial class DiscsEditorForm : Form
{
    private readonly DiscsRepository _discsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly ProductsRepository _productsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    private readonly SeriesRepository _seriesRepo;

    private List<Disc> _discs = new();
    private List<Track> _tracks = new();

    public DiscsEditorForm(
        DiscsRepository discsRepo,
        TracksRepository tracksRepo,
        ProductsRepository productsRepo,
        DiscKindsRepository discKindsRepo,
        TrackContentKindsRepository trackContentKindsRepo,
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        BgmCuesRepository bgmCuesRepo,
        SongSizeVariantsRepository songSizeVariantsRepo,
        SongPartVariantsRepository songPartVariantsRepo,
        SeriesRepository seriesRepo)
    {
        _discsRepo = discsRepo;
        _tracksRepo = tracksRepo;
        _productsRepo = productsRepo;
        _discKindsRepo = discKindsRepo;
        _trackContentKindsRepo = trackContentKindsRepo;
        _songsRepo = songsRepo;
        _songRecRepo = songRecRepo;
        _bgmCuesRepo = bgmCuesRepo;
        _songSizeVariantsRepo = songSizeVariantsRepo;
        _songPartVariantsRepo = songPartVariantsRepo;
        _seriesRepo = seriesRepo;

        InitializeComponent();
        Load += async (_, __) => await InitAsync();

        gridDiscs.SelectionChanged += async (_, __) => await OnDiscSelectedAsync();
        gridTracks.SelectionChanged += async (_, __) => await OnTrackSelectedAsync();

        btnDiscSave.Click += async (_, __) => await SaveDiscAsync();
        btnDiscDelete.Click += async (_, __) => await DeleteDiscAsync();
        btnDiscReload.Click += async (_, __) => await ReloadDiscsAsync();
        btnSearch.Click += async (_, __) => await SearchDiscsAsync();

        btnTrackSave.Click += async (_, __) => await SaveTrackAsync();
        btnTrackDelete.Click += async (_, __) => await DeleteTrackAsync();
        btnTrackNew.Click += (_, __) => ClearTrackForm();

        cboContentKind.SelectedIndexChanged += (_, __) => UpdateTrackLinkPanelVisibility();
        cboSongParent.SelectedIndexChanged += async (_, __) => await ReloadSongRecordingsAsync();
        // BGM は 1 テーブル化により cue 直接選択のみ。録音リロードイベントは不要。
    }

    /// <summary>初期化：ディスク種別・トラック内容種別マスタ、曲／BGM キューを読み込む。</summary>
    private async Task InitAsync()
    {
        try
        {
            // ディスク種別コンボ
            var discKinds = (await _discKindsRepo.GetAllAsync()).ToList();
            cboDiscKind.DisplayMember = "NameJa";
            cboDiscKind.ValueMember = "KindCode";
            cboDiscKind.DataSource = discKinds;
            cboDiscKind.DropDownStyle = ComboBoxStyle.DropDownList;

            // v1.1.1: ディスクのシリーズコンボ。先頭に NULL=オールスターズ項目を挿入する。
            var seriesAll = (await _seriesRepo.GetAllAsync()).ToList();
            var seriesItems = new List<DiscSeriesItem> { new DiscSeriesItem(null, "(オールスターズ)") };
            foreach (var s in seriesAll)
            {
                seriesItems.Add(new DiscSeriesItem(s.SeriesId, $"[{s.SeriesId}] {s.TitleShort ?? s.Title}"));
            }
            cboDiscSeries.DisplayMember = nameof(DiscSeriesItem.Label);
            cboDiscSeries.ValueMember = nameof(DiscSeriesItem.Id);
            cboDiscSeries.DataSource = seriesItems;
            cboDiscSeries.DropDownStyle = ComboBoxStyle.DropDownList;

            // トラック内容種別コンボ
            var contentKinds = (await _trackContentKindsRepo.GetAllAsync()).ToList();
            cboContentKind.DisplayMember = "NameJa";
            cboContentKind.ValueMember = "KindCode";
            cboContentKind.DataSource = contentKinds;
            cboContentKind.DropDownStyle = ComboBoxStyle.DropDownList;

            // 歌トラック用のサイズ種別コンボ（先頭に「(未設定)」を追加）
            var sizeVariants = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
            cboSongSize.DisplayMember = "Label";
            cboSongSize.ValueMember = "Code";
            cboSongSize.DataSource = PrependNullCodeItem(
                sizeVariants.Select(v => new CodeItemStr(v.VariantCode, v.NameJa)));
            cboSongSize.DropDownStyle = ComboBoxStyle.DropDownList;

            // 歌トラック用のパート種別コンボ（先頭に「(未設定)」を追加）
            var partVariants = (await _songPartVariantsRepo.GetAllAsync()).ToList();
            cboSongPart.DisplayMember = "Label";
            cboSongPart.ValueMember = "Code";
            cboSongPart.DataSource = PrependNullCodeItem(
                partVariants.Select(v => new CodeItemStr(v.VariantCode, v.NameJa)));
            cboSongPart.DropDownStyle = ComboBoxStyle.DropDownList;

            // 曲マスタコンボ（曲 = SONG トラック用）
            var songs = (await _songsRepo.GetAllAsync()).ToList();
            cboSongParent.DisplayMember = "Title";
            cboSongParent.ValueMember = "SongId";
            cboSongParent.DataSource = PrependNullSong(songs);
            cboSongParent.DropDownStyle = ComboBoxStyle.DropDownList;

            // BGM キューコンボ：全シリーズのキューを連結する
            var allCues = new List<BgmCueItem>();
            foreach (var s in seriesAll)
            {
                var cues = await _bgmCuesRepo.GetBySeriesAsync(s.SeriesId);
                foreach (var c in cues)
                {
                    string label = $"[{s.Title}] {c.MNoDetail} {c.MenuTitle ?? ""}".Trim();
                    allCues.Add(new BgmCueItem(c.SeriesId, c.MNoDetail, label));
                }
            }
            cboBgmCue.DisplayMember = "Label";
            cboBgmCue.ValueMember = null; // 複合キーのため ValueMember は使わず、SelectedItem から直接取り出す
            cboBgmCue.DataSource = PrependNullBgmCue(allCues);
            cboBgmCue.DropDownStyle = ComboBoxStyle.DropDownList;

            await ReloadDiscsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>ディスク一覧を全件取得。</summary>
    private async Task ReloadDiscsAsync()
    {
        string keyword = txtSearch.Text.Trim();
        _discs = (string.IsNullOrEmpty(keyword)
            ? await _discsRepo.SearchAsync("")
            : await _discsRepo.SearchAsync(keyword)).ToList();
        gridDiscs.DataSource = _discs;
        HideAuditColumns(gridDiscs);
    }

    private async Task SearchDiscsAsync() => await ReloadDiscsAsync();

    // ===== ディスク側 =====

    private async Task OnDiscSelectedAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc d)
        {
            ClearDiscForm();
            _tracks.Clear();
            gridTracks.DataSource = null;
            ClearTrackForm();
            return;
        }
        BindDiscToForm(d);
        try
        {
            // グリッド表示は sub_order=0 の親行のみ（本フォームはメドレーの子行を扱わない）
            _tracks = (await _tracksRepo.GetByCatalogNoAsync(d.CatalogNo)).Where(x => x.SubOrder == 0).ToList();
            gridTracks.DataSource = _tracks;
            HideAuditColumns(gridTracks);
            ClearTrackForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BindDiscToForm(Disc d)
    {
        txtCatalogNo.Text = d.CatalogNo;
        txtProductCatalogNo.Text = d.ProductCatalogNo;
        txtDiscTitle.Text = d.Title ?? "";
        txtDiscTitleShort.Text = d.TitleShort ?? "";
        txtDiscTitleEn.Text = d.TitleEn ?? "";
        // v1.1.1: シリーズを Disc の属性として反映。NULL はオールスターズ（先頭項目）。
        SetDiscSeriesComboValue(d.SeriesId);
        numDiscNoInSet.Value = d.DiscNoInSet ?? 0;
        cboDiscKind.SelectedValue = d.DiscKindCode ?? "";
        txtMediaFormat.Text = d.MediaFormat ?? "";
        txtMcn.Text = d.Mcn ?? "";
        numTotalTracks.Value = d.TotalTracks ?? 0;
        txtVolumeLabel.Text = d.VolumeLabel ?? "";
        txtDiscNotes.Text = d.Notes ?? "";
    }

    /// <summary>
    /// ディスク用シリーズコンボの選択を指定 ID に合わせる。NULL は先頭のオールスターズ項目。
    /// 一致する項目が見つからなければ先頭（オールスターズ）を選択する。
    /// </summary>
    private void SetDiscSeriesComboValue(int? seriesId)
    {
        if (cboDiscSeries.Items.Count == 0) return;
        foreach (var item in cboDiscSeries.Items)
        {
            if (item is DiscSeriesItem si && si.Id == seriesId)
            {
                cboDiscSeries.SelectedItem = si;
                return;
            }
        }
        cboDiscSeries.SelectedIndex = 0;
    }

    private void ClearDiscForm()
    {
        txtCatalogNo.Text = ""; txtProductCatalogNo.Text = "";
        txtDiscTitle.Text = ""; txtDiscTitleShort.Text = ""; txtDiscTitleEn.Text = "";
        // v1.1.1: シリーズは先頭（オールスターズ）に戻す
        if (cboDiscSeries.Items.Count > 0) cboDiscSeries.SelectedIndex = 0;
        numDiscNoInSet.Value = 0;
        if (cboDiscKind.Items.Count > 0) cboDiscKind.SelectedIndex = 0;
        txtMediaFormat.Text = "";
        txtMcn.Text = "";
        numTotalTracks.Value = 0;
        txtVolumeLabel.Text = "";
        txtDiscNotes.Text = "";
    }

    private async Task SaveDiscAsync()
    {
        try
        {
            var d = new Disc
            {
                CatalogNo = txtCatalogNo.Text.Trim(),
                ProductCatalogNo = txtProductCatalogNo.Text.Trim(),
                Title = NullIfEmpty(txtDiscTitle.Text),
                TitleShort = NullIfEmpty(txtDiscTitleShort.Text),
                TitleEn = NullIfEmpty(txtDiscTitleEn.Text),
                // v1.1.1: シリーズ ID をコンボから読み取って保存。NULL=オールスターズ。
                SeriesId = (cboDiscSeries.SelectedItem as DiscSeriesItem)?.Id,
                DiscNoInSet = numDiscNoInSet.Value == 0 ? null : (uint)numDiscNoInSet.Value,
                DiscKindCode = SelectedCode(cboDiscKind),
                // MediaFormat は NOT NULL。入力空なら既定値 "CD" を採用する。
                MediaFormat = NullIfEmpty(txtMediaFormat.Text) ?? "CD",
                Mcn = NullIfEmpty(txtMcn.Text),
                TotalTracks = numTotalTracks.Value == 0 ? null : (byte)numTotalTracks.Value,
                VolumeLabel = NullIfEmpty(txtVolumeLabel.Text),
                Notes = NullIfEmpty(txtDiscNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrWhiteSpace(d.CatalogNo)) { MessageBox.Show(this, "品番は必須です。"); return; }
            if (string.IsNullOrWhiteSpace(d.ProductCatalogNo)) { MessageBox.Show(this, "所属商品の代表品番を指定してください。"); return; }
            await _discsRepo.UpsertAsync(d);
            MessageBox.Show(this, $"ディスク [{d.CatalogNo}] を保存しました。");
            await ReloadDiscsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteDiscAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc d) return;
        if (Confirm($"ディスク [{d.CatalogNo}] を論理削除しますか？") != DialogResult.Yes) return;
        try
        {
            await _discsRepo.SoftDeleteAsync(d.CatalogNo, Environment.UserName);
            await ReloadDiscsAsync();
            ClearDiscForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== トラック側 =====

    private async Task OnTrackSelectedAsync()
    {
        if (gridTracks.CurrentRow?.DataBoundItem is not Track t)
        {
            ClearTrackForm();
            return;
        }
        await BindTrackToFormAsync(t);
    }

    private async Task BindTrackToFormAsync(Track t)
    {
        numTrackNo.Value = t.TrackNo;
        cboContentKind.SelectedValue = t.ContentKindCode;
        txtTrackTitleOverride.Text = t.TrackTitleOverride ?? "";
        numStartLba.Value = t.StartLba ?? 0;
        numLengthFrames.Value = t.LengthFrames ?? 0;
        txtIsrc.Text = t.Isrc ?? "";
        txtCdTextTitle.Text = t.CdTextTitle ?? "";
        txtCdTextPerformer.Text = t.CdTextPerformer ?? "";
        chkIsData.Checked = t.IsDataTrack;
        chkPreEmphasis.Checked = t.HasPreEmphasis;
        chkCopyOk.Checked = t.IsCopyPermitted;
        txtTrackNotes.Text = t.Notes ?? "";

        // SongRecording 選択：まず親曲を検索して SelectedValue に設定
        if (t.SongRecordingId is int srId && srId > 0)
        {
            try
            {
                var rec = await _songRecRepo.GetByIdAsync(srId);
                if (rec is not null)
                {
                    cboSongParent.SelectedValue = rec.SongId;
                    await ReloadSongRecordingsAsync();
                    cboSongRecording.SelectedValue = srId;
                }
            }
            catch { /* 取得失敗は無視して空選択にする */ }
        }
        else
        {
            if (cboSongParent.Items.Count > 0) cboSongParent.SelectedIndex = 0;
            cboSongRecording.DataSource = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
        }

        // BGM 参照：Track の 2 列キー (bgm_series_id, bgm_m_no_detail) から cue を直接選択。
        // ターン C の 1 テーブル統合により、録音選択用 cboBgmRecording は廃止された。
        if (t.BgmSeriesId is int bsId && !string.IsNullOrEmpty(t.BgmMNoDetail))
        {
            foreach (var item in cboBgmCue.Items)
            {
                if (item is BgmCueItem ci && ci.SeriesId == bsId && ci.MNoDetail == t.BgmMNoDetail)
                {
                    cboBgmCue.SelectedItem = ci;
                    break;
                }
            }
        }
        else
        {
            if (cboBgmCue.Items.Count > 0) cboBgmCue.SelectedIndex = 0;
        }

        // 歌トラックのサイズ／パート種別コンボを反映（SONG 以外では「(未設定)」で固定）
        SetSongVariantComboValue(cboSongSize, t.SongSizeVariantCode);
        SetSongVariantComboValue(cboSongPart, t.SongPartVariantCode);

        UpdateTrackLinkPanelVisibility();
    }

    /// <summary>
    /// サイズ／パート種別コンボの選択を指定コードに合わせる。
    /// 対象コードが見つからない（または NULL）場合は「(未設定)」にフォールバック。
    /// </summary>
    private static void SetSongVariantComboValue(ComboBox cbo, string? code)
    {
        if (cbo.Items.Count == 0) return;
        string target = code ?? "";
        foreach (var item in cbo.Items)
        {
            if (item is CodeItemStr ci && ci.Code == target)
            {
                cbo.SelectedItem = ci;
                return;
            }
        }
        // 見つからなければ先頭（「(未設定)」）を選択
        cbo.SelectedIndex = 0;
    }

    private void ClearTrackForm()
    {
        numTrackNo.Value = 0;
        if (cboContentKind.Items.Count > 0) cboContentKind.SelectedIndex = 0;
        txtTrackTitleOverride.Text = "";
        numStartLba.Value = 0;
        numLengthFrames.Value = 0;
        txtIsrc.Text = "";
        txtCdTextTitle.Text = "";
        txtCdTextPerformer.Text = "";
        chkIsData.Checked = false;
        chkPreEmphasis.Checked = false;
        chkCopyOk.Checked = false;
        txtTrackNotes.Text = "";
        if (cboSongParent.Items.Count > 0) cboSongParent.SelectedIndex = 0;
        if (cboBgmCue.Items.Count > 0) cboBgmCue.SelectedIndex = 0;
        cboSongRecording.DataSource = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
        // サイズ／パートも「(未設定)」に戻す
        if (cboSongSize.Items.Count > 0) cboSongSize.SelectedIndex = 0;
        if (cboSongPart.Items.Count > 0) cboSongPart.SelectedIndex = 0;
        UpdateTrackLinkPanelVisibility();
    }

    /// <summary>content_kind に応じて、関連パネルの可視性を切り替える。</summary>
    private void UpdateTrackLinkPanelVisibility()
    {
        var code = SelectedCode(cboContentKind) ?? "OTHER";
        bool isSong = code == "SONG";
        bool isBgm = code == "BGM";

        pnlSongLink.Visible = isSong;
        pnlBgmLink.Visible = isBgm;

        // track_title_override は SONG/BGM/その他すべてで使用可能（収録盤固有の表記を保持するため）。
        // したがって常時表示する。
        lblTitleOverride.Visible = true;
        txtTrackTitleOverride.Visible = true;

        // 歌トラックのサイズ／パート種別は SONG のときのみ表示。トリガーで SONG 以外は拒否される。
        lblSongSize.Visible = isSong;
        cboSongSize.Visible = isSong;
        lblSongPart.Visible = isSong;
        cboSongPart.Visible = isSong;
    }

    /// <summary>親曲選択変更 → 子録音リスト更新。</summary>
    private async Task ReloadSongRecordingsAsync()
    {
        if (cboSongParent.SelectedValue is not int songId || songId <= 0)
        {
            cboSongRecording.DataSource = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
            return;
        }
        try
        {
            var recs = await _songRecRepo.GetBySongIdAsync(songId);
            var items = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
            foreach (var r in recs)
            {
                // ラベル例: "#123 五條真由美 (Ver. MaxHeart)"
                // 実際のカラオケ/ガイド区別はトラック編集画面のパート種別コンボで行う。
                string label = $"#{r.SongRecordingId} {r.SingerName ?? "(歌唱者未設定)"} {r.VariantLabel ?? ""}";
                items.Add(new CodeItemInt(r.SongRecordingId, label.Trim()));
            }
            cboSongRecording.DisplayMember = "Label";
            cboSongRecording.ValueMember = "Id";
            cboSongRecording.DataSource = items;
            cboSongRecording.DropDownStyle = ComboBoxStyle.DropDownList;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ターン C の劇伴 1 テーブル化により ReloadBgmRecordingsAsync / NoneRecordingList は廃止。
    // BGM 紐付けは cboBgmCue（cue 一覧コンボ）だけで完結する。

    /// <summary>トラックの保存（upsert）。</summary>
    private async Task SaveTrackAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc parent)
        {
            MessageBox.Show(this, "先に親となるディスクを選択してください。"); return;
        }
        try
        {
            string contentKind = SelectedCode(cboContentKind) ?? "OTHER";
            int? songRecId = null;

            // BGM 参照 2 列（複合 FK）。BGM 以外のときは全て null になる。
            int? bgmSeriesId = null;
            string? bgmMNoDetail = null;

            if (contentKind == "SONG" && cboSongRecording.SelectedValue is int sid && sid > 0) songRecId = sid;
            if (contentKind == "BGM" && cboBgmCue.SelectedItem is BgmCueItem bci && !bci.IsNoneCue())
            {
                bgmSeriesId = bci.SeriesId;
                bgmMNoDetail = bci.MNoDetail;
            }

            // 歌トラックのサイズ／パート種別。SONG 以外では NULL（トリガーで許可されない）。
            string? songSizeCode = null;
            string? songPartCode = null;
            if (contentKind == "SONG")
            {
                if (cboSongSize.SelectedItem is CodeItemStr sv && !string.IsNullOrEmpty(sv.Code))
                    songSizeCode = sv.Code;
                if (cboSongPart.SelectedItem is CodeItemStr pv && !string.IsNullOrEmpty(pv.Code))
                    songPartCode = pv.Code;
            }

            // 排他制約: SONG なら song_recording_id 必須、BGM なら bgm 2 列必須
            if (contentKind == "SONG" && songRecId is null)
            {
                MessageBox.Show(this, "SONG トラックには曲録音を選択してください。"); return;
            }
            if (contentKind == "BGM" && bgmSeriesId is null)
            {
                MessageBox.Show(this, "BGM トラックには劇伴 cue を選択してください。"); return;
            }

            var t = new Track
            {
                CatalogNo = parent.CatalogNo,
                TrackNo = (byte)numTrackNo.Value,
                SubOrder = 0, // 本フォームは sub_order=0 の親行のみ編集する。メドレーの子行は別画面で扱う想定。
                ContentKindCode = contentKind,
                SongRecordingId = songRecId,
                SongSizeVariantCode = songSizeCode,
                SongPartVariantCode = songPartCode,
                BgmSeriesId = bgmSeriesId,
                BgmMNoDetail = bgmMNoDetail,
                TrackTitleOverride = NullIfEmpty(txtTrackTitleOverride.Text),
                StartLba = numStartLba.Value == 0 ? null : (uint)numStartLba.Value,
                LengthFrames = numLengthFrames.Value == 0 ? null : (uint)numLengthFrames.Value,
                Isrc = NullIfEmpty(txtIsrc.Text),
                CdTextTitle = NullIfEmpty(txtCdTextTitle.Text),
                CdTextPerformer = NullIfEmpty(txtCdTextPerformer.Text),
                IsDataTrack = chkIsData.Checked,
                HasPreEmphasis = chkPreEmphasis.Checked,
                IsCopyPermitted = chkCopyOk.Checked,
                Notes = NullIfEmpty(txtTrackNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (t.TrackNo == 0) { MessageBox.Show(this, "トラック番号を 1 以上で指定してください。"); return; }
            await _tracksRepo.UpsertAsync(t);
            // グリッド表示は sub_order=0 の親行のみ。メドレーの子行 (sub_order>0) は別管理のため非表示。
            _tracks = (await _tracksRepo.GetByCatalogNoAsync(parent.CatalogNo)).Where(x => x.SubOrder == 0).ToList();
            gridTracks.DataSource = _tracks;
            HideAuditColumns(gridTracks);
            MessageBox.Show(this, $"トラック #{t.TrackNo} を保存しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteTrackAsync()
    {
        if (gridTracks.CurrentRow?.DataBoundItem is not Track t) return;
        if (Confirm($"トラック #{t.TrackNo} を削除しますか？メドレー等の sub_order 子行も一緒に削除されます。") != DialogResult.Yes) return;
        try
        {
            // 親行 (sub_order=0) を削除するときは、同じ track_no の子行 (sub_order>0) も全て消す。
            await _tracksRepo.DeleteAllSubOrdersAsync(t.CatalogNo, t.TrackNo);
            if (gridDiscs.CurrentRow?.DataBoundItem is Disc parent)
            {
                _tracks = (await _tracksRepo.GetByCatalogNoAsync(parent.CatalogNo)).Where(x => x.SubOrder == 0).ToList();
                gridTracks.DataSource = _tracks;
                HideAuditColumns(gridTracks);
            }
            ClearTrackForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== ヘルパ =====

    private static void HideAuditColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn c in grid.Columns)
        {
            if (c.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy" or "IsDeleted")
                c.Visible = false;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? SelectedCode(ComboBox cbo)
    {
        var v = cbo.SelectedValue?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static List<SongItem> PrependNullSong(IEnumerable<Song> songs)
    {
        var result = new List<SongItem> { new SongItem(0, "(未設定)") };
        foreach (var s in songs) result.Add(new SongItem(s.SongId, s.Title));
        return result;
    }

    private static List<BgmCueItem> PrependNullBgmCue(IEnumerable<BgmCueItem> cues)
    {
        // 「未設定」枠は SeriesId=0, MNoDetail="" のセンチネル値
        var result = new List<BgmCueItem> { new BgmCueItem(0, "", "(未設定)") };
        result.AddRange(cues);
        return result;
    }

    /// <summary>
    /// 文字列コード付きコンボ（サイズ種別／パート種別）用の先頭「(未設定)」付きリストを作る。
    /// "(未設定)" 項目のコードは空文字で表現する。
    /// </summary>
    private static List<CodeItemStr> PrependNullCodeItem(IEnumerable<CodeItemStr> items)
    {
        var result = new List<CodeItemStr> { new CodeItemStr("", "(未設定)") };
        result.AddRange(items);
        return result;
    }

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>曲コンボ用の表示項目。</summary>
    private sealed record SongItem(int SongId, string Title);

    /// <summary>
    /// BGM キューコンボ用の表示項目。複合キー (SeriesId, MNoDetail) を保持する。
    /// Label は「[シリーズ名] M番号 メニュー名」の整形済み表示文字列。
    /// </summary>
    private sealed record BgmCueItem(int SeriesId, string MNoDetail, string Label)
    {
        /// <summary>「未設定」枠（センチネル: SeriesId=0 & MNoDetail="")。</summary>
        public bool IsNoneCue() => SeriesId == 0 && string.IsNullOrEmpty(MNoDetail);
    }

    /// <summary>
    /// v1.1.1 で追加されたディスク側シリーズコンボの項目。
    /// Id=null はオールスターズ扱い（先頭要素）。
    /// </summary>
    private sealed record DiscSeriesItem(int? Id, string Label);

    // ターン C の 1 テーブル統合で BgmRecItem は廃止。

    /// <summary>int ID を持つ汎用コード項目。</summary>
    private sealed record CodeItemInt(int Id, string Label);

    /// <summary>
    /// 文字列コード（VARCHAR マスタキー）を持つ汎用コード項目。
    /// サイズ種別 / パート種別コンボで使用。Code が空文字なら「(未設定)」を意味する。
    /// </summary>
    private sealed record CodeItemStr(string Code, string Label);
}
