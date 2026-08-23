#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.AmazonPaApi;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 書籍管理フォーム。<c>books</c> とその付随テーブル（<c>book_series</c> / <c>book_genre_links</c> /
/// <c>book_credits</c>）に対する CRUD GUI。
/// <para>
/// 画面構成:
/// <list type="bullet">
///   <item>左: 書籍一覧（発売日昇順）＋書名・ISBN の絞り込みボックス</item>
///   <item>右: タブ 3 枚（基本情報 / シリーズ・ジャンル / クレジット）＋新規・保存・削除</item>
/// </list>
/// </para>
/// <para>
/// 紙と Kindle は 1 冊のレコードに束ねる。特装版のように Kindle 版が存在しない別商品は、
/// 通常版とは独立した書籍として登録する（同じ書名に「特装版」を付けて区別する運用）。
/// </para>
/// <para>
/// 表紙 URL は本フォームの「保存」では触らない（<see cref="BooksRepository.UpdateAsync"/> が
/// 画像列を対象外にしているため）。画像は Amazon 検索ダイアログで選んだ時点で専用経路から
/// 直接書き込む。ProductDiscsEditorForm と同じ流儀。
/// </para>
/// <para>
/// 紙の価格は Amazon から取り込まない方針（出品価格であって定価ではないため）なので、
/// 本フォームの「紙 税抜 / 紙 税込」が唯一の入力口になる。
/// </para>
/// </summary>
public partial class BooksEditorForm : Form
{
    private readonly BooksRepository _booksRepo;
    private readonly BookMastersRepository _mastersRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    private readonly SeriesRepository _seriesRepo;

    // 一覧の元データ（is_deleted=0 のみ）と、絞り込み後の表示行。
    private List<Book> _books = new();

    // マスタ類。フォーム表示時に 1 度だけ読んで使い回す。
    private List<BookGenre> _genres = new();
    private List<BookCreditRole> _creditRoles = new();
    private List<ProductCompany> _publishers = new();
    private List<Series> _seriesList = new();

    // クレジットタブの編集中行。保存時に ReplaceRelationsAsync へ丸ごと渡す。
    private readonly BindingSource _creditBinding = new();
    private List<CreditRow> _creditRows = new();

    /// <summary><see cref="BooksEditorForm"/> の新しいインスタンスを生成する。</summary>
    /// <param name="booksRepo">書籍リポジトリ。</param>
    /// <param name="mastersRepo">書籍マスタ（ジャンル・役職）リポジトリ。</param>
    /// <param name="productCompaniesRepo">出版社として引く商品社名マスタ。</param>
    /// <param name="seriesRepo">シリーズ紐付けの選択肢に使うシリーズリポジトリ。</param>
    public BooksEditorForm(
        BooksRepository booksRepo,
        BookMastersRepository mastersRepo,
        ProductCompaniesRepository productCompaniesRepo,
        SeriesRepository seriesRepo)
    {
        _booksRepo = booksRepo ?? throw new ArgumentNullException(nameof(booksRepo));
        _mastersRepo = mastersRepo ?? throw new ArgumentNullException(nameof(mastersRepo));
        _productCompaniesRepo = productCompaniesRepo ?? throw new ArgumentNullException(nameof(productCompaniesRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();
        SetupGridColumns();

        Load += (_, __) => InitializeSplitterLayout();
        Load += async (_, __) => await LoadMastersAsync();
        Load += async (_, __) => await ReloadAsync();

        gridItems.SelectionChanged += async (_, __) => await OnItemSelectedAsync();
        txtFilter.TextChanged += (_, __) => ApplyFilter();

        chkHasKindleDate.CheckedChanged += (_, __) => dtReleaseKindle.Enabled = chkHasKindleDate.Checked;
        clbGenres.ItemCheck += (_, __) => BeginInvoke(new Action(RefreshPrimaryGenreChoices));

        btnAmazonSearch.Click += async (_, __) => await SearchAmazonAsync();
        btnCreditAdd.Click += (_, __) => AddCreditRow();
        btnCreditRemove.Click += (_, __) => RemoveCreditRow();

        btnNew.Click += (_, __) => ClearForm();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnDelete.Click += async (_, __) => await DeleteAsync();
        btnReload.Click += async (_, __) => await ReloadAsync();
    }

    /// <summary><see cref="splitMain"/> のスプリッタ実値を ClientSize 確定後に設定する。 左 42% / 右 58% を初期値とし、一覧の書名が読める幅を確保する。</summary>
    private void InitializeSplitterLayout()
    {
        int usable = splitMain.Width - splitMain.SplitterWidth;
        if (usable < 2) return;

        int maxAllowedMin = Math.Max(25, usable / 3);
        splitMain.Panel1MinSize = Math.Min(260, maxAllowedMin);
        splitMain.Panel2MinSize = Math.Min(420, maxAllowedMin);

        int target = (int)(usable * 0.42);
        int min = splitMain.Panel1MinSize;
        int max = Math.Max(min, usable - splitMain.Panel2MinSize);
        splitMain.SplitterDistance = Math.Clamp(target, min, max);
    }

    /// <summary>左ペインの一覧列と、クレジットタブの編集列を定義する。</summary>
    private void SetupGridColumns()
    {
        gridItems.Columns.Clear();
        gridItems.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "BookId", HeaderText = "ID",
                DataPropertyName = nameof(BookRow.BookId), Width = 46,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "ReleaseDate", HeaderText = "発売日",
                DataPropertyName = nameof(BookRow.ReleaseDateLabel), Width = 86 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "書名",
                DataPropertyName = nameof(BookRow.Title),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            // 版の有無を「紙/電」の 1 文字ずつで示す。どちらが登録済みかを一覧で判別できるようにする。
            new DataGridViewTextBoxColumn { Name = "Editions", HeaderText = "版",
                DataPropertyName = nameof(BookRow.EditionsLabel), Width = 54,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } }
        });

