
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapper;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 音楽クレジット名寄せ移行フォーム（v1.2.3 新設）。
/// <para>
/// 既存のフリーテキスト列（<c>songs.lyricist_name</c>, <c>songs.composer_name</c>,
/// <c>songs.arranger_name</c>, <c>song_recordings.singer_name</c>,
/// <c>bgm_cues.composer_name</c>, <c>bgm_cues.arranger_name</c>）と <c>person_aliases</c> の
/// <b>完全一致</b>を引き、選択行を構造化クレジット表
/// （<c>song_credits</c> / <c>song_recording_singers</c> / <c>bgm_cue_credits</c>）に
/// 手動で 1 名義ずつ <b>トランザクション一括</b>で移行するためのツール。
/// </para>
/// <para>
/// 自動移行は行わない（連名・ユニット・キャラ(CV) 等は機械的な分解が困難なため、運用者が
/// 1 名義ずつ確認しながら移行する）。フリーテキスト列はそのまま温存する（バックアップ）。
/// </para>
/// <para>
/// 構造化テーブル側で連名対応にしているが、本フォームの一括反映では <c>credit_seq=1</c> の
/// 単一行のみを INSERT する（連名の作り込みは <c>SongsEditorForm</c> や <c>BgmCuesEditorForm</c>
/// 側のクレジットグリッドで行う前提）。なお、対象行に既に同役の構造化クレジット行が
/// ある場合は二重 INSERT を避けるためスキップする。
/// </para>
/// </summary>
public partial class MusicCreditsMigrationForm : Form
{
    private readonly IConnectionFactory _factory;
    private readonly SeriesRepository _seriesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly BgmCueCreditsRepository _bgmCueCreditsRepo;

    /// <summary>選択中の対象名義（picker 結果）。</summary>
    private PersonAlias? _selectedAlias;

    /// <summary>検索結果の行コレクション（DataGridView にバインドする）。</summary>
    private readonly BindingList<MatchRow> _matches = new();

    /// <summary>
    /// v1.3.0 ブラッシュアップ stage 16 Phase 3：未マッチング名義からの新規 alias 登録機能のために
    /// PersonsRepository を保持する。コンストラクタで factory から組み立てる
    /// （既存の SongCreditsRepository 等と同じスタイル）。
    /// </summary>
    private readonly PersonsRepository _personsRepo;

    public MusicCreditsMigrationForm(
        IConnectionFactory factory,
        SeriesRepository seriesRepo,
        PersonAliasesRepository personAliasesRepo,
        SongCreditsRepository songCreditsRepo,
        SongRecordingSingersRepository songRecordingSingersRepo,
        BgmCueCreditsRepository bgmCueCreditsRepo)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _songCreditsRepo = songCreditsRepo ?? throw new ArgumentNullException(nameof(songCreditsRepo));
        _songRecordingSingersRepo = songRecordingSingersRepo ?? throw new ArgumentNullException(nameof(songRecordingSingersRepo));
        _bgmCueCreditsRepo = bgmCueCreditsRepo ?? throw new ArgumentNullException(nameof(bgmCueCreditsRepo));
        // Phase 3 で追加：PersonsRepository は同じ factory から組み立てる。
        _personsRepo = new PersonsRepository(factory);

        InitializeComponent();

        // データソースバインド
        gridMatches.DataSource = _matches;

        // イベント
        Load += async (_, __) =>
        {
            await LoadSeriesAsync();
            // Phase 3：未マッチング名義一覧をロード（時間がかかる場合があるが
            // 数千件オーダーまでは現実的に問題ない範囲。最初の表示時だけ走らせる）。
            await LoadUnmatchedAsync();
        };
        btnPickAlias.Click += async (_, __) => await OnPickAliasAsync();
        btnSearch.Click += async (_, __) => await OnSearchAsync();
        btnSelectAll.Click += (_, __) => SetAllChecked(true);
        btnDeselectAll.Click += (_, __) => SetAllChecked(false);
        btnMigrate.Click += async (_, __) => await OnMigrateAsync();

