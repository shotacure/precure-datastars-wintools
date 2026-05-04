using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 歌録音（<c>song_recordings</c>）の検索・選択ダイアログ（v1.2.0 工程 C 追加）。
/// <para>
/// <see cref="SongRecordingsRepository.SearchAsync"/> が親曲タイトル（<c>songs.title</c>）を
/// 含む <see cref="SongRecordingSearchResult"/> を返すため、ListView 上では
/// 「録音 ID / 曲タイトル / 歌手 / バリエーション / 曲 ID」の 5 列で表示する。
/// </para>
/// </summary>
public partial class SongRecordingPickerDialog : Form
{
    private readonly SongRecordingsRepository _repo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>選択結果の song_recording_id。</summary>
    public int? SelectedId { get; private set; }

    public SongRecordingPickerDialog(SongRecordingsRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, async () => await ReloadAsync());

        txtKeyword.TextChanged += (_, __) => _debouncer.Trigger();
        lvResults.DoubleClick += (_, __) => OnDoubleClick();
        btnOk.Click += (_, __) => OnOkClick();
        FormClosed += (_, __) => _debouncer.Dispose();
        Load += async (_, __) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var rows = await _repo.SearchAsync(txtKeyword.Text.Trim(), 100);
            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            foreach (var r in rows)
            {
                var item = new ListViewItem(new[]
                {
                    r.SongRecordingId.ToString(),
                    r.SongTitle ?? "",
                    r.SingerName ?? "",
                    r.VariantLabel ?? "",
                    r.SongId.ToString()
                })
                { Tag = r };
                lvResults.Items.Add(item);
            }
            lvResults.EndUpdate();
            lblHitCount.Text = $"{rows.Count} 件見つかりました（最大 100 件表示）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "検索エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDoubleClick()
    {
        if (lvResults.SelectedItems.Count == 0) return;
        OnOkClick();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnOkClick()
    {
        if (lvResults.SelectedItems.Count == 0)
        {
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (lvResults.SelectedItems[0].Tag is SongRecordingSearchResult r)
        {
            SelectedId = r.SongRecordingId;
        }
    }
}