        gridCredits.Columns.Clear();
        var roleCol = new DataGridViewComboBoxColumn
        {
            Name = "RoleCode", HeaderText = "役職",
            DataPropertyName = nameof(CreditRow.RoleCode), Width = 120,
            FlatStyle = FlatStyle.Flat
        };
        gridCredits.Columns.Add(roleCol);
        gridCredits.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PersonAliasId", HeaderText = "名義ID",
            DataPropertyName = nameof(CreditRow.PersonAliasIdText), Width = 70
        });
        gridCredits.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CreditText", HeaderText = "表記（マスタに無い相手・誌面表記が違う場合）",
            DataPropertyName = nameof(CreditRow.CreditText),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
    }

    /// <summary>ジャンル・役職・出版社・シリーズのマスタを読み込んで各コントロールへ流し込む。</summary>
    private async Task LoadMastersAsync()
    {
        try
        {
            _genres = (await _mastersRepo.GetGenresAsync()).ToList();
            _creditRoles = (await _mastersRepo.GetCreditRolesAsync()).ToList();
            _publishers = (await _productCompaniesRepo.GetAllAsync()).ToList();
            _seriesList = (await _seriesRepo.GetAllAsync()).ToList();

            // 出版社コンボ。先頭に「(未設定)」を置いて NULL を選べるようにする。
            cmbPublisher.DisplayMember = nameof(PublisherItem.Label);
            cmbPublisher.DataSource = new[] { new PublisherItem(null, "(未設定)") }
                .Concat(_publishers.Select(p => new PublisherItem(p.ProductCompanyId, p.NameJa)))
                .ToList();

            // 代表書影の採用ソース。値は books.cover_image_source に入るコード。
            cmbCoverSource.DisplayMember = nameof(CoverSourceItem.Label);
            cmbCoverSource.DataSource = new List<CoverSourceItem>
            {
                new(null, "(未選択)"),
                new("amazon_print", "紙"),
                new("amazon_kindle", "Kindle")
            };

            clbSeries.Items.Clear();
            foreach (var s in _seriesList) clbSeries.Items.Add(new SeriesItem(s));

            clbGenres.Items.Clear();
            foreach (var g in _genres) clbGenres.Items.Add(new GenreItem(g));

            // クレジット行の役職コンボの選択肢。表示は和名、値はコード。
            if (gridCredits.Columns["RoleCode"] is DataGridViewComboBoxColumn rc)
            {
                rc.DisplayMember = nameof(RoleItem.Label);
                rc.ValueMember = nameof(RoleItem.Code);
                rc.DataSource = _creditRoles.Select(r => new RoleItem(r.RoleCode, r.NameJa)).ToList();
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>DB から書籍一覧を取り直してグリッドへ反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            _books = (await _booksRepo.GetAllAsync()).ToList();
            ApplyFilter();
            ClearForm();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>絞り込みボックスの文字列で一覧を絞る。書名・読み・ISBN を対象にした部分一致。</summary>
    private void ApplyFilter()
    {
        string kw = txtFilter.Text?.Trim() ?? "";
        IEnumerable<Book> src = _books;
        if (kw.Length > 0)
        {
            src = src.Where(b =>
                b.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || (b.TitleKana?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
                || (b.Isbn13?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        gridItems.DataSource = null;
        gridItems.DataSource = src.Select(b => new BookRow(b)).ToList();
    }

    /// <summary>グリッド選択行の内容を右ペインへ展開する。付随テーブルはこのタイミングで引く。</summary>
    private async Task OnItemSelectedAsync()
    {
        if (gridItems.CurrentRow?.DataBoundItem is not BookRow row)
        {
            ClearForm();
            return;
        }

        try
        {
            var b = row.Inner;
            numId.Value = b.BookId;
            txtTitle.Text = b.Title;
            txtTitleKana.Text = b.TitleKana ?? "";
            txtTitleEn.Text = b.TitleEn ?? "";
            SelectPublisher(b.PublisherProductCompanyId);
            dtRelease.Value = b.ReleaseDate;
            chkHasKindleDate.Checked = b.ReleaseDateKindle.HasValue;
            dtReleaseKindle.Enabled = chkHasKindleDate.Checked;
            dtReleaseKindle.Value = b.ReleaseDateKindle ?? b.ReleaseDate;
            txtIsbn13.Text = b.Isbn13 ?? "";
            numPageCount.Value = b.PageCount ?? 0;
            txtTrimSize.Text = b.TrimSize ?? "";
            txtBindingText.Text = b.BindingText ?? "";
            numPriceExTax.Value = b.PriceExTax ?? 0;
            numPriceIncTax.Value = b.PriceIncTax ?? 0;
            numPriceKindle.Value = b.PriceKindleIncTax ?? 0;
            chkHasPrint.Checked = b.HasPrint;
            chkHasKindle.Checked = b.HasKindle;
            txtAsinPrint.Text = b.AmazonAsinPrint ?? "";
            txtAsinKindle.Text = b.AmazonAsinKindle ?? "";
            SelectCoverSource(b.CoverImageSource);
            chkCoverShowBoth.Checked = b.CoverImageShowBoth;
            txtOfficialUrl.Text = b.OfficialUrl ?? "";
            txtNotes.Text = b.Notes ?? "";

            // シリーズ・ジャンル・クレジットは書籍ごとに引く（一覧選択のたびに 3 クエリ）。
            var seriesIds = (await _booksRepo.GetSeriesLinksAsync(b.BookId)).Select(l => l.SeriesId).ToHashSet();
            for (int i = 0; i < clbSeries.Items.Count; i++)
                clbSeries.SetItemChecked(i, clbSeries.Items[i] is SeriesItem si && seriesIds.Contains(si.SeriesId));

            var genreLinks = await _booksRepo.GetGenreLinksAsync(b.BookId);
            var genreCodes = genreLinks.Select(l => l.GenreCode).ToHashSet(StringComparer.Ordinal);
            for (int i = 0; i < clbGenres.Items.Count; i++)
                clbGenres.SetItemChecked(i, clbGenres.Items[i] is GenreItem gi && genreCodes.Contains(gi.Code));
            RefreshPrimaryGenreChoices();
            SelectPrimaryGenre(genreLinks.FirstOrDefault(l => l.IsPrimary)?.GenreCode);

            _creditRows = (await _booksRepo.GetCreditsAsync(b.BookId)).Select(c => new CreditRow(c)).ToList();
            RebindCredits();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>詳細フォームをクリアし、新規入力モードに戻す。</summary>
    private void ClearForm()
    {
        numId.Value = 0;
        txtTitle.Text = "";
        txtTitleKana.Text = "";
        txtTitleEn.Text = "";
        SelectPublisher(null);
        dtRelease.Value = DateTime.Today;
        chkHasKindleDate.Checked = false;
        dtReleaseKindle.Enabled = false;
        dtReleaseKindle.Value = DateTime.Today;
        txtIsbn13.Text = "";
        numPageCount.Value = 0;
        txtTrimSize.Text = "";
        txtBindingText.Text = "";
        numPriceExTax.Value = 0;
        numPriceIncTax.Value = 0;
        numPriceKindle.Value = 0;
        chkHasPrint.Checked = true;
        chkHasKindle.Checked = false;
        txtAsinPrint.Text = "";
        txtAsinKindle.Text = "";
        SelectCoverSource(null);
        chkCoverShowBoth.Checked = false;
        txtOfficialUrl.Text = "";
        txtNotes.Text = "";

        for (int i = 0; i < clbSeries.Items.Count; i++) clbSeries.SetItemChecked(i, false);
        for (int i = 0; i < clbGenres.Items.Count; i++) clbGenres.SetItemChecked(i, false);
        RefreshPrimaryGenreChoices();

        _creditRows = new List<CreditRow>();
        RebindCredits();
    }

    /// <summary>入力内容で新規登録または上書き保存を行う。付随テーブルは全置換で保存する。</summary>
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            MessageBox.Show(this, "書名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!chkHasPrint.Checked && !chkHasKindle.Checked)
        {
            MessageBox.Show(this, "紙版・Kindle 版の少なくとも一方にチェックを入れてください。", "入力エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // クレジットは「名義 ID か表記のどちらかが必ず入る」ことが DB の CHECK 制約。
        // 保存前に弾いて、DB エラーではなく画面のメッセージで気付けるようにする。
        var credits = new List<BookCredit>();
        foreach (var r in _creditRows)
        {
            if (string.IsNullOrWhiteSpace(r.RoleCode)) continue;
            int? aliasId = int.TryParse(r.PersonAliasIdText, out int id) && id > 0 ? id : null;
            string? text = FormHelpers.NullIfEmpty(r.CreditText);
            if (aliasId == null && text == null)
            {
                MessageBox.Show(this, "クレジット行には「名義ID」か「表記」のどちらかが必要です。", "入力エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            credits.Add(new BookCredit
            {
                RoleCode = r.RoleCode,
                PersonAliasId = aliasId,
                CreditText = text
            });
        }

        try
        {
            int id = (int)numId.Value;
            var book = new Book
            {
                BookId = id,
                Title = txtTitle.Text.Trim(),
                TitleKana = FormHelpers.NullIfEmpty(txtTitleKana.Text),
                TitleEn = FormHelpers.NullIfEmpty(txtTitleEn.Text),
                PublisherProductCompanyId = (cmbPublisher.SelectedItem as PublisherItem)?.Id,
                ReleaseDate = dtRelease.Value.Date,
                ReleaseDateKindle = chkHasKindleDate.Checked ? dtReleaseKindle.Value.Date : null,
                Isbn13 = FormHelpers.NullIfEmpty(txtIsbn13.Text),
                PageCount = numPageCount.Value > 0 ? (ushort)numPageCount.Value : null,
                TrimSize = FormHelpers.NullIfEmpty(txtTrimSize.Text),
                BindingText = FormHelpers.NullIfEmpty(txtBindingText.Text),
                PriceExTax = numPriceExTax.Value > 0 ? (int)numPriceExTax.Value : null,
                PriceIncTax = numPriceIncTax.Value > 0 ? (int)numPriceIncTax.Value : null,
                PriceKindleIncTax = numPriceKindle.Value > 0 ? (int)numPriceKindle.Value : null,
                HasPrint = chkHasPrint.Checked,
                HasKindle = chkHasKindle.Checked,
                AmazonAsinPrint = FormHelpers.NullIfEmpty(txtAsinPrint.Text),
                AmazonAsinKindle = FormHelpers.NullIfEmpty(txtAsinKindle.Text),
                OfficialUrl = FormHelpers.NullIfEmpty(txtOfficialUrl.Text),
                Notes = FormHelpers.NullIfEmpty(txtNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName,
                IsDeleted = false
            };

            if (id == 0)
            {
                id = await _booksRepo.InsertAsync(book);
            }
            else
            {
                await _booksRepo.UpdateAsync(book);
            }

            // 代表書影と「両方表示」は画像列専用の経路で更新する（UpdateAsync は画像列を触らない）。
            await _booksRepo.UpdateCoverImageSelectionAsync(
                id, (cmbCoverSource.SelectedItem as CoverSourceItem)?.Code, chkCoverShowBoth.Checked);

            var seriesIds = clbSeries.CheckedItems.OfType<SeriesItem>().Select(s => s.SeriesId).ToList();
            var genreCodes = clbGenres.CheckedItems.OfType<GenreItem>().Select(g => g.Code).ToList();
            string? primaryGenre = (cmbPrimaryGenre.SelectedItem as GenreItem)?.Code;

            await _booksRepo.ReplaceRelationsAsync(
                id, seriesIds, genreCodes, primaryGenre, credits, Environment.UserName);

            MessageBox.Show(this, $"保存しました（ID = {id}）。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ReloadAsync();
            SelectRowById(id);
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中の書籍を論理削除する。付随テーブルは残るが、一覧・サイト出力からは外れる。</summary>
    private async Task DeleteAsync()
    {
        if (gridItems.CurrentRow?.DataBoundItem is not BookRow row) return;
        var b = row.Inner;
        if (this.Confirm($"書籍 [{b.Title}] (ID={b.BookId}) を論理削除しますか？") != DialogResult.Yes) return;

        try
        {
            await _booksRepo.SoftDeleteAsync(b.BookId, Environment.UserName);
            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>
    /// Amazon 検索ダイアログ（書籍モード）を開き、選ばれた紙／Kindle の ASIN を欄へ反映する。
    /// 書影が返ってきたら保存済みの書籍に限り画像列だけを直接更新する（保存ボタンを待たない）。
    /// </summary>
    private async Task SearchAmazonAsync()
    {
        var paApi = PaApiClientFactory.TryCreateFromAppConfig();
        if (paApi == null)
        {
            MessageBox.Show(this,
                "Amazon 検索を使うには App.config に Creators API のキー（PaApi.CredentialId / PaApi.CredentialSecret / PaApi.CredentialVersion / PaApi.PartnerTag）を設定してください。",
                "Creators API 未設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string initialKeyword = txtTitle.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(initialKeyword))
        {
            MessageBox.Show(this, "先に書名を入力してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            btnAmazonSearch.Enabled = false;
            using var dlg = new Dialogs.AmazonProductSearchDialog(paApi, initialKeyword, Dialogs.AmazonSearchMode.Book());
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            // 片方だけ採用したいケースに備えて、空の結果では既存値を上書きしない。
            if (!string.IsNullOrWhiteSpace(dlg.SelectedLeftAsin))
            {
                txtAsinPrint.Text = dlg.SelectedLeftAsin;
                chkHasPrint.Checked = true;
            }
            if (!string.IsNullOrWhiteSpace(dlg.SelectedRightAsin))
            {
                txtAsinKindle.Text = dlg.SelectedRightAsin;
                chkHasKindle.Checked = true;
            }

            int id = (int)numId.Value;
            if (id > 0
                && (!string.IsNullOrWhiteSpace(dlg.SelectedLeftImageUrl)
                    || !string.IsNullOrWhiteSpace(dlg.SelectedRightImageUrl)))
            {
                await _booksRepo.UpdateCoverImagesAsync(
                    id, dlg.SelectedLeftImageUrl, dlg.SelectedRightImageUrl,
                    dlg.SelectedCoverImageSource, DateTime.Now);
                SelectCoverSource(dlg.SelectedCoverImageSource);
            }
            else if (id == 0 && dlg.SelectedCoverImageUrl != null)
            {
                MessageBox.Show(this,
                    "書影は書籍を保存したあとに取得できます。ASIN だけ反映したので、保存してからもう一度検索してください。",
                    "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
        finally { btnAmazonSearch.Enabled = true; }
    }

    // ── クレジットタブ ──

    /// <summary>クレジット行を 1 行追加する。役職は先頭のマスタ値を初期値にする。</summary>
    private void AddCreditRow()
    {
        _creditRows.Add(new CreditRow { RoleCode = _creditRoles.FirstOrDefault()?.RoleCode ?? "" });
        RebindCredits();
    }

    /// <summary>選択中のクレジット行を削除する。</summary>
    private void RemoveCreditRow()
    {
        if (gridCredits.CurrentRow?.DataBoundItem is not CreditRow r) return;
        _creditRows.Remove(r);
        RebindCredits();
    }

    /// <summary>クレジット行のバインドを張り直す。行の追加・削除のたびに呼ぶ。</summary>
    private void RebindCredits()
    {
        _creditBinding.DataSource = null;
        _creditBinding.DataSource = _creditRows;
        gridCredits.DataSource = _creditBinding;
    }

    // ── 選択補助 ──

    /// <summary>代表ジャンルの選択肢を、いまチェックされているジャンルだけに絞り直す。</summary>
    private void RefreshPrimaryGenreChoices()
    {
        string? current = (cmbPrimaryGenre.SelectedItem as GenreItem)?.Code;
        var checkedGenres = clbGenres.CheckedItems.OfType<GenreItem>().ToList();

        cmbPrimaryGenre.DisplayMember = nameof(GenreItem.Label);
        cmbPrimaryGenre.DataSource = checkedGenres;

        if (current != null)
        {
            var again = checkedGenres.FirstOrDefault(g => g.Code == current);
            if (again != null) cmbPrimaryGenre.SelectedItem = again;
        }
    }

    /// <summary>出版社コンボで指定 ID の項目を選ぶ。null なら「(未設定)」。</summary>
    private void SelectPublisher(int? id)
    {
        foreach (var item in cmbPublisher.Items.OfType<PublisherItem>())
        {
            if (item.Id == id) { cmbPublisher.SelectedItem = item; return; }
        }
    }

    /// <summary>代表書影コンボで指定コードの項目を選ぶ。null なら「(未選択)」。</summary>
    private void SelectCoverSource(string? code)
    {
        foreach (var item in cmbCoverSource.Items.OfType<CoverSourceItem>())
        {
            if (item.Code == code) { cmbCoverSource.SelectedItem = item; return; }
        }
    }

    /// <summary>代表ジャンルコンボで指定コードの項目を選ぶ。</summary>
    private void SelectPrimaryGenre(string? code)
    {
        if (code == null) return;
        foreach (var item in cmbPrimaryGenre.Items.OfType<GenreItem>())
        {
            if (item.Code == code) { cmbPrimaryGenre.SelectedItem = item; return; }
        }
    }

    /// <summary>指定 ID の行をグリッドで選択し直す（保存直後のフォーカス戻し用）。</summary>
    private void SelectRowById(int id)
    {
        for (int i = 0; i < gridItems.Rows.Count; i++)
        {
            if (gridItems.Rows[i].DataBoundItem is BookRow r && r.BookId == id)
            {
                gridItems.ClearSelection();
                gridItems.Rows[i].Selected = true;
                gridItems.CurrentCell = gridItems.Rows[i].Cells[0];
                return;
            }
        }
    }

    // ── バインド用の表示ラッパ ──

    /// <summary>一覧グリッド用の表示ラッパ。発売日と版構成を整形して見せる。</summary>
    private sealed class BookRow
    {
        public Book Inner { get; }
        public int BookId => Inner.BookId;
        public string Title => Inner.Title;
        public string ReleaseDateLabel => Inner.ReleaseDate.ToString("yyyy-MM-dd");
        /// <summary>「紙電」「紙」「電」の 1〜2 文字で版の有無を示す。</summary>
        public string EditionsLabel => (Inner.HasPrint ? "紙" : "") + (Inner.HasKindle ? "電" : "");

        public BookRow(Book inner) { Inner = inner; }
    }

    /// <summary>クレジットタブの編集行。名義 ID は空欄を許すため文字列で持つ。</summary>
    private sealed class CreditRow
    {
        public string RoleCode { get; set; } = "";
        public string PersonAliasIdText { get; set; } = "";
        public string CreditText { get; set; } = "";

        public CreditRow() { }

        public CreditRow(BookCredit c)
        {
            RoleCode = c.RoleCode;
            PersonAliasIdText = c.PersonAliasId?.ToString() ?? "";
            CreditText = c.CreditText ?? "";
        }
    }

    /// <summary>出版社コンボの項目。Id が null なら「(未設定)」。</summary>
    private sealed record PublisherItem(int? Id, string Label);

    /// <summary>代表書影コンボの項目。Code が null なら「(未選択)」。</summary>
    private sealed record CoverSourceItem(string? Code, string Label);

    /// <summary>役職コンボ列の項目。</summary>
    private sealed record RoleItem(string Code, string Label);

    /// <summary>ジャンルのチェックリスト項目。</summary>
    private sealed class GenreItem
    {
        public string Code { get; }
        public string Label { get; }
        public GenreItem(BookGenre g) { Code = g.GenreCode; Label = g.NameJa; }
        public override string ToString() => Label;
    }

    /// <summary>シリーズのチェックリスト項目。開始年を添えて同名作の取り違えを防ぐ。</summary>
    private sealed class SeriesItem
    {
        public int SeriesId { get; }
        public string Label { get; }
        public SeriesItem(Series s)
        {
            SeriesId = s.SeriesId;
            Label = $"{s.StartDate.Year} {s.Title}";
        }
        public override string ToString() => Label;
    }
}
