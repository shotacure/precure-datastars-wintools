using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジットエディタの警告ペイン表示。（機能単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditEditorForm
{
    // ───────────── 警告ペインの元データ（Stage 2 追加） ─────────────
    // フィルタトグル切替時に「すでに集計した警告群を再フィルタするだけ」で済むよう、
    // 直近の警告データを保持する。重複グルーピングもこの上で実施する。

    /// <summary>警告ペインに反映済みの「アイテム単位」警告データ。フィルタ切替時の再描画に使う。</summary>
    private readonly List<WarningItemData> _currentWarnings = new();

    /// <summary>警告 1 件分の整形済みデータ（lvWarnings に表示する単位）。
    /// 重複グルーピング後の「ユニーク 1 行」を表す。</summary>
    private sealed class WarningItemData
    {
        /// <summary>関連するテキスト行番号（1 始まり、無関係なら 0）。
        /// 同じメッセージで複数行のものをグルーピングする際は「最小行番号」を採用。</summary>
        public int LineNumber { get; init; }

        /// <summary>重要度（Block / Warning / Info）。</summary>
        public Dialogs.WarningSeverity Severity { get; init; }

        /// <summary>表示メッセージ（オリジナル、1 行化済み）。</summary>
        public required string Message { get; init; }

        /// <summary>同じメッセージで重複していた件数（1 ならグルーピング無し）。</summary>
        public int Count { get; init; } = 1;

        /// <summary>「マスタ未登録の役職」警告のとき、役職表示名（テキスト中の見出し）を持つ。
        /// 非 null なら、行ダブルクリック時に <see cref="Dialogs.QuickAddRoleDialog"/> を
        /// この名前で起動する（行ジャンプ動作の代わり）。役職が DB に登録されたら自動的に
        /// テキスト再パースが走って警告が消える。</summary>
        public string? UnresolvedRoleName { get; init; }

        /// <summary>「マスタ未登録の所属屋号」警告のとき、屋号表示名（テキスト中の括弧内）を持つ。
        /// 非 null なら、行ダブルクリック時に <see cref="Dialogs.QuickAddCompanyAliasDialog"/> を
        /// この名前で起動する。屋号が登録されたら自動的にテキスト再パースが走って警告が消える。</summary>
        public string? UnresolvedAffiliationName { get; init; }
    }

    /// <summary>警告ペインの内容を、パイプライン実行結果で更新する。
    /// <paramref name="parsed"/> の <c>Warnings</c>（行番号付き構文警告）と、<paramref name="infoMessages"/>
    /// （マスタ解決時の「✅ … 追加予定」「⚠ … 1 字違い」等の文字列リスト）を結合し、
    /// 同じメッセージ文字列で重複していたら「×N」表記でグルーピングしたうえで <see cref="_currentWarnings"/> に格納する。
    /// 実際の ListView 描画は <see cref="RenderWarningsToListView"/> に委譲（フィルタ切替時に再呼び出し可）。</summary>
    private void UpdateWarningsPane(
        Dialogs.BulkParseResult? parsed,
        IReadOnlyList<string>? infoMessages,
        IReadOnlyList<Dialogs.ParsedRole>? unresolvedRoles,
        IReadOnlyList<Dialogs.UnresolvedAffiliation>? unresolvedAffiliations)
    {
        _currentWarnings.Clear();

        // (a) 元の警告群を「(message → エントリ群)」辞書にまとめる。
        // メッセージ文字列をキーに、最小 LineNumber と件数を集計する。
        var grouped = new Dictionary<(Dialogs.WarningSeverity Sev, string Msg),
                                     (int MinLine, int Count)>();
        void Add(int line, Dialogs.WarningSeverity sev, string msg)
        {
            string oneLine = (msg ?? "").Replace("\r", " ").Replace("\n", " ");
            var key = (sev, oneLine);
            if (grouped.TryGetValue(key, out var prev))
            {
                int minLine = (prev.MinLine == 0)
                    ? line
                    : (line == 0 ? prev.MinLine : Math.Min(prev.MinLine, line));
                grouped[key] = (minLine, prev.Count + 1);
            }
            else
            {
                grouped[key] = (line, 1);
            }
        }
        if (parsed is not null)
        {
            foreach (var w in parsed.Warnings) Add(w.LineNumber, w.Severity, w.Message);
        }
        if (infoMessages is not null)
        {
            foreach (var msg in infoMessages)
            {
                var sev = (msg ?? "").StartsWith("⚠", StringComparison.Ordinal)
                    ? Dialogs.WarningSeverity.Warning
                    : Dialogs.WarningSeverity.Info;
                Add(0, sev, msg ?? "");
            }
        }

        // (b) グループ化結果を _currentWarnings に格納（重要度 desc → 行番号 asc の順で安定ソート）。
        foreach (var kv in grouped
            .OrderByDescending(g => (int)g.Key.Sev)
            .ThenBy(g => g.Value.MinLine))
        {
            _currentWarnings.Add(new WarningItemData
            {
                LineNumber = kv.Value.MinLine,
                Severity = kv.Key.Sev,
                Message = kv.Key.Msg,
                Count = kv.Value.Count,
            });
        }

        // (c) マスタ未登録役職を独立した警告行として追加。ダブルクリック時の挙動が
        // 「行ジャンプ」ではなく「QuickAddRoleDialog 起動」に切り替わるよう
        // UnresolvedRoleName を立てておく。同名重複は HashSet で 1 件にまとめる。
        if (unresolvedRoles is not null && unresolvedRoles.Count > 0)
        {
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ur in unresolvedRoles)
            {
                string name = (ur.DisplayName ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!seenNames.Add(name)) continue;
                _currentWarnings.Add(new WarningItemData
                {
                    LineNumber = ur.LineNumber,
                    Severity = Dialogs.WarningSeverity.Block,
                    Message = $"役職「{name}」がマスタ未登録（ダブルクリックで登録ダイアログを開く）",
                    Count = 1,
                    UnresolvedRoleName = name,
                });
            }
        }

        // (d) マスタ未登録の所属屋号を警告化（重要度 Block、ダブルクリックで QuickAddCompanyAliasDialog 起動）。
        // クオート記法 ("..." 強制テキスト) は引き当てを試みないため、ここに来るのは引き当てを試みて失敗したものだけ。
        if (unresolvedAffiliations is not null && unresolvedAffiliations.Count > 0)
        {
            var seenAffilNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ua in unresolvedAffiliations)
            {
                string name = (ua.Name ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!seenAffilNames.Add(name)) continue;
                _currentWarnings.Add(new WarningItemData
                {
                    LineNumber = ua.LineNumber,
                    Severity = Dialogs.WarningSeverity.Block,
                    Message = $"所属屋号「{name}」がマスタ未登録（ダブルクリックで登録ダイアログを開く）",
                    Count = 1,
                    UnresolvedAffiliationName = name,
                });
            }
        }

        RenderWarningsToListView();
    }

    /// <summary>パースが例外で死んだ時に警告ペインを「エラー 1 件のみ」状態にする。</summary>
    private void UpdateWarningsPaneWithSingleError(string message)
    {
        _currentWarnings.Clear();
        _currentWarnings.Add(new WarningItemData
        {
            LineNumber = 0,
            Severity = Dialogs.WarningSeverity.Block,
            Message = (message ?? "").Replace("\r", " ").Replace("\n", " "),
            Count = 1,
        });
        RenderWarningsToListView();
    }

    /// <summary><see cref="_currentWarnings"/> を現在のフィルタチェック状態に従って lvWarnings に描画する。
    /// フィルタトグル切替時もここを再呼び出しすれば再フィルタが効く。
    /// ヘッダの件数バッジ（「⚠ 警告 (N / 全 M)」）もここで同期更新する。
    /// 編集中の頻繁な再描画でユーザーのスクロール位置が常時 0 に戻る問題を避けるため、
    /// Items.Clear 前に <see cref="ListView.TopItem"/> の Index と SelectedIndices を控えて、
    /// 再描画後に同 Index へ復元する（Items 件数を超えていれば末尾にクランプ）。</summary>
    private void RenderWarningsToListView()
    {
        bool showBlock   = chkFilterBlock.Checked;
        bool showWarning = chkFilterWarning.Checked;
        bool showInfo    = chkFilterInfo.Checked;

        int savedTopIndex = -1;
        var savedSelectedIndices = new List<int>();
        try
        {
            if (lvWarnings.TopItem is { } topItem)
            {
                savedTopIndex = topItem.Index;
            }
            foreach (int i in lvWarnings.SelectedIndices)
            {
                savedSelectedIndices.Add(i);
            }
        }
        catch { /* 初回描画時など TopItem 取得が失敗するケースをスルー */ }

        lvWarnings.BeginUpdate();
        try
        {
            lvWarnings.Items.Clear();
            int shownCount = 0;
            foreach (var w in _currentWarnings)
            {
                bool pass = w.Severity switch
                {
                    Dialogs.WarningSeverity.Block => showBlock,
                    Dialogs.WarningSeverity.Warning => showWarning,
                    _ => showInfo,
                };
                if (!pass) continue;
                AddWarningRow(w);
                shownCount++;
            }
            UpdateWarningsHeaderBadge(shownCount, _currentWarnings.Count);

            // 選択行を復元（範囲外の旧 index は無視）。
            foreach (int i in savedSelectedIndices)
            {
                if (i >= 0 && i < lvWarnings.Items.Count)
                {
                    lvWarnings.Items[i].Selected = true;
                }
            }
            // スクロール先頭位置を復元。Items 件数を超えていれば末尾にクランプ。
            if (savedTopIndex >= 0 && lvWarnings.Items.Count > 0)
            {
                int clamped = Math.Min(savedTopIndex, lvWarnings.Items.Count - 1);
                try { lvWarnings.TopItem = lvWarnings.Items[clamped]; }
                catch { /* TopItem 設定はハンドル未生成時に失敗することがあるので飲み込む */ }
            }
        }
        finally
        {
            lvWarnings.EndUpdate();
        }
    }

    /// <summary>WarningItemData 1 件を lvWarnings に行追加。Tag に LineNumber を入れてクリック→ジャンプで参照する。</summary>
    private void AddWarningRow(WarningItemData w)
    {
        (string icon, Color fore) = w.Severity switch
        {
            Dialogs.WarningSeverity.Block   => ("🔥", Color.FromArgb(0xCC, 0x00, 0x00)),
            Dialogs.WarningSeverity.Warning => ("⚠", Color.FromArgb(0xB0, 0x60, 0x00)),
            _                               => ("ⓘ", Color.FromArgb(0x00, 0x60, 0xC0)),
        };
        string lineText = w.LineNumber > 0 ? w.LineNumber.ToString() : "";
        string display = w.Count > 1 ? $"{w.Message}  (×{w.Count})" : w.Message;
        var item = new ListViewItem(lineText) { ForeColor = fore, ToolTipText = w.Message, Tag = w };
        item.SubItems.Add(icon);
        item.SubItems.Add(display);
        lvWarnings.Items.Add(item);
    }

    /// <summary>警告ペインヘッダの件数バッジを更新する。
    /// フィルタで除外されている件数があれば「⚠ 警告 (N / 全 M)」のような表記、なければ「⚠ 警告 (M)」、
    /// 件数 0 なら「⚠ 警告」のままにする。</summary>
    private void UpdateWarningsHeaderBadge(int shownCount, int totalCount)
    {
        if (totalCount == 0)
        {
            lblWarningsHeader.Text = "⚠ 警告";
        }
        else if (shownCount == totalCount)
        {
            lblWarningsHeader.Text = $"⚠ 警告 ({totalCount})";
        }
        else
        {
            lblWarningsHeader.Text = $"⚠ 警告 ({shownCount} / 全 {totalCount})";
        }
    }

    /// <summary>警告ペインの行をダブルクリックしたとき、その警告に紐付く <c>LineNumber</c> を
    /// テキストペインで選択して行頭にスクロールする。行番号 0（マスタ解決系で行番号を持たない警告）の
    /// 場合は何もしない。</summary>
    private async void OnWarningRowDoubleClick(object? sender, EventArgs e)
    {
        if (lvWarnings.SelectedItems.Count == 0) return;
        var item = lvWarnings.SelectedItems[0];
        if (item.Tag is not WarningItemData data) return;

        // マスタ未登録役職の警告行は、ダブルクリックで QuickAddRoleDialog を起動する。
        // 行ジャンプは「次に同じ警告が出てきた時にどこの行から始まったか」を出す既存挙動だが、
        // 未登録役職の場合は「登録すれば警告が消える」状況なので、ダイアログ直起動の方が UX 効率が高い。
        if (!string.IsNullOrEmpty(data.UnresolvedRoleName))
        {
            await OpenQuickAddRoleDialogAndReparseAsync(data.UnresolvedRoleName!);
            return;
        }

        // マスタ未登録の所属屋号も同様、QuickAddCompanyAliasDialog を起動する。
        if (!string.IsNullOrEmpty(data.UnresolvedAffiliationName))
        {
            await OpenQuickAddCompanyAliasDialogAndReparseAsync(data.UnresolvedAffiliationName!);
            return;
        }

        if (data.LineNumber <= 0) return;

        // txtBulkText の指定行の先頭オフセットを計算 → SelectionStart に設定 → ScrollToCaret。
        // 行は 1 始まりなので 0 始まりインデックスに変換。
        int lineIndex = data.LineNumber - 1;
        try
        {
            int offset = txtBulkText.GetFirstCharIndexFromLine(lineIndex);
            if (offset < 0) return; // 範囲外
            // 行末まで選択して該当行をハイライト表示。
            int lineEnd = (lineIndex + 1 < txtBulkText.Lines.Length)
                ? txtBulkText.GetFirstCharIndexFromLine(lineIndex + 1) - Environment.NewLine.Length
                : txtBulkText.TextLength;
            int len = Math.Max(0, lineEnd - offset);
            txtBulkText.Focus();
            txtBulkText.SelectionStart = offset;
            txtBulkText.SelectionLength = len;
            txtBulkText.ScrollToCaret();
        }
        catch
        {
            // 該当行が存在しない（テキスト編集後に行数が減った等）ケースは静かにスキップ。
        }
    }
}
