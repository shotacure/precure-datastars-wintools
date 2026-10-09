using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// メドレーの曲の中身（song_medley_parts）の編集ダイアログ。
/// 1 行 = メドレーの中の 1 曲で、上から順に part_seq 1, 2, … になる。同じ原曲を何度並べてもよい。
/// 原曲（版違いは表記どおりの版の曲）を選んで並べる。原曲の曲名・作詞・作曲・編曲は原曲から引くので、ここでは入れない。
/// 「保存」でメドレーの中身を丸ごと差し替える（<see cref="SongMedleyPartsRepository.ReplaceAllAsync"/>）。
/// </summary>
public sealed class SongMedleyPartsEditDialog : Form
{
    private readonly SongMedleyPartsRepository _partsRepo;
    private readonly SongsRepository _songsRepo;
    private readonly int _medleySongId;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoGenerateColumns = false
    };
    private readonly Button _btnAdd = new() { Text = "行を足す", Size = new Size(96, 28) };
    private readonly Button _btnDelete = new() { Text = "行を消す", Size = new Size(96, 28) };
    private readonly Button _btnUp = new() { Text = "上へ", Size = new Size(64, 28) };
    private readonly Button _btnDown = new() { Text = "下へ", Size = new Size(64, 28) };
    private readonly Button _btnSave = new() { Text = "保存", Size = new Size(96, 28) };
    private readonly Button _btnCancel = new() { Text = "キャンセル", Size = new Size(96, 28), DialogResult = DialogResult.Cancel };

    private readonly BindingList<PartRow> _rows = new();

    /// <summary>原曲が DB に無いことを表す値（表のコンボボックスは null を照合できないため 0 で持つ）。</summary>
    private const int NoSource = 0;

    /// <summary>ダイアログを構築する。<paramref name="medleySongId"/> が編集するメドレーの曲。</summary>
    public SongMedleyPartsEditDialog(SongMedleyPartsRepository partsRepo, SongsRepository songsRepo, int medleySongId, string medleyTitle)
    {
        _partsRepo = partsRepo ?? throw new ArgumentNullException(nameof(partsRepo));
        _songsRepo = songsRepo ?? throw new ArgumentNullException(nameof(songsRepo));
        _medleySongId = medleySongId;

        Text = $"メドレーの中身の編集：#{medleySongId} {medleyTitle}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 480);
        MinimizeBox = false;
        CancelButton = _btnCancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange(new Control[] { _btnAdd, _btnDelete, _btnUp, _btnDown, new Label { Width = 40 }, _btnSave, _btnCancel });
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 6, 8, 0), ForeColor = Color.DimGray,
            Text = "上から順にメドレーの 1 曲目、2 曲目…。原曲は表記どおりの版の曲を選ぶ（DB に無ければ「DB に無い」のまま）。"
        };
        Controls.Add(_grid);
        Controls.Add(hint);
        Controls.Add(buttons);

        _btnAdd.Click += (_, _) => { _rows.Add(new PartRow()); SelectRow(_rows.Count - 1); };
        _btnDelete.Click += (_, _) => { if (CurrentIndex() is int i) { _rows.RemoveAt(i); SelectRow(Math.Min(i, _rows.Count - 1)); } };
        _btnUp.Click += (_, _) => MoveRow(-1);
        _btnDown.Click += (_, _) => MoveRow(+1);
        _btnSave.Click += async (_, _) => await SaveAsync();
        Load += async (_, _) => await LoadAsync();
    }

    /// <summary>原曲の候補（曲 ID と曲名）を読み込み、既存の中身を表に並べる。</summary>
    private async Task LoadAsync()
    {
        try
        {
            var songs = (await _songsRepo.GetAllAsync()).OrderBy(s => s.SongId).ToList();
            var choices = new List<SongChoice> { new(NoSource, "（DB に無い）") };
            choices.AddRange(songs.Select(s => new SongChoice(s.SongId, $"#{s.SongId} {s.Title}")));

            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "原曲", DataPropertyName = nameof(PartRow.SourceSongId), Width = 420,
                DataSource = choices, ValueMember = nameof(SongChoice.Id), DisplayMember = nameof(SongChoice.Label),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "備考", DataPropertyName = nameof(PartRow.Notes), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (var p in await _partsRepo.GetByMedleyAsync(_medleySongId))
            {
                _rows.Add(new PartRow
                {
                    SourceSongId = p.SourceSongId ?? NoSource,
                    Notes = p.Notes
                });
            }
            _grid.DataSource = _rows;
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>表の並びどおりにメドレーの中身を丸ごと差し替えて閉じる。</summary>
    private async Task SaveAsync()
    {
        try
        {
            _grid.EndEdit();
            var parts = _rows.Select(r => new SongMedleyPart
            {
                MedleySongId = _medleySongId,
                SourceSongId = r.SourceSongId == NoSource ? null : r.SourceSongId,
                Notes = NullIfBlank(r.Notes)
            }).ToList();
            await _partsRepo.ReplaceAllAsync(_medleySongId, parts, Environment.UserName);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private void MoveRow(int delta)
    {
        if (CurrentIndex() is not int i) return;
        int j = i + delta;
        if (j < 0 || j >= _rows.Count) return;
        var row = _rows[i];
        _rows.RemoveAt(i);
        _rows.Insert(j, row);
        SelectRow(j);
    }

    private int? CurrentIndex() => _grid.CurrentRow is { Index: >= 0 } r && r.Index < _rows.Count ? r.Index : null;

    private void SelectRow(int index)
    {
        if (index < 0 || index >= _grid.Rows.Count) return;
        _grid.ClearSelection();
        _grid.Rows[index].Selected = true;
        _grid.CurrentCell = _grid.Rows[index].Cells[0];
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>表の 1 行。</summary>
    private sealed class PartRow
    {
        /// <summary>原曲の song_id（<see cref="NoSource"/> は DB に無い）。</summary>
        public int SourceSongId { get; set; } = NoSource;
        public string? Notes { get; set; }
    }

    /// <summary>原曲の選択肢（<see cref="NoSource"/> は「DB に無い」）。</summary>
    private sealed record SongChoice(int Id, string Label);
}
