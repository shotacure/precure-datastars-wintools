using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// キャラクター名義（<c>character_aliases</c>）の検索・選択ダイアログ
/// （v1.2.0 工程 B-3b 追加）。
/// <para>
/// クレジットエントリの CHARACTER_VOICE 種別では「キャラ本体（characters）」ではなく
/// 「キャラ名義（character_aliases）」の ID を直接持つため、本体ピッカー
/// （<see cref="CharacterPickerDialog"/>）とは別に名義ピッカーを用意する。
/// </para>
/// <para>
/// 検索キーワードは name_ja と name_kana_ja（モデル上は <see cref="CharacterAlias.NameKana"/>）の
/// 両方に対する部分一致。リポジトリ側に専用 SearchAsync が無い前提なので、
/// 全件取得してメモリ上でフィルタする方式を採る（既存マスタピッカー
/// <see cref="RolePickerDialog"/> と同じ実装スタイル）。
/// </para>
/// </summary>
public partial class CharacterAliasPickerDialog : Form
{
    private readonly CharacterAliasesRepository _repo;
    private readonly SearchDebouncer _debouncer;

    private System.Collections.Generic.List<CharacterAlias> _all = new();

    /// <summary>選択結果のキャラ名義 ID。OK で選択されていれば値あり、キャンセルなら null。</summary>
    public int? SelectedId { get; private set; }

    public CharacterAliasPickerDialog(CharacterAliasesRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, ApplyFilter);

        txtKeyword.TextChanged += (_, __) => _debouncer.Trigger();
        lvResults.DoubleClick += (_, __) => OnDoubleClick();
        btnOk.Click += (_, __) => OnOkClick();
        FormClosed += (_, __) => _debouncer.Dispose();
        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>初回ロード：全件取得して内部リストに保持し、フィルタ未適用の状態で表示。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var rows = await _repo.GetAllAsync();
            _all = rows is System.Collections.Generic.List<CharacterAlias> l ? l : new System.Collections.Generic.List<CharacterAlias>(rows);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "検索エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>キーワードで内部リストを部分一致絞込みし、ListView を再構築する。</summary>
    private void ApplyFilter()
    {
        string keyword = (txtKeyword.Text ?? "").Trim();
        System.Collections.Generic.IEnumerable<CharacterAlias> filtered = _all;
        if (keyword.Length > 0)
        {
            string kw = keyword.ToLowerInvariant();
            filtered = System.Linq.Enumerable.Where(_all, a =>
                (a.Name?.ToLowerInvariant().Contains(kw) ?? false) ||
                (a.NameKana?.ToLowerInvariant().Contains(kw) ?? false));
        }

        lvResults.BeginUpdate();
        try
        {
            lvResults.Items.Clear();
            foreach (var a in System.Linq.Enumerable.OrderBy(filtered, x => x.Name))
            {
                // v1.2.1: character_aliases から valid_from / valid_to を撤去したので、
                // 旧来の「有効期間」カラム表示は廃止。3 カラム（alias_id / name / name_kana）構成に変更。
                var item = new ListViewItem(new[]
                {
                    a.AliasId.ToString(),
                    a.Name ?? "",
                    a.NameKana ?? ""
                })
                { Tag = a };
                lvResults.Items.Add(item);
            }
        }
        finally
        {
            lvResults.EndUpdate();
        }
        lblHitCount.Text = $"{lvResults.Items.Count} 件 / 全 {_all.Count} 件";
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
        if (lvResults.SelectedItems[0].Tag is CharacterAlias a) SelectedId = a.AliasId;
    }
}
