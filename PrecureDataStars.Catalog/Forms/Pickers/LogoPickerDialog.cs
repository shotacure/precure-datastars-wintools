using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// ロゴ（<c>logos</c>）の検索・選択ダイアログ（v1.2.0 工程 B-3b 追加）。
/// <para>
/// クレジットエントリの LOGO 種別で使う。logo_id だけでは何のロゴか分からないため、
/// 親屋号（company_aliases）の名前と CI バージョンラベルを併記して人間可読に表示する。
/// 検索キーワードは「屋号名」と「CI バージョンラベル」の両方に対する部分一致。
/// </para>
/// <para>
/// LogosRepository は専用 SearchAsync を持たないため、起動時に GetAllAsync で全件取得し、
/// 屋号情報は CompanyAliasesRepository.GetByIdAsync で個別解決して
/// メモリ上にスナップショット（<see cref="Row"/>）として保持する。
/// 入力の度に毎回 DB に行くと屋号 JOIN のコストが嵩むため。
/// </para>
/// </summary>
public partial class LogoPickerDialog : Form
{
    private readonly LogosRepository _logosRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>表示用に組み立てたスナップショット行。</summary>
    private List<Row> _all = new();

    /// <summary>選択結果のロゴ ID。OK で選択されていれば値あり、キャンセルなら null。</summary>
    public int? SelectedId { get; private set; }

    public LogoPickerDialog(LogosRepository logosRepo, CompanyAliasesRepository companyAliasesRepo)
    {
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, ApplyFilter);

        txtKeyword.TextChanged += (_, __) => _debouncer.Trigger();
        lvResults.DoubleClick += (_, __) => OnDoubleClick();
        btnOk.Click += (_, __) => OnOkClick();
        FormClosed += (_, __) => _debouncer.Dispose();
        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>
    /// 初回ロード：全ロゴを取得し、各々の親屋号を解決して表示用スナップショットに変換する。
    /// 屋号は <see cref="Dictionary{TKey, TValue}"/> で 1 度だけ解決して N+1 を避ける。
    /// </summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var logos = await _logosRepo.GetAllAsync();
            // company_alias_id を集約して 1 回ずつ解決（同じ屋号配下に複数ロゴがあるケース）
            var distinctAliasIds = logos.Select(l => l.CompanyAliasId).Distinct().ToList();
            var aliasMap = new Dictionary<int, CompanyAlias?>();
            foreach (var id in distinctAliasIds)
            {
                aliasMap[id] = await _companyAliasesRepo.GetByIdAsync(id);
            }

            _all = logos.Select(l => new Row(
                logo: l,
                aliasName: aliasMap.TryGetValue(l.CompanyAliasId, out var ca) ? (ca?.Name ?? "(屋号未解決)") : "(屋号未解決)"
            )).ToList();

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
        IEnumerable<Row> filtered = _all;
        if (keyword.Length > 0)
        {
            string kw = keyword.ToLowerInvariant();
            filtered = _all.Where(r =>
                r.AliasName.ToLowerInvariant().Contains(kw) ||
                (r.Logo.CiVersionLabel?.ToLowerInvariant().Contains(kw) ?? false));
        }

        lvResults.BeginUpdate();
        try
        {
            lvResults.Items.Clear();
            foreach (var r in filtered.OrderBy(x => x.AliasName).ThenBy(x => x.Logo.CiVersionLabel))
            {
                string period = "";
                if (r.Logo.ValidFrom.HasValue || r.Logo.ValidTo.HasValue)
                {
                    string from = r.Logo.ValidFrom?.ToString("yyyy-MM-dd") ?? "";
                    string to = r.Logo.ValidTo?.ToString("yyyy-MM-dd") ?? "";
                    period = $"{from} 〜 {to}";
                }
                var item = new ListViewItem(new[]
                {
                    r.Logo.LogoId.ToString(),
                    r.AliasName,
                    r.Logo.CiVersionLabel ?? "",
                    period
                })
                { Tag = r.Logo };
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
        if (lvResults.SelectedItems[0].Tag is Logo l) SelectedId = l.LogoId;
    }

    /// <summary>表示用スナップショット行（ロゴ本体 + 解決済みの親屋号名）。</summary>
    private sealed class Row
    {
        public Logo Logo { get; }
        public string AliasName { get; }
        public Row(Logo logo, string aliasName) { Logo = logo; AliasName = aliasName; }
    }
}
