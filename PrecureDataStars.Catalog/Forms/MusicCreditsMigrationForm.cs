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

        InitializeComponent();

        // データソースバインド
        gridMatches.DataSource = _matches;

        // イベント
        Load += async (_, __) => await LoadSeriesAsync();
        btnPickAlias.Click += async (_, __) => await OnPickAliasAsync();
        btnSearch.Click += async (_, __) => await OnSearchAsync();
        btnSelectAll.Click += (_, __) => SetAllChecked(true);
        btnDeselectAll.Click += (_, __) => SetAllChecked(false);
        btnMigrate.Click += async (_, __) => await OnMigrateAsync();
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
    /// </summary>
    private async Task SearchSongColumnAsync(
        System.Data.Common.DbConnection conn,
        int? filterSeriesId, string aliasName, string? aliasOverride,
        string column, string columnLabel, MatchKind kind)
    {
        // SQL インジェクション防止のため column はホワイトリスト由来の文字列のみを受け付ける運用。
        string seriesClause = filterSeriesId.HasValue ? "AND s.series_id = @SeriesId" : "";
        string sql = $"""
            SELECT s.song_id AS Id, s.title AS Title, s.{column} AS CurrentText
            FROM songs s
            WHERE s.{column} IS NOT NULL
              AND s.{column} <> ''
              AND (
                    REPLACE(REPLACE(s.{column}, ' ', ''), '　', '') = @AliasName
                    OR (@AliasOverride IS NOT NULL AND s.{column} = @AliasOverride)
                  )
              {seriesClause}
            ORDER BY s.song_id;
            """;

        var rows = await conn.QueryAsync<(int Id, string Title, string CurrentText)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride }));

        foreach (var (id, title, current) in rows)
        {
            // 既に同役の構造化行があればスキップ表示（重複登録防止）
            bool already = await ExistsSongCreditAsync(conn, id, MapKindToSongRole(kind));
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
              {seriesClause}
            ORDER BY sr.song_recording_id;
            """;

        var rows = await conn.QueryAsync<(int RecId, string CurrentText, string Title, int SongId)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride }));

        foreach (var (recId, current, title, songId) in rows)
        {
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
              {seriesClause}
            ORDER BY bc.series_id, bc.m_no_detail;
            """;

        var rows = await conn.QueryAsync<(int SeriesId, string MNoDetail, string? Title, string CurrentText)>(
            new CommandDefinition(sql, new { SeriesId = filterSeriesId, AliasName = aliasName, AliasOverride = aliasOverride }));

        foreach (var (seriesId, mno, title, current) in rows)
        {
            bool already = await ExistsBgmCueCreditAsync(conn, seriesId, mno, MapKindToBgmRole(kind));
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

    private static SongCreditRole MapKindToSongRole(MatchKind k) => k switch
    {
        MatchKind.SongLyricist => SongCreditRole.Lyricist,
        MatchKind.SongComposer => SongCreditRole.Composer,
        MatchKind.SongArranger => SongCreditRole.Arranger,
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private static BgmCueCreditRole MapKindToBgmRole(MatchKind k) => k switch
    {
        MatchKind.BgmComposer => BgmCueCreditRole.Composer,
        MatchKind.BgmArranger => BgmCueCreditRole.Arranger,
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private static async Task<bool> ExistsSongCreditAsync(System.Data.Common.DbConnection conn, int songId, SongCreditRole role)
    {
        string roleStr = role switch { SongCreditRole.Lyricist => "LYRICIST", SongCreditRole.Composer => "COMPOSER", SongCreditRole.Arranger => "ARRANGER", _ => "" };
        const string sql = "SELECT COUNT(*) FROM song_credits WHERE song_id = @SongId AND credit_role = @Role;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { SongId = songId, Role = roleStr })) > 0;
    }

    private static async Task<bool> ExistsSingerStructuredAsync(System.Data.Common.DbConnection conn, int recordingId)
    {
        const string sql = "SELECT COUNT(*) FROM song_recording_singers WHERE song_recording_id = @Id;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Id = recordingId })) > 0;
    }

    private static async Task<bool> ExistsBgmCueCreditAsync(System.Data.Common.DbConnection conn, int seriesId, string mno, BgmCueCreditRole role)
    {
        string roleStr = role == BgmCueCreditRole.Composer ? "COMPOSER" : "ARRANGER";
        const string sql = "SELECT COUNT(*) FROM bgm_cue_credits WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail AND credit_role = @Role;";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { SeriesId = seriesId, MNoDetail = mno, Role = roleStr })) > 0;
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
                                    Role = t.Kind == MatchKind.SongLyricist ? "LYRICIST"
                                         : t.Kind == MatchKind.SongComposer ? "COMPOSER" : "ARRANGER",
                                    AliasId = aliasId,
                                    By = updatedBy
                                }, transaction: tx));
                            inserted++;
                            break;

                        case MatchKind.RecordingSinger:
                            await conn.ExecuteAsync(new CommandDefinition(
                                """
                                INSERT INTO song_recording_singers
                                  (song_recording_id, singer_seq, billing_kind,
                                   person_alias_id, character_alias_id, voice_person_alias_id,
                                   slash_person_alias_id, slash_character_alias_id,
                                   preceding_separator, affiliation_text, notes,
                                   created_by, updated_by)
                                VALUES
                                  (@RecId, 1, 'PERSON',
                                   @AliasId, NULL, NULL,
                                   NULL, NULL,
                                   NULL, NULL, NULL,
                                   @By, @By);
                                """,
                                new { RecId = t.Id, AliasId = aliasId, By = updatedBy }, transaction: tx));
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
                                    Role = t.Kind == MatchKind.BgmComposer ? "COMPOSER" : "ARRANGER",
                                    AliasId = aliasId,
                                    By = updatedBy
                                }, transaction: tx));
                            inserted++;
                            break;
                    }
                }

                await tx.CommitAsync();
                lblStatus.Text = $"{inserted} 件を反映しました。再検索すると完了行は除外表示されます。";
                MessageBox.Show($"{inserted} 件を反映しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _matches.Clear();
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
        }
        finally { Cursor = Cursors.Default; }
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
