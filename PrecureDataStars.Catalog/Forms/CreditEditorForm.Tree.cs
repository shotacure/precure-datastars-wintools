using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms.Drafting;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジットエディタのツリービュー構築。（機能単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditEditorForm
{
    /// <summary>選択中クレジットのカード／役職／ブロック／エントリを TreeView に再構築する。</summary>
    /// fix4: 並列実行による Tree.Nodes 重複追加を防ぐため、
    /// 「先にローカル List にすべての TreeNode を組み立てきる → 最後に同期セクションで
    /// Clear → AddRange → EndUpdate を一気に実行」パターンに書き換えた。
    /// 旧実装では Nodes.Clear() の直後から DB アクセスの await を伴う foreach が続くため、
    /// ボタン連打や AfterSelect イベント連鎖で複数の RebuildTreeAsync が並列に await されると、
    /// 互いの Clear と Add が交互に走って同じカードノードが Tree に複数追加される問題があった。
    /// 新実装は同期反映区間に await を含まないので、並列で呼ばれても各呼び出しが
    /// 完成形のツリーで上書きするだけになり、重複は生じない。
    private async Task RebuildTreeAsync()
    {
        // で Draft 経由に切り替え。本メソッドは互換用ラッパで、
        // 実体は RebuildTreeFromDraftAsync が担う。Draft セッションが未構築の場合は何もしない
        // （クレジット未選択の状態。OnCreditSelectedAsync が呼ばれた時点で session が作られ、
        //  本メソッドが Draft からツリーを描画する流れになる）。
        await RebuildTreeFromDraftAsync();
    }

    /// <summary>エントリのマスタ参照列に Pending（負数仮 ID）が含まれているか判定する。
    /// 含まれていればツリーノードの ForeColor を赤にする条件として使う。
    /// 対象列：PersonAliasId / CharacterAliasId / CompanyAliasId / AffiliationCompanyAliasId / LogoId。</summary>
    private static bool HasPendingMasterId(CreditBlockEntry e)
        => (e.PersonAliasId is int p && p < 0)
        || (e.CharacterAliasId is int c && c < 0)
        || (e.CompanyAliasId is int co && co < 0)
        || (e.AffiliationCompanyAliasId is int af && af < 0)
        || (e.LogoId is int l && l < 0);

    /// <summary>Draft セッション（_draftSession）からツリーを構築する。</summary>
    /// 並列実行による Tree.Nodes 重複追加を防ぐため、「先にローカル List にすべての TreeNode を
    /// 組み立てきる → 最後に同期セクションで Clear → AddRange → EndUpdate を一気に実行」パターン。
    private async Task RebuildTreeFromDraftAsync()
    {
        if (_currentCredit is null || _draftSession is null) { ClearTreeAndPreview(); return; }

        // ─── フェーズ 0: 再構築前の TreeView 状態をスナップショット ───
        // 編集中の頻繁な再描画（テキスト打鍵 → デバウンス → Draft 反映）でユーザーの
        // スクロール位置・選択・展開状態が常時リセットされていた問題への対応。
        // パスは (NodeKind, CurrentId) の連鎖で表現する。Draft 内 CurrentId は同セッション内で安定なので、
        // 同一クレジット編集中は path → ノード逆引きが成立する。クレジット切替で CurrentId が総入れ替えに
        // なるケースでは復元時に見つからず黙ってフォールスルー（= 既定の ExpandAll になる）するため安全。
        var savedSelectedPath = treeStructure.SelectedNode is { } selBefore
            ? GetTreeNodePath(selBefore)
            : null;
        var savedTopPath = treeStructure.TopNode is { } topBefore
            ? GetTreeNodePath(topBefore)
            : null;
        var savedCollapsedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (TreeNode root in treeStructure.Nodes)
        {
            CollectCollapsedNodeKeys(root, savedCollapsedKeys);
        }

        // ─── フェーズ 1: Draft からツリーを組み立て（この間 treeStructure には触らない）───
        var newRootNodes = new List<TreeNode>();

        // ツリー上の Card 番号は 1 始まりの連続表示にする。
        int cardDisplayIndex = 1;
        foreach (var draftCard in _draftSession.Root.Cards.Where(c => c.State != DraftState.Deleted))
        {
            var card = draftCard.Entity;
            var cardNode = new TreeNode($"📂 Card #{cardDisplayIndex}{(string.IsNullOrEmpty(card.Notes) ? "" : "  " + card.Notes)}")
            {
                Tag = new NodeTag(NodeKind.Card, draftCard.CurrentId, draftCard)
            };
            cardDisplayIndex++;

            foreach (var draftTier in draftCard.Tiers.Where(t => t.State != DraftState.Deleted)
                                                       .OrderBy(t => t.Entity.TierNo))
            {
                var tier = draftTier.Entity;
                var tierKey = new TierKey(card.CardId, tier.CardTierId, tier.TierNo);
                var tierNode = new TreeNode($"📐 Tier {tier.TierNo}")
                {
                    Tag = new NodeTag(NodeKind.Tier, draftTier.CurrentId, draftTier)
                };

                foreach (var draftGroup in draftTier.Groups.Where(g => g.State != DraftState.Deleted)
                                                              .OrderBy(g => g.Entity.GroupNo))
                {
                    var grp = draftGroup.Entity;
                    var groupKey = new GroupKey(card.CardId, tier.CardTierId, tier.TierNo, grp.CardGroupId, grp.GroupNo);
                    var groupNode = new TreeNode($"🗂 Group {grp.GroupNo}")
                    {
                        Tag = new NodeTag(NodeKind.Group, draftGroup.CurrentId, draftGroup)
                    };

                    int roleDisplayIndex = 1;
                    foreach (var draftRole in draftGroup.Roles.Where(r => r.State != DraftState.Deleted)
                                                                  .OrderBy(r => r.Entity.OrderInGroup))
                    {
                        var role = draftRole.Entity;
                        string roleName = await _lookupCache.ResolveRoleNameAsync(role.RoleCode);

                        // 役職テンプレ展開（既存と同じロジック、Role エンティティは DB から再取得）
                        Role? roleEntity = string.IsNullOrEmpty(role.RoleCode)
                            ? null
                            : await _rolesRepo.GetByCodeAsync(role.RoleCode);
                        string roleNote = "";
                        bool isThemeSongRole = (roleEntity?.RoleFormatKind == "THEME_SONG");
                        if (isThemeSongRole && !string.IsNullOrEmpty(role.RoleCode))
                        {
                            // 役職の書式テンプレは role_templates テーブルで管理するため、
                            // 主題歌役職の columns 抽出はここで RoleTemplatesRepository.ResolveAsync 経由で
                            // テンプレを引いてから ExtractThemeSongsColumns に渡す。
                            // SERIES スコープなら credit.SeriesId、EPISODE スコープなら episodes 経由で逆引き
                            // した series_id を渡すことで「シリーズ専用テンプレ」を正しく解決させる。
                            int? seriesIdForResolve;
                            if (_currentCredit?.ScopeKind == "SERIES")
                            {
                                seriesIdForResolve = _currentCredit?.SeriesId;
                            }
                            else if (_currentCredit?.EpisodeId is int eid && eid > 0)
                            {
                                // 軽量に逆引き（EpisodesRepository に GetByIdAsync が無いため
                                // 直接生 SQL で series_id を取得）
                                await using var conn = await _factory.CreateOpenedAsync();
                                seriesIdForResolve = await Dapper.SqlMapper.ExecuteScalarAsync<int?>(conn,
                                    new Dapper.CommandDefinition(
                                        "SELECT series_id FROM episodes WHERE episode_id = @eid LIMIT 1;",
                                        new { eid }));
                            }
                            else
                            {
                                seriesIdForResolve = null;
                            }
                            var tpl = await _roleTemplatesRepo.ResolveAsync(role.RoleCode!, seriesIdForResolve);
                            int columns = ExtractThemeSongsColumns(tpl?.FormatTemplate);
                            if (columns >= 2) roleNote = $"  [横 {columns} カラム表示指定]";
                        }

                        var roleNode = new TreeNode($"📋 Role: {roleName}  (order {roleDisplayIndex}){roleNote}")
                        {
                            Tag = new NodeTag(NodeKind.CardRole, draftRole.CurrentId, draftRole)
                        };
                        roleDisplayIndex++;

                        // 主題歌役職の場合：episode_theme_songs から楽曲情報を引いて、楽曲サブノードを差し込む。
                        if (isThemeSongRole && _currentCredit?.ScopeKind == "EPISODE" && _currentCredit.EpisodeId is int epId)
                        {
                            await AddThemeSongVirtualNodesAsync(roleNode, epId, role.RoleCode ?? "");
                        }

                        int blockDisplayIndex = 1;
                        foreach (var draftBlock in draftRole.Blocks.Where(b => b.State != DraftState.Deleted)
                                                                       .OrderBy(b => b.Entity.BlockSeq))
                        {
                            var block = draftBlock.Entity;
                            // ブロック内エントリ：Deleted を除外、is_broadcast_only ASC, entry_seq ASC で並べる
                            var entries = draftBlock.Entries
                                .Where(en => en.State != DraftState.Deleted)
                                .OrderBy(en => en.Entity.IsBroadcastOnly)
                                .ThenBy(en => en.Entity.EntrySeq)
                                .ToList();

                            // 先頭企業屋号 (leading_company_alias_id) が設定されていれば
                            // ブロックラベルに名前を併記する（連載役職などで「どの出版社か」が一目で分かるように）。
                            // 屋号名は LookupCache 経由で引き、設定なしなら何も表示しない。
                            string leadingLabel = "";
                            if (block.LeadingCompanyAliasId is int lcid)
                            {
                                string? lname = await _lookupCache.LookupCompanyAliasNameAsync(lcid);
                                if (!string.IsNullOrEmpty(lname)) leadingLabel = $"  先頭=「{lname}」";
                                else leadingLabel = $"  先頭=#{lcid}";
                            }

                            var blockNode = new TreeNode(
                                $"🔵 Block #{blockDisplayIndex}  ({block.ColCount} cols, {entries.Count} entries){leadingLabel}")
                            {
                                Tag = new NodeTag(NodeKind.Block, draftBlock.CurrentId, draftBlock)
                            };
                            // ブロック先頭屋号が Pending（負数 ID）なら、ブロックノード全体を赤色で警告表示。
                            // HTML プレビュー側の ⚠ 赤太字と意味論を揃える（TreeView は文字単位の色変えが
                            // 標準では出来ないためノード全体を塗る方針、ユーザー指定）。
                            if (block.LeadingCompanyAliasId is int lid && lid < 0)
                            {
                                blockNode.ForeColor = PendingNodeColor;
                            }
                            blockDisplayIndex++;

                            int displayIndex = 1;
                            foreach (var draftEntry in entries)
                            {
                                var entry = draftEntry.Entity;
                                string preview = await _lookupCache.BuildEntryPreviewAsync(entry);
                                string prefix = entry.EntryKind switch
                                {
                                    "PERSON"          => "🟢 [PERSON]         ",
                                    "CHARACTER_VOICE" => "🟣 [CHARACTER_VOICE]",
                                    "COMPANY"         => "🟠 [COMPANY]        ",
                                    "LOGO"            => "🟡 [LOGO]           ",
                                    "TEXT"            => "⚪ [TEXT]            ",
                                    _                 => "❓ [UNKNOWN]        "
                                };
                                var entryNode = new TreeNode($"{prefix} #{displayIndex}  {preview}")
                                {
                                    Tag = new NodeTag(NodeKind.Entry, draftEntry.CurrentId, draftEntry)
                                };
                                // Pending マスタを参照しているエントリは ForeColor を赤に。
                                if (HasPendingMasterId(entry))
                                {
                                    entryNode.ForeColor = PendingNodeColor;
                                }
                                blockNode.Nodes.Add(entryNode);
                                displayIndex++;
                            }
                            roleNode.Nodes.Add(blockNode);
                        }
                        groupNode.Nodes.Add(roleNode);
                    }
                    tierNode.Nodes.Add(groupNode);
                }
                cardNode.Nodes.Add(tierNode);
            }
            newRootNodes.Add(cardNode);
        }

        // ─── フェーズ 2: 同期セクションで treeStructure を一気に更新 ───
        treeStructure.BeginUpdate();
        try
        {
            treeStructure.Nodes.Clear();
            treeStructure.Nodes.AddRange(newRootNodes.ToArray());
            treeStructure.ExpandAll();

            // フェーズ 0 で取った折りたたみ状態を復元（Path が新ツリーで見つかるノードだけ）。
            // 再構築前に折りたたまれていた / なかったが新規追加された場合は ExpandAll 既定のまま開いた状態。
            if (savedCollapsedKeys.Count > 0)
            {
                foreach (TreeNode root in treeStructure.Nodes)
                {
                    ApplyCollapsedKeys(root, savedCollapsedKeys);
                }
            }
            // 選択ノードを復元。見つからなければ何も選択しない。
            if (savedSelectedPath is not null)
            {
                var restoredSel = FindTreeNodeByPath(treeStructure, savedSelectedPath);
                if (restoredSel is not null)
                {
                    treeStructure.SelectedNode = restoredSel;
                }
            }
            // スクロール先頭位置（TopNode）を復元。
            // SelectedNode の自動 EnsureVisible で先頭位置がズレることがあるため、
            // TopNode 設定は SelectedNode 設定よりあとに置く。
            if (savedTopPath is not null)
            {
                var restoredTop = FindTreeNodeByPath(treeStructure, savedTopPath);
                if (restoredTop is not null)
                {
                    treeStructure.TopNode = restoredTop;
                }
            }
        }
        finally
        {
            treeStructure.EndUpdate();
        }

        // 未保存変更があれば背景色を黄色っぽく。
        // 視覚的に「保存待ち」を示すため、TreeView 全体の背景色を変える。
        ApplyDraftBackgroundColor();

        // 終盤の修正：
        // ① TreeView の表示更新を画面へ反映させるため Refresh を強制呼び出し。Clear/AddRange の直後は
        //    まれに描画が遅延することがあるため、保険として明示的に Invalidate + Update する。
        // ② 末尾で blockEditor / entryEditor を ClearAndDisable はしない。編集中に再構築が走った
        //    場合に右ペインの状態（_currentDraft）が消えてしまうため。右ペインの状態は選択ノード
        //    変更時の OnTreeNodeSelected で適切に切り替わるので、ここでクリアする必要はない。
        // ※ Application.DoEvents() は SelectedIndexChanged 連鎖発火など別バグの温床になり得るため
        //    入れない。本来の真因（OnCreditSelectedAsync 等の再入による _draftSession 多重生成）は
        //    各ハンドラの再入防止フラグで根本対処済み。
        treeStructure.Refresh();

        // Draft 編集のリアルタイムプレビュー反映。
        RequestPreviewRefresh();
    }

    /// <summary>
    /// 主題歌役職ノード <paramref name="roleNode"/> 配下に、<paramref name="episodeId"/> に
    /// 対応する <c>episode_theme_songs</c> 由来の楽曲サブノードを差し込む。
    /// 仮想ノード（<see cref="NodeKind.ThemeSongVirtual"/>）として作るため、Tag.Id には
    /// song_recording_id を入れるが、削除・並べ替え対象には含めない（UpdateButtonStates で抑止）。
    /// </summary>
    private async Task AddThemeSongVirtualNodesAsync(TreeNode roleNode, int episodeId, string roleCode)
    {
        // 役職コードに応じて、ツリーに表示する theme_kind を決める。
        // これは「その役職に紐付く楽曲だけを楽曲ノードとして表示する」ためのフィルタで、
        // テンプレ DSL の {THEME_SONGS:kind=...} と同じセマンティクスを持つ。
        // 既知の主題歌役職コードに該当しない場合は OP/ED/INSERT 全部を表示するフォールバック。
        // INSERT_SONG と INSERT_SONGS_NONCREDITED は同じ INSERT を表示する（本来運用上は
        // 一方だけ置く前提だが、両方置かれた場合は両方ともに楽曲を表示してユーザー判断を尊重）。
        IReadOnlyList<string> kinds = roleCode switch
        {
            "THEME_SONG_OP"             => new[] { "OP" },
            "THEME_SONG_ED"             => new[] { "ED" },
            "THEME_SONG_OP_COMBINED"    => new[] { "OP", "ED" },
            "INSERT_SONG"               => new[] { "INSERT" },
            "INSERT_SONGS_NONCREDITED"  => new[] { "INSERT" },
            _                           => new[] { "OP", "ED", "INSERT" }
        };

        // ノンクレジット役職のときは楽曲ノードラベルに視認用マークを付けて、
        // 「これらは実放送ではクレジットされない」ことを一目でわかるようにする。
        bool isNoncredited = (roleCode == "INSERT_SONGS_NONCREDITED");

        // seq は劇中で流れた順を表す汎用カラム（OP/ED/INSERT を区別せず
        // エピソード単位の劇中順）。CHECK
        // 制約 ck_ets_op_ed_no_insert_seq は撤廃。並び順は ets.seq 単独でソート、
        // kinds パラメータはフィルタとしてのみ使う。同位置に既定行と本放送限定行が
        // あれば既定行（is_broadcast_only=0）を先に。
        // 構造化クレジット解決のため song_id も SELECT。
        string sql = $$"""
            SELECT
              ets.song_recording_id  AS SongRecordingId,
              ets.theme_kind         AS ThemeKind,
              ets.seq                AS Seq,
              ets.is_broadcast_only  AS IsBroadcastOnly,
              s.song_id              AS SongId,
              s.title                AS SongTitle,
              s.lyricist_name        AS LyricistName,
              s.composer_name        AS ComposerName,
              s.arranger_name        AS ArrangerName,
              sr.singer_name         AS SingerName,
              sr.variant_label       AS VariantLabel
            FROM episode_theme_songs ets
            JOIN song_recordings sr ON sr.song_recording_id = ets.song_recording_id
            JOIN songs           s  ON s.song_id           = sr.song_id
            WHERE ets.episode_id = @episodeId
              AND ets.theme_kind IN @kinds
            ORDER BY
              ets.seq,
              ets.is_broadcast_only;
            """;
        await using var conn = await _lookupCache.Factory.CreateOpenedAsync(default);
        var rows = (await Dapper.SqlMapper.QueryAsync<ThemeSongRowForTree>(
            conn, sql, new { episodeId, kinds })).ToList();

        // 構造化クレジット（song_credits / song_recording_singers）が
        // 存在する曲・録音は、それを優先表示文字列に展開してフリーテキスト列を上書きする。
        // 動作は ThemeSongsHandler（HTML プレビュー側）と完全に同等で、表示の整合性を保つ。
        // 主題歌は 1 エピソードあたり 2-4 件程度なので、行ごとの追加クエリで実用上問題ない。
        var songCreditsRepo = new SongCreditsRepository(_lookupCache.Factory);
        var recordingSingersRepo = new SongRecordingSingersRepository(_lookupCache.Factory);
        foreach (var r in rows)
        {
            if (r.SongId > 0)
            {
                string lyr = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Lyrics);
                if (!string.IsNullOrEmpty(lyr)) r.LyricistName = lyr;

                string cmp = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Composition);
                if (!string.IsNullOrEmpty(cmp)) r.ComposerName = cmp;

                string arr = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Arrangement);
                if (!string.IsNullOrEmpty(arr)) r.ArrangerName = arr;
            }
            if (r.SongRecordingId is int recId && recId > 0)
            {
                // VOCALS 役職を主題歌の歌い手として優先採用（CHORUS の併記は別途）。
                string sing = await recordingSingersRepo.GetDisplayStringAsync(recId, SongRecordingSingerRoles.Vocals);
                if (!string.IsNullOrEmpty(sing)) r.SingerName = sing;
            }
        }

        foreach (var r in rows)
        {
            string title = r.SongTitle ?? "(曲名未登録)";
            string variant = string.IsNullOrEmpty(r.VariantLabel) ? "" : $" [{r.VariantLabel}]";
            string broadcastMark = (r.IsBroadcastOnly == 1) ? "🎬[本放送限定] " : "";
            string noncreditedMark = isNoncredited ? "🚫[ノンクレジット] " : "";
            string detail = "";
            var detailParts = new List<string>();
            if (!string.IsNullOrEmpty(r.LyricistName)) detailParts.Add($"作詞:{r.LyricistName}");
            if (!string.IsNullOrEmpty(r.ComposerName)) detailParts.Add($"作曲:{r.ComposerName}");
            if (!string.IsNullOrEmpty(r.ArrangerName)) detailParts.Add($"編曲:{r.ArrangerName}");
            if (!string.IsNullOrEmpty(r.SingerName))   detailParts.Add($"うた:{r.SingerName}");
            if (detailParts.Count > 0) detail = "  [" + string.Join(" / ", detailParts) + "]";
            string label = $"📀 Song({r.ThemeKind}): {noncreditedMark}{broadcastMark}「{title}」{variant}{detail}";
            var node = new TreeNode(label)
            {
                Tag = new NodeTag(NodeKind.ThemeSongVirtual, r.SongRecordingId ?? 0, r),
                ForeColor = System.Drawing.SystemColors.GrayText
            };
            roleNode.Nodes.Add(node);
        }
    }

    /// <summary>役職テンプレ文字列から <c>{THEME_SONGS:columns=N}</c> の N 値を抽出する （ノードラベル注記用、見つからなければ 1）。</summary>
    private static int ExtractThemeSongsColumns(string? template)
    {
        if (string.IsNullOrEmpty(template)) return 1;
        // 雑な抽出：{THEME_SONGS:columns=N} に含まれる数値を読む
        int idx = template.IndexOf("THEME_SONGS", StringComparison.Ordinal);
        if (idx < 0) return 1;
        int colon = template.IndexOf(':', idx);
        int close = template.IndexOf('}', idx);
        if (colon < 0 || close < 0 || colon > close) return 1;
        string opts = template.Substring(colon + 1, close - colon - 1);
        // columns=N をスキャン
        var m = System.Text.RegularExpressions.Regex.Match(opts, @"columns\s*=\s*(\d+)");
        if (!m.Success) return 1;
        return int.TryParse(m.Groups[1].Value, out var n) && n >= 1 ? n : 1;
    }

    /// <summary>Tier 仮想ノードのキー（実体テーブル化に対応してリファクタ）。</summary>
    private sealed record TierKey(int CardId, int CardTierId, byte TierNo);

    /// <summary>Group 仮想ノードのキー（実体テーブル化に対応してリファクタ）。</summary>
    private sealed record GroupKey(int CardId, int CardTierId, byte TierNo, int CardGroupId, byte GroupNo);

    /// <summary>パスを文字列キー（Collect/Apply の HashSet に積むキー）に変換する。</summary>
    private static string EncodePathKey(IReadOnlyList<(NodeKind Kind, int Id)> path)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in path)
        {
            if (sb.Length > 0) sb.Append('/');
            sb.Append((int)seg.Kind);
            sb.Append(':');
            sb.Append(seg.Id);
        }
        return sb.ToString();
    }

    /// <summary>パスを辿って新ツリー上の対応ノードを引く。 同 CurrentId のノードが見つからなければ null（クレジット切替や削除でパスが消えたケース）。</summary>
    private static TreeNode? FindTreeNodeByPath(TreeView tree, IReadOnlyList<(NodeKind Kind, int Id)> path)
    {
        if (path.Count == 0) return null;
        TreeNodeCollection nodes = tree.Nodes;
        TreeNode? found = null;
        foreach (var seg in path)
        {
            found = null;
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is NodeTag tag && tag.Kind == seg.Kind && tag.Id == seg.Id)
                {
                    found = n;
                    break;
                }
            }
            if (found is null) return null;
            nodes = found.Nodes;
        }
        return found;
    }

    /// <summary>サブツリーを再帰的に走査して「折りたたまれているノード」のパスキーを集める。 葉ノード（子なし）は折りたたみ概念がないのでスキップ。</summary>
    private static void CollectCollapsedNodeKeys(TreeNode node, HashSet<string> output)
    {
        if (node.Nodes.Count > 0)
        {
            if (!node.IsExpanded)
            {
                output.Add(EncodePathKey(GetTreeNodePath(node)));
            }
            foreach (TreeNode child in node.Nodes)
            {
                CollectCollapsedNodeKeys(child, output);
            }
        }
    }

    /// <summary>サブツリーを再帰的に走査して、収集済み折りたたみキー集合に含まれるノードを <see cref="TreeNode.Collapse()"/> する。 既定（ExpandAll 後）の状態に対して「以前折りたたまれていたものだけ閉じ直す」差分適用。</summary>
    private static void ApplyCollapsedKeys(TreeNode node, HashSet<string> collapsedKeys)
    {
        if (node.Nodes.Count > 0)
        {
            string key = EncodePathKey(GetTreeNodePath(node));
            if (collapsedKeys.Contains(key))
            {
                node.Collapse();
            }
            foreach (TreeNode child in node.Nodes)
            {
                ApplyCollapsedKeys(child, collapsedKeys);
            }
        }
    }
}
