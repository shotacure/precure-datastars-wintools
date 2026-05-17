using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Preview;

/// <summary>
/// クレジット HTML プレビューウィンドウ。
/// <para>
/// クレジット編集画面の「🌐 HTML プレビュー」ボタンから起動される非モーダルフォーム。
/// 現在選択中のクレジットを <see cref="CreditPreviewRenderer"/> で HTML 化し、
/// <see cref="WebBrowser"/> コントロールに表示する。
/// </para>
/// <para>
/// 親フォーム（<see cref="CreditEditorForm"/>）からは、クレジット選択切替や保存／取消の
/// タイミングで <see cref="ReloadAsync"/> が呼ばれて自動再描画される。
/// </para>
/// <para>
/// アクセシビリティは <c>internal</c>。コンストラクタ引数の <see cref="CreditPreviewRenderer"/> が
/// <c>internal sealed</c> なため、本クラスも <c>internal</c> に揃える必要がある（CS0051 回避）。
/// </para>
/// </summary>
internal partial class CreditPreviewForm : Form
{
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditPreviewRenderer _renderer;

    /// <summary>現在表示中のクレジット情報（同エピソードの OP/ED を引くために保持）。</summary>
    private Credit? _currentCredit;

    public CreditPreviewForm(
        CreditsRepository creditsRepo,
        CreditPreviewRenderer renderer)
    {
        _creditsRepo = creditsRepo ?? throw new ArgumentNullException(nameof(creditsRepo));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        InitializeComponent();
    }

    /// <summary>
    /// 表示するクレジットを切り替えて再描画する。クレジット編集画面側からクレジット選択切替・保存・
    /// 取消のタイミングで呼ばれる。
    /// <para>
    /// 渡されたクレジットがエピソードスコープなら、同エピソードの全クレジット（OP / ED 等）を
    /// 縦に並べて表示する（OP → ED → ... の順）。シリーズスコープなら当該クレジット 1 件のみ。
    /// </para>
    /// </summary>
    public async Task ReloadAsync(Credit? credit)
    {
        _currentCredit = credit;
        try
        {
            if (credit is null)
            {
                webBrowser.DocumentText = "<html><body style='font-family:sans-serif;color:#999;padding:24px'>（クレジット未選択）</body></html>";
                Text = "クレジットプレビュー";
                return;
            }

            // 同エピソード or 同シリーズの全クレジットを取得
            IReadOnlyList<Credit> credits;
            string titleSuffix;
            if (credit.ScopeKind == "EPISODE" && credit.EpisodeId is int eid)
            {
                credits = await _creditsRepo.GetByEpisodeAsync(eid).ConfigureAwait(true);
                // 取得結果には is_deleted=1 の論理削除行も入る可能性があるので除外
                var filtered = new List<Credit>();
                foreach (var c in credits) if (!c.IsDeleted) filtered.Add(c);
                // CreditKind 順で並べ替え（OP → ED の見やすい順）
                filtered.Sort((a, b) => string.Compare(a.CreditKind, b.CreditKind, StringComparison.Ordinal));
                credits = filtered;
                titleSuffix = $"エピソード #{eid}";
            }
            else if (credit.ScopeKind == "SERIES")
            {
                // シリーズスコープは 1 件単独で表示（同シリーズの OP/ED を全部出すと multi-series 編集の混乱の元）
                credits = new[] { credit };
                titleSuffix = $"シリーズ #{credit.SeriesId}";
            }
            else
            {
                credits = new[] { credit };
                titleSuffix = "(scope 不明)";
            }

            string html = await _renderer.RenderCreditsAsync(credits).ConfigureAwait(true);
            webBrowser.DocumentText = html;
            Text = $"クレジットプレビュー - {titleSuffix}";
        }
        catch (Exception ex)
        {
            // プレビュー失敗時は本文にエラーメッセージを表示（モーダルの MessageBox は出さない、非モーダル UX を保つ）
            string esc = System.Net.WebUtility.HtmlEncode(ex.ToString());
            webBrowser.DocumentText = $"<html><body style='font-family:sans-serif;color:#c0392b;padding:24px'><h2>プレビュー生成エラー</h2><pre>{esc}</pre></body></html>";
        }
    }
}