        // Phase 3 追加：未マッチング名義行のイベント
        btnRegisterFromUnmatched.Click += async (_, __) => await OnRegisterFromUnmatchedAsync();
        btnReloadUnmatched.Click += async (_, __) => await LoadUnmatchedAsync();
    }

    /// <summary>シリーズフィルタを初期化。</summary>
    private async Task LoadSeriesAsync()
    {
        try
        {
            var series = (await _seriesRepo.GetAllAsync()).ToList();
            var items = new List<SeriesItem> { new SeriesItem(null, "(全て)") };
            items.AddRange(series.Select(s => new SeriesItem(s.SeriesId, s.Title)));
            cboSeriesFilter.DisplayMember = "Label";
            cboSeriesFilter.ValueMember = "Id";
            cboSeriesFilter.DataSource = items;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 未マッチング名義をドロップダウンに読み込む（v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// <para>
    /// 「未マッチング」の正しい定義：3 棚のいずれかのテキスト列に値が入っているのに、対応する
    /// 構造化クレジット行（song_credits / song_recording_singers / bgm_cue_credits）が
    /// まだ作られていない、というケース。person_aliases に登録済みかどうかは判定に使わない
    /// （登録済みでも構造化が無いケースは全曲対する移行が未完了なので、未マッチングとして拾う）。
    /// </para>
    /// <para>
    /// 各列ごとの「対応する構造化行の有無」判定：
    /// </para>
    /// <list type="bullet">
    ///   <item><description>songs.lyricist_name → song_credits (song_id, credit_role='LYRICS')</description></item>
    ///   <item><description>songs.composer_name → song_credits (song_id, credit_role='COMPOSITION')</description></item>
    ///   <item><description>songs.arranger_name → song_credits (song_id, credit_role='ARRANGEMENT')</description></item>
    ///   <item><description>song_recordings.singer_name → song_recording_singers (song_recording_id, role_code='VOCALS')</description></item>
    ///   <item><description>bgm_cues.composer_name → bgm_cue_credits (series_id, m_no_detail, credit_role='COMPOSITION')</description></item>
    ///   <item><description>bgm_cues.arranger_name → bgm_cue_credits (series_id, m_no_detail, credit_role='ARRANGEMENT')</description></item>
    /// </list>
    /// <para>
    /// 並び順：song_recordings 起点で、各テキストが「最初に紐付く song_recording_id」昇順。
    /// bgm_cues 由来は ORDER BY 末尾に集める（bgm のテキストは通常クレジット側の「音楽」役職で
    /// 表示されるため、移行優先度は低い）。
    /// </para>
    /// </summary>
    private async Task LoadUnmatchedAsync()
    {
        try
        {
            // 既存選択を保持しておき、再ロード後も同じ値を選択し直す（再読込ボタン用途）。
            string? prev = cboUnmatched.SelectedItem as string;

            // コレーション衝突対策：3 棚のテキスト列はテーブル既定の utf8mb4_0900_ai_ci を継承して
            // いるが、person_aliases 側は utf8mb4_ja_0900_as_cs_ks（日本語の accent/case sensitive）で
            // 定義されている。WHERE 句の '=' が両者を直接比較すると Error 1267 で弾かれるため、
            // テキスト側に COLLATE を明示して person_aliases 側に揃える。
            //
            // 並び順：song_recordings 起点で MIN(song_recording_id) を sort_key に。
            // bgm 由来は sort_key=NULL → 末尾に集める。
            const string sql = """
                WITH src AS (
                  -- songs.lyricist_name： 同 song_id × credit_role='LYRICS' の song_credits が
                  -- 無い song を未マッチングとして拾う。
                  SELECT TRIM(s.lyricist_name) COLLATE utf8mb4_ja_0900_as_cs_ks AS name,
                         (SELECT MIN(sr2.song_recording_id) FROM song_recordings sr2 WHERE sr2.song_id = s.song_id) AS sort_key
                    FROM songs s
                   WHERE s.lyricist_name IS NOT NULL
                     AND TRIM(s.lyricist_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM song_credits sc
                        WHERE sc.song_id = s.song_id
                          AND sc.credit_role = 'LYRICS'
                     )
                  UNION ALL
                  -- songs.composer_name → song_credits with credit_role='COMPOSITION'
                  SELECT TRIM(s.composer_name) COLLATE utf8mb4_ja_0900_as_cs_ks,
                         (SELECT MIN(sr2.song_recording_id) FROM song_recordings sr2 WHERE sr2.song_id = s.song_id)
                    FROM songs s
                   WHERE s.composer_name IS NOT NULL
                     AND TRIM(s.composer_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM song_credits sc
                        WHERE sc.song_id = s.song_id
                          AND sc.credit_role = 'COMPOSITION'
                     )
                  UNION ALL
                  -- songs.arranger_name → song_credits with credit_role='ARRANGEMENT'
                  SELECT TRIM(s.arranger_name) COLLATE utf8mb4_ja_0900_as_cs_ks,
                         (SELECT MIN(sr2.song_recording_id) FROM song_recordings sr2 WHERE sr2.song_id = s.song_id)
                    FROM songs s
                   WHERE s.arranger_name IS NOT NULL
                     AND TRIM(s.arranger_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM song_credits sc
                        WHERE sc.song_id = s.song_id
                          AND sc.credit_role = 'ARRANGEMENT'
                     )
                  UNION ALL
                  -- song_recordings.singer_name → song_recording_singers (song_recording_id, role_code='VOCALS')
                  SELECT TRIM(sr.singer_name) COLLATE utf8mb4_ja_0900_as_cs_ks,
                         sr.song_recording_id
                    FROM song_recordings sr
                   WHERE sr.singer_name IS NOT NULL
                     AND TRIM(sr.singer_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM song_recording_singers srs
                        WHERE srs.song_recording_id = sr.song_recording_id
                          AND srs.role_code = 'VOCALS'
                     )
                  UNION ALL
                  -- bgm_cues.composer_name → bgm_cue_credits (series_id, m_no_detail, credit_role='COMPOSITION')
                  SELECT TRIM(b.composer_name) COLLATE utf8mb4_ja_0900_as_cs_ks, NULL
                    FROM bgm_cues b
                   WHERE b.composer_name IS NOT NULL
                     AND TRIM(b.composer_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM bgm_cue_credits bcc
                        WHERE bcc.series_id   = b.series_id
                          AND bcc.m_no_detail = b.m_no_detail
                          AND bcc.credit_role = 'COMPOSITION'
                     )
                  UNION ALL
                  -- bgm_cues.arranger_name → bgm_cue_credits (series_id, m_no_detail, credit_role='ARRANGEMENT')
                  SELECT TRIM(b.arranger_name) COLLATE utf8mb4_ja_0900_as_cs_ks, NULL
                    FROM bgm_cues b
                   WHERE b.arranger_name IS NOT NULL
                     AND TRIM(b.arranger_name) <> ''
                     AND NOT EXISTS (
                       SELECT 1 FROM bgm_cue_credits bcc
                        WHERE bcc.series_id   = b.series_id
                          AND bcc.m_no_detail = b.m_no_detail
                          AND bcc.credit_role = 'ARRANGEMENT'
                     )
                )
                SELECT name FROM (
                  -- 同テキストが複数行（複数列・複数曲）に出ても 1 行に集約。代表 sort_key は最小値。
                  SELECT name, MIN(sort_key) AS sort_key
                    FROM src
                   GROUP BY name
                ) grouped
                ORDER BY (sort_key IS NULL), sort_key, name;
                """;

            // ConfigureAwait(false) を付けると await 後の続きがワーカースレッドで動き、
            // 直後の UI 操作（cboUnmatched.BeginUpdate 等）でクロススレッド例外になるため付けない。
            await using var conn = await _factory.CreateOpenedAsync();
            var rows = (await conn.QueryAsync<string>(new CommandDefinition(sql))).ToList();

            cboUnmatched.BeginUpdate();
            cboUnmatched.DataSource = null;
            cboUnmatched.Items.Clear();
            cboUnmatched.Items.AddRange(rows.Cast<object>().ToArray());
            // 再選択
            if (prev is not null && cboUnmatched.Items.Contains(prev))
            {
                cboUnmatched.SelectedItem = prev;
            }
            else if (cboUnmatched.Items.Count > 0)
            {
                cboUnmatched.SelectedIndex = 0;
            }
            cboUnmatched.EndUpdate();

            lblStatus.Text = $"未マッチング名義: {rows.Count} 件（構造化クレジット未紐付け、song_recordings 経由の登場順）";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「選択中の名義を新規 alias として登録...」ボタンの処理（v1.3.0 ブラッシュアップ stage 16 Phase 3）。
    /// 選択中の未マッチングテキストに対し：
    /// <list type="bullet">
    ///   <item><description>
    ///     person_aliases に同名（name または display_text_override）が既に登録されていれば、
    ///     ダイアログを開かずにその alias を <c>_selectedAlias</c> にセットし、そのまま
    ///     ワンストップ移行を実行する（重複登録の防止 + 操作削減）。
    ///   </description></item>
    ///   <item><description>
    ///     登録されていなければ、<see cref="UnmatchedAliasRegisterDialog"/> を開いて
    ///     person + alias を新規登録し、その後ワンストップ移行を実行する。
    ///   </description></item>
    /// </list>
    /// いずれの分岐でも、最後に未マッチング一覧を再ロードする（移行成功で当該テキストが
    /// 構造化済みになって一覧から消える、または、登録のみ成功で一覧表示が変わる）。
    /// </summary>
    private async Task OnRegisterFromUnmatchedAsync()
    {
        if (cboUnmatched.SelectedItem is not string sourceText || string.IsNullOrWhiteSpace(sourceText))
        {
            MessageBox.Show(this, "未マッチング名義が選択されていません。", "未選択",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 既存 alias 検出：person_aliases に同名（name または display_text_override）が既に登録済みなら、
        // ダイアログを開かずにその alias を _selectedAlias にセット → ワンストップ移行へ。
        // FindByExactNameAsync は accent/case sensitive なコレーションでの完全一致を返す。
        var existing = await _personAliasesRepo.FindByExactNameAsync(sourceText);
        if (existing.Count >= 1)
        {
            // 同名 alias が複数件あるケースは稀（運用ミスや誤登録）だが、Phase 3 では
            // 「最古の alias_id を採用」というシンプル戦略を取る（FindByExactNameAsync が ORDER BY alias_id）。
            // どの alias を使うかは運用者が事後に person_alias 編集 UI で選び直せば良い。
            var aliasToUse = existing[0];
            _selectedAlias = aliasToUse;
            txtAliasDisplay.Text = aliasToUse.GetDisplayName();
            lblAliasIdValue.Text = aliasToUse.AliasId.ToString();
            lblAliasIdValue.ForeColor = SystemColors.ControlText;

            string note = existing.Count == 1
                ? $"既存 alias_id={aliasToUse.AliasId} を使用してワンストップ移行します。"
                : $"同名 alias が {existing.Count} 件あります。最古（alias_id={aliasToUse.AliasId}）を使用してワンストップ移行します。";
            lblStatus.Text = note;

            await RunOneStopMigrationAsync();
            await LoadUnmatchedAsync();
            return;
        }

        // 未登録：ダイアログで新規登録 → ワンストップ移行 → 未マッチング再ロード。
        using var dlg = new UnmatchedAliasRegisterDialog(_personsRepo, _personAliasesRepo, sourceText);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.CreatedAliasId <= 0) return;

        // 下段の「対象名義」セレクトに自動セット（運用者の次の検索操作をスムーズに）。
        // 新規作成された alias を Repository から引き直して、_selectedAlias に流し込む。
        var created = await _personAliasesRepo.GetByIdAsync(dlg.CreatedAliasId);
        if (created is not null)
        {
            _selectedAlias = created;
            txtAliasDisplay.Text = created.GetDisplayName();
            lblAliasIdValue.Text = created.AliasId.ToString();
            lblAliasIdValue.ForeColor = SystemColors.ControlText;
        }

        // v1.3.0 ブラッシュアップ stage 16 Phase 3：ワンストップ自動移行。
        // alias 登録だけ済ませて手動でフィルタを設定し直すのは UX が悪いので、
        // ここで「全シリーズ + 全 6 列」で検索 → 全選択 → 構造化テーブル INSERT までを
        // 一気に流してしまう。確認ダイアログは抑止し、完了通知は lblStatus に表示する。
        await RunOneStopMigrationAsync();

        // 未マッチング一覧を再ロード（登録 + 移行で該当テキストが消える）。
        await LoadUnmatchedAsync();
        // lblStatus は RunOneStopMigrationAsync が「alias_id=X を登録し、N 件を自動移行しました」
        // のような完了通知を既にセットしているので、ここでは上書きしない。
    }

    /// <summary>名義 picker を開いて選択結果を保持する。</summary>
    private async Task OnPickAliasAsync()
    {
        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        try
        {
            var alias = await _personAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
            if (alias is null) return;

            _selectedAlias = alias;
            txtAliasDisplay.Text = alias.GetDisplayName();
            lblAliasIdValue.Text = alias.AliasId.ToString();
            lblAliasIdValue.ForeColor = SystemColors.ControlText;
            lblStatus.Text = $"対象名義: {alias.GetDisplayName()} (alias_id={alias.AliasId})。「検索」を押してください。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// チェックされた対象列について、フリーテキストの完全一致行を検索してグリッドに表示する。
    /// 一致対象は <see cref="PersonAlias.Name"/> と
    /// <see cref="PersonAlias.DisplayTextOverride"/> の両方（OR）。
    /// </summary>
    private async Task OnSearchAsync()
    {
        if (_selectedAlias is null)
        {
            MessageBox.Show("先に対象名義を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _matches.Clear();
        lblStatus.Text = "検索中...";
        Cursor = Cursors.WaitCursor;
        try
        {
            int? filterSeriesId = cboSeriesFilter.SelectedValue as int?;
            string aliasName = _selectedAlias.Name;
            string? aliasOverride = string.IsNullOrEmpty(_selectedAlias.DisplayTextOverride) ? null : _selectedAlias.DisplayTextOverride;

            // v1.2.3 修正：person_aliases.name は姓と名の間に半角/全角スペースが入って格納される運用、
            // 一方 songs.lyricist_name 等のフリーテキストはスペース無しで格納される運用のため、
            // exact match で取りこぼしが起きる。両辺を「半角SP・全角SP 全除去」に正規化して比較する。
            // SQL 側でも REPLACE() を二重適用してテキスト列を正規化するため、パラメータ側も
            // 同じ規則で先に詰めておく。display_text_override（ユニット長文表記）はスペースが
            // 表示上の意味を持つので正規化対象外で、これまで通り厳密一致のままにする。
            string aliasNameNormalized = aliasName.Replace(" ", "").Replace("\u3000", "");

            await using var conn = await _factory.CreateOpenedAsync();

            // 1) songs.lyricist_name
            if (chkLyricist.Checked)
                await SearchSongColumnAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride, "lyricist_name", "歌:作詞", MatchKind.SongLyricist);

            // 2) songs.composer_name
            if (chkComposer.Checked)
                await SearchSongColumnAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride, "composer_name", "歌:作曲", MatchKind.SongComposer);

            // 3) songs.arranger_name
            if (chkArranger.Checked)
                await SearchSongColumnAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride, "arranger_name", "歌:編曲", MatchKind.SongArranger);

            // 4) song_recordings.singer_name
            if (chkSinger.Checked)
                await SearchSingerAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride);

            // 5) bgm_cues.composer_name
            if (chkBgmComposer.Checked)
                await SearchBgmColumnAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride, "composer_name", "BGM:作曲", MatchKind.BgmComposer);

            // 6) bgm_cues.arranger_name
            if (chkBgmArranger.Checked)
                await SearchBgmColumnAsync(conn, filterSeriesId, aliasNameNormalized, aliasOverride, "arranger_name", "BGM:編曲", MatchKind.BgmArranger);

            lblStatus.Text = $"{_matches.Count} 件の一致行が見つかりました。チェックを確認のうえ「選択行を構造化テーブルに反映」を押してください。";
        }
        catch (Exception ex) { ShowError(ex); lblStatus.Text = "検索失敗。"; }
        finally { Cursor = Cursors.Default; }
    }

    /// <summary>
    /// songs.{col} の完全一致（スペース正規化込み）を検索する内部ヘルパ。
    /// 比較規則：name 側は両辺の半角SP・全角SP を除去して一致判定（運用差吸収）。
    /// display_text_override 側はユニット長文表記の意味を保つため厳密一致（無加工）。
    /// <para>
    /// v1.3.0 ブラッシュアップ stage 16 Phase 3 hotfix12：既に対応する構造化クレジット行が
    /// 存在する song（移行済み）はグリッドに出さないよう SQL の NOT EXISTS で除外する。
    /// 旧実装は表示はするがチェック外れの状態にする方式だったが、運用者にとって「既に処理済み」
    /// と「未処理」が混ざって紛らわしいため、未処理のみ表示する方針に変更した。
    /// 二重 INSERT 防止の二段ガード（コード側 ExistsSongCreditAsync）は残す。
    /// </para>
    /// </summary>
    private async Task SearchSongColumnAsync(
        System.Data.Common.DbConnection conn,
        int? filterSeriesId, string aliasName, string? aliasOverride,
        string column, string columnLabel, MatchKind kind)
    {
        // SQL インジェクション防止のため column はホワイトリスト由来の文字列のみを受け付ける運用。
        string seriesClause = filterSeriesId.HasValue ? "AND s.series_id = @SeriesId" : "";
        string roleCode = MapKindToSongRole(kind);
        string sql = $"""
            SELECT s.song_id AS Id, s.title AS Title, s.{column} AS CurrentText
            FROM songs s
            WHERE s.{column} IS NOT NULL
              AND s.{column} <> ''
              AND (
                    REPLACE(REPLACE(s.{column}, ' ', ''), '　', '') = @AliasName
                    OR (@AliasOverride IS NOT NULL AND s.{column} = @AliasOverride)
                  )
              AND NOT EXISTS (
                    SELECT 1 FROM song_credits sc
                     WHERE sc.song_id = s.song_id
                       AND sc.credit_role = @RoleCode
                  )
              {seriesClause}
            ORDER BY s.song_id;
            """;

        var rows = await conn.QueryAsync<(int Id, string Title, string CurrentText)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride, RoleCode = roleCode }));

        foreach (var (id, title, current) in rows)
        {
            // SQL で既移行を除外済みなので always false のはずだが、二段ガードとして残す。
            bool already = await ExistsSongCreditAsync(conn, id, roleCode);
            _matches.Add(new MatchRow
            {
                Checked = !already,
                Kind = kind,
                KindLabel = "歌（song）",
                IdDisplay = id.ToString(),
                Id = id,
                Title = title ?? "",
                ColumnLabel = columnLabel,
                CurrentText = current,
                AfterText = (_selectedAlias!.GetDisplayName()),
                AlreadyHasStructured = already
            });
        }
    }

    /// <summary>
    /// song_recordings.singer_name の完全一致（スペース正規化込み）検索。
    /// 録音と曲タイトルを JOIN で同時取得する。比較規則は SearchSongColumnAsync と同様。
    /// </summary>
    private async Task SearchSingerAsync(
        System.Data.Common.DbConnection conn,
        int? filterSeriesId, string aliasName, string? aliasOverride)
    {
        // v1.3.0 ブラッシュアップ stage 16 Phase 3 hotfix12：既に VOCALS 役職の song_recording_singers
        // 行が存在する song_recording は SQL で除外する。
        string seriesClause = filterSeriesId.HasValue ? "AND s.series_id = @SeriesId" : "";
        string sql = $"""
            SELECT sr.song_recording_id AS RecId,
                   sr.singer_name        AS CurrentText,
                   s.title               AS Title,
                   s.song_id             AS SongId
            FROM song_recordings sr
            JOIN songs s ON s.song_id = sr.song_id
            WHERE sr.singer_name IS NOT NULL
              AND sr.singer_name <> ''
              AND (
                    REPLACE(REPLACE(sr.singer_name, ' ', ''), '　', '') = @AliasName
                    OR (@AliasOverride IS NOT NULL AND sr.singer_name = @AliasOverride)
                  )
              AND NOT EXISTS (
                    SELECT 1 FROM song_recording_singers srs
                     WHERE srs.song_recording_id = sr.song_recording_id
                       AND srs.role_code = 'VOCALS'
                  )
              {seriesClause}
            ORDER BY sr.song_recording_id;
            """;

        var rows = await conn.QueryAsync<(int RecId, string CurrentText, string Title, int SongId)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride }));

        foreach (var (recId, current, title, songId) in rows)
        {
            // SQL で既移行を除外済みなので always false のはずだが、二段ガードとして残す。
            bool already = await ExistsSingerStructuredAsync(conn, recId);
            _matches.Add(new MatchRow
            {
                Checked = !already,
                Kind = MatchKind.RecordingSinger,
                KindLabel = "歌録音（recording）",
                IdDisplay = recId.ToString(),
                Id = recId,
                Title = $"{title}",
                ColumnLabel = "歌:歌唱",
                CurrentText = current,
                AfterText = _selectedAlias!.GetDisplayName(),
                AlreadyHasStructured = already
            });
        }
    }

    /// <summary>
    /// bgm_cues.{col} の完全一致（スペース正規化込み）検索。bgm_cues は (series_id, m_no_detail) 複合 PK。
    /// 比較規則は SearchSongColumnAsync と同様。
    /// </summary>
    private async Task SearchBgmColumnAsync(
        System.Data.Common.DbConnection conn,
        int? filterSeriesId, string aliasName, string? aliasOverride,
        string column, string columnLabel, MatchKind kind)
    {
        string seriesClause = filterSeriesId.HasValue ? "AND bc.series_id = @SeriesId" : "";
        string roleCode = MapKindToBgmRole(kind);
        string sql = $"""
            SELECT bc.series_id   AS SeriesId,
                   bc.m_no_detail AS MNoDetail,
                   bc.menu_title  AS Title,
                   bc.{column}    AS CurrentText
            FROM bgm_cues bc
            WHERE bc.{column} IS NOT NULL
              AND bc.{column} <> ''
              AND (
                    REPLACE(REPLACE(bc.{column}, ' ', ''), '　', '') = @AliasName
                    OR (@AliasOverride IS NOT NULL AND bc.{column} = @AliasOverride)
                  )
              AND NOT EXISTS (
                    SELECT 1 FROM bgm_cue_credits bcc
                     WHERE bcc.series_id   = bc.series_id
                       AND bcc.m_no_detail = bc.m_no_detail
                       AND bcc.credit_role = @RoleCode
                  )
              {seriesClause}
            ORDER BY bc.series_id, bc.m_no_detail;
            """;

        var rows = await conn.QueryAsync<(int SeriesId, string MNoDetail, string? Title, string CurrentText)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride, RoleCode = roleCode }));

        foreach (var (seriesId, mno, title, current) in rows)
        {
            // SQL で既移行を除外済みなので always false のはずだが、二段ガードとして残す。
            bool already = await ExistsBgmCueCreditAsync(conn, seriesId, mno, roleCode);
            _matches.Add(new MatchRow
            {
                Checked = !already,
                Kind = kind,
                KindLabel = "劇伴（bgm_cue）",
                IdDisplay = $"{seriesId}/{mno}",
                BgmSeriesId = seriesId,
                BgmMNoDetail = mno,
                Title = title ?? "",
                ColumnLabel = columnLabel,
                CurrentText = current,
                AfterText = _selectedAlias!.GetDisplayName(),
                AlreadyHasStructured = already
            });
        }
    }

    // v1.3.0 ブラッシュアップ続編：戻り値を string（roles.role_code）に変更。
    // MatchKind ごとに対応する song_credits.credit_role の値を返す。
    private static string MapKindToSongRole(MatchKind k) => k switch
    {
        MatchKind.SongLyricist => SongCreditRoles.Lyrics,
        MatchKind.SongComposer => SongCreditRoles.Composition,
        MatchKind.SongArranger => SongCreditRoles.Arrangement,
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    // v1.3.0 ブラッシュアップ続編：戻り値を string（roles.role_code）に変更。
    private static string MapKindToBgmRole(MatchKind k) => k switch
    {
        MatchKind.BgmComposer => BgmCueCreditRoles.Composition,
        MatchKind.BgmArranger => BgmCueCreditRoles.Arrangement,
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    // v1.3.0 ブラッシュアップ続編：role 引数を string に変更（DB 側も varchar+roles FK 化済み）。
    private static async Task<bool> ExistsSongCreditAsync(System.Data.Common.DbConnection conn, int songId, string role)
    {
        const string sql = "SELECT COUNT(*) FROM song_credits WHERE song_id = @SongId AND credit_role = @Role;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { SongId = songId, Role = role })) > 0;
    }

    private static async Task<bool> ExistsSingerStructuredAsync(System.Data.Common.DbConnection conn, int recordingId)
    {
        // v1.3.0 ブラッシュアップ続編：song_recording_singers に role_code 列が追加されたが、
        // ここでは VOCALS 役職に限定して既存判定する（Phase 3 の連名移行ツールが扱う想定の範囲）。
        const string sql = "SELECT COUNT(*) FROM song_recording_singers WHERE song_recording_id = @Id AND role_code = @Role;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Id = recordingId, Role = SongRecordingSingerRoles.Vocals })) > 0;
    }

    // v1.3.0 ブラッシュアップ続編：role 引数を string に変更。
    private static async Task<bool> ExistsBgmCueCreditAsync(System.Data.Common.DbConnection conn, int seriesId, string mno, string role)
    {
        const string sql = "SELECT COUNT(*) FROM bgm_cue_credits WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail AND credit_role = @Role;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { SeriesId = seriesId, MNoDetail = mno, Role = role })) > 0;
    }

    private void SetAllChecked(bool value)
    {
        foreach (var r in _matches)
        {
            // 既に構造化行がある場合はチェック ON しない（重複登録防止）
            if (value && r.AlreadyHasStructured) continue;
            r.Checked = value;
        }
        gridMatches.Refresh();
    }

    /// <summary>
    /// 選択行を構造化テーブルに一括 INSERT する。1 回のクリックで全行を 1 トランザクションにまとめる。
    /// 連名対応は seq=1 の単一行のみ（連名は SongsEditor / BgmCuesEditor で組む）。
    /// </summary>
    private async Task OnMigrateAsync()
    {
        if (_selectedAlias is null) return;
        var targets = _matches.Where(m => m.Checked && !m.AlreadyHasStructured).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("反映対象の行がありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dr = MessageBox.Show(
            $"選択された {targets.Count} 行を構造化テーブルに反映します。\n" +
            $"対象名義: {_selectedAlias.GetDisplayName()} (alias_id={_selectedAlias.AliasId})\n\n" +
            "・各行とも seq=1 の単一行として挿入します（連名は別途エディタで構築してください）\n" +
            "・歌唱クレジットは billing_kind=PERSON で挿入します（CHARACTER_WITH_CV はエディタで設定）\n" +
            "・元のフリーテキスト列はそのまま温存されます\n\n" +
            "実行しますか？",
            "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (dr != DialogResult.Yes) return;

        // v1.3.0 ブラッシュアップ stage 16 Phase 3：DB 処理本体は MigrateInternalAsync に切り出した。
        // OnMigrateAsync は手動移行ボタン用に確認・完了 MessageBox を担当する。
        int inserted = await MigrateInternalAsync(targets);
        if (inserted >= 0)
        {
            MessageBox.Show($"{inserted} 件を反映しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// 構造化テーブルへの INSERT 本体（v1.3.0 ブラッシュアップ stage 16 Phase 3 で抽出）。
    /// 確認・完了の MessageBox は呼び出し側の責務とする。Cursor / lblStatus はここで管理。
    /// 戻り値：反映件数（失敗時は -1、呼び出し側で完了 MessageBox の出し分けに使う）。
    /// </summary>
    /// <remarks>
    /// 切り出し前の OnMigrateAsync が一体で行っていた処理のうち、
    /// 純粋に DB に書き込む部分だけをここに持たせる。手動移行（OnMigrateAsync）と
    /// ワンストップ自動移行（RunOneStopMigrationAsync）の両方から再利用する。
    /// </remarks>
    private async Task<int> MigrateInternalAsync(List<MatchRow> targets)
    {
        if (_selectedAlias is null) return -1;

        Cursor = Cursors.WaitCursor;
        lblStatus.Text = "反映中...";
        try
        {
            await using var conn = await _factory.CreateOpenedAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                int aliasId = _selectedAlias.AliasId;
                string? updatedBy = Environment.UserName;
                int inserted = 0;

                foreach (var t in targets)
                {
                    switch (t.Kind)
                    {
                        case MatchKind.SongLyricist:
                        case MatchKind.SongComposer:
                        case MatchKind.SongArranger:
                            await conn.ExecuteAsync(new CommandDefinition(
                                """
                                INSERT INTO song_credits
                                  (song_id, credit_role, credit_seq, person_alias_id, preceding_separator, created_by, updated_by)
                                VALUES
                                  (@SongId, @Role, 1, @AliasId, NULL, @By, @By);
                                """,
                                new
                                {
                                    SongId = t.Id,
                                    // v1.3.0 ブラッシュアップ続編：DB の credit_role 値が
                                    // LYRICS/COMPOSITION/ARRANGEMENT に変わったので新値で INSERT。
                                    Role = t.Kind == MatchKind.SongLyricist ? SongCreditRoles.Lyrics
                                         : t.Kind == MatchKind.SongComposer ? SongCreditRoles.Composition
                                         : SongCreditRoles.Arrangement,
                                    AliasId = aliasId,
                                    By = updatedBy
                                }, transaction: tx));
                            inserted++;
                            break;

                        case MatchKind.RecordingSinger:
                            await conn.ExecuteAsync(new CommandDefinition(
                                """
                                INSERT INTO song_recording_singers
                                  (song_recording_id, role_code, singer_seq, billing_kind,
                                   person_alias_id, character_alias_id, voice_person_alias_id,
                                   slash_person_alias_id, slash_character_alias_id,
                                   preceding_separator, affiliation_text, notes,
                                   created_by, updated_by)
                                VALUES
                                  (@RecId, @Role, 1, 'PERSON',
                                   @AliasId, NULL, NULL,
                                   NULL, NULL,
                                   NULL, NULL, NULL,
                                   @By, @By);
                                """,
                                // v1.3.0 ブラッシュアップ続編：role_code 列が PK に含まれるようになったため必須。
                                // singer_name フリーテキストを移行する用途では VOCALS 役職で確定。
                                new { RecId = t.Id, Role = SongRecordingSingerRoles.Vocals, AliasId = aliasId, By = updatedBy }, transaction: tx));
                            inserted++;
                            break;

                        case MatchKind.BgmComposer:
                        case MatchKind.BgmArranger:
                            await conn.ExecuteAsync(new CommandDefinition(
                                """
                                INSERT INTO bgm_cue_credits
                                  (series_id, m_no_detail, credit_role, credit_seq, person_alias_id, preceding_separator, created_by, updated_by)
                                VALUES
                                  (@SeriesId, @MNoDetail, @Role, 1, @AliasId, NULL, @By, @By);
                                """,
                                new
                                {
                                    SeriesId = t.BgmSeriesId,
                                    MNoDetail = t.BgmMNoDetail,
                                    // v1.3.0 ブラッシュアップ続編：DB の credit_role 値が
                                    // COMPOSITION/ARRANGEMENT に変わったので新値で INSERT。
                                    Role = t.Kind == MatchKind.BgmComposer ? BgmCueCreditRoles.Composition : BgmCueCreditRoles.Arrangement,
                                    AliasId = aliasId,
                                    By = updatedBy
                                }, transaction: tx));
                            inserted++;
                            break;
                    }
                }

                await tx.CommitAsync();
                lblStatus.Text = $"{inserted} 件を反映しました。再検索すると完了行は除外表示されます。";
                _matches.Clear();
                return inserted;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
            lblStatus.Text = "反映失敗。";
            return -1;
        }
        finally { Cursor = Cursors.Default; }
    }

    /// <summary>
    /// alias 登録直後のワンストップ自動移行（v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// シリーズフィルタを「(全て)」、6 列全部 ON に強制した状態で検索 → 全選択 → 移行 INSERT を
    /// 連続実行する。元の UI 状態は finally で復元する。確認 MessageBox は出さない
    /// （ユーザーの「ワンストップで」要望に応えるため）。完了は lblStatus で通知。
    /// </summary>
    private async Task RunOneStopMigrationAsync()
    {
        if (_selectedAlias is null) return;

        // 元の UI 状態を保存（後で復元するため）。
        int savedSeriesIndex = cboSeriesFilter.SelectedIndex;
        bool savedLyr = chkLyricist.Checked;
        bool savedCmp = chkComposer.Checked;
        bool savedArr = chkArranger.Checked;
        bool savedSng = chkSinger.Checked;
        bool savedBgmCmp = chkBgmComposer.Checked;
        bool savedBgmArr = chkBgmArranger.Checked;

        try
        {
            // 「(全て)」シリーズ + 全 6 列を強制設定。
            // 未マッチング名義は全シリーズ横断で抽出されているので、対応する移行も全シリーズで行うのが自然。
            if (cboSeriesFilter.Items.Count > 0) cboSeriesFilter.SelectedIndex = 0;
            chkLyricist.Checked    = true;
            chkComposer.Checked    = true;
            chkArranger.Checked    = true;
            chkSinger.Checked      = true;
            chkBgmComposer.Checked = true;
            chkBgmArranger.Checked = true;

            // 検索 → 全選択 → 移行
            await OnSearchAsync();

            var targets = _matches.Where(m => m.Checked && !m.AlreadyHasStructured).ToList();
            if (targets.Count == 0)
            {
                lblStatus.Text = $"alias_id={_selectedAlias.AliasId} を登録しました。完全一致レコードは見つかりませんでした（自動移行 0 件）。";
                return;
            }

            int inserted = await MigrateInternalAsync(targets);
            if (inserted >= 0)
            {
                lblStatus.Text = $"alias_id={_selectedAlias.AliasId} を登録し、{inserted} 件を自動移行しました。";
            }
        }
        finally
        {
            // UI 状態を復元（移行成功でも失敗でも、ユーザーが見ていたフィルタを元に戻す）。
            if (savedSeriesIndex >= 0 && savedSeriesIndex < cboSeriesFilter.Items.Count)
                cboSeriesFilter.SelectedIndex = savedSeriesIndex;
            chkLyricist.Checked    = savedLyr;
            chkComposer.Checked    = savedCmp;
            chkArranger.Checked    = savedArr;
            chkSinger.Checked      = savedSng;
            chkBgmComposer.Checked = savedBgmCmp;
            chkBgmArranger.Checked = savedBgmArr;
        }
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>シリーズ ComboBox のバインド用 DTO。</summary>
    private sealed class SeriesItem
    {
        public int? Id { get; }
        public string Label { get; }
        public SeriesItem(int? id, string label) { Id = id; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>検索結果 1 行の種別タグ。</summary>
    private enum MatchKind
    {
        SongLyricist, SongComposer, SongArranger,
        RecordingSinger,
        BgmComposer, BgmArranger
    }

    /// <summary>DataGridView バインド用 DTO（v1.2.3 移行ツール内部）。</summary>
    private sealed class MatchRow
    {
        /// <summary>チェック状態（一括反映の対象選択）。</summary>
        public bool Checked { get; set; }
        public MatchKind Kind { get; set; }
        public string KindLabel { get; set; } = "";
        public string IdDisplay { get; set; } = "";
        /// <summary>song / song_recording の主キー。劇伴のときは無効。</summary>
        public int Id { get; set; }
        /// <summary>劇伴のとき使う複合 PK（series_id）。</summary>
        public int BgmSeriesId { get; set; }
        /// <summary>劇伴のとき使う複合 PK（m_no_detail）。</summary>
        public string BgmMNoDetail { get; set; } = "";
        public string Title { get; set; } = "";
        public string ColumnLabel { get; set; } = "";
        public string CurrentText { get; set; } = "";
        public string AfterText { get; set; } = "";
        /// <summary>同役の構造化行が既に存在するか（チェック OFF にする目印）。</summary>
        public bool AlreadyHasStructured { get; set; }
    }
}
