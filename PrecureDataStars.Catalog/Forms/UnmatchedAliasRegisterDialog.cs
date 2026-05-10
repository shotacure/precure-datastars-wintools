
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 未マッチング名義テキスト（song_recordings.singer_name 等）を起点に、person_alias を新規登録するダイアログ
/// （v1.3.0 ブラッシュアップ stage 16 Phase 3 で新設）。
/// <para>
/// 2 モードを選択可能：
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       「既存人物の新名義として登録」：既存 <see cref="Person"/> を Picker で選び、
///       その人物配下に新しい <see cref="PersonAlias"/> を 1 件作成して
///       <see cref="PersonAliasesRepository.InsertAsync"/> + person_alias_persons 中間表 INSERT で結合する。
///     </description>
///   </item>
///   <item>
///     <description>
///       「新規人物としてまとめて登録」：<see cref="PersonsRepository.QuickAddWithSingleAliasAsync"/>
///       を呼んで、人物本体と最初の名義を一気に作る。氏名 = 対象テキストで初期化される（書き換え可能）。
///     </description>
///   </item>
/// </list>
/// <para>
/// どちらのモードでも、共通の「alias 名義かな / 英語名 / 表示上書き」を後から付け足せる。
/// 「既存人物」モードでは alias の name は対象テキストそのまま固定（編集不可）。
/// 「新規人物」モードでは QuickAdd の挙動に従い、alias.name = persons.full_name と一致する
/// （= 対象テキストか、ユーザーが書き換えた氏名のいずれか）。
/// </para>
/// </summary>
public partial class UnmatchedAliasRegisterDialog : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly string _sourceText;

    /// <summary>登録完了後、呼び出し側に返す新規 alias_id（成功時のみ正の値）。</summary>
    public int CreatedAliasId { get; private set; }

    /// <summary>登録完了後、呼び出し側に返す新規 alias の表示名（picker で再選択させずに直接セットしたいとき用）。</summary>
    public string CreatedAliasDisplay { get; private set; } = "";

    // 「既存人物の新名義」モードで Picker から受け取った person_id（未選択なら null）
    private int? _selectedExistingPersonId;

    public UnmatchedAliasRegisterDialog(
        PersonsRepository personsRepo,
        PersonAliasesRepository aliasesRepo,
        string sourceText)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _aliasesRepo = aliasesRepo ?? throw new ArgumentNullException(nameof(aliasesRepo));
        _sourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));

        InitializeComponent();

        // 元テキストを表示。新規モード時の氏名既定値にも使う。
        txtSourceText.Text = _sourceText;
        txtNewFullName.Text = _sourceText;

        // モード切替で関連コントロールの有効/無効を切り替える。
        rbAttachExisting.CheckedChanged += (_, __) => { UpdateControlsByMode(); UpdateFullNameWarning(); };
        rbCreateNew.CheckedChanged      += (_, __) => { UpdateControlsByMode(); UpdateFullNameWarning(); };
        UpdateControlsByMode();

        // 新規人物モードでフル氏名にスペースが無いと姓・名分離が効かないので、運用者に警告を出す。
        // テキスト変更とモード切替の両方で再評価する。MessageBox は出さず lblStatus にだけ表示。
        txtNewFullName.TextChanged += (_, __) => UpdateFullNameWarning();
        UpdateFullNameWarning();

        btnPickExistingPerson.Click += async (_, __) => await OnPickExistingPersonAsync();

        // OK ボタン Click で登録を実行。
        // FormClosing 内で await を仕掛けると ObjectDisposedException や DialogResult の
        // 二重設定で詰みやすいため、登録ロジックは Click ハンドラに集約し、成功時のみ
        // DialogResult.OK をセットして閉じる、失敗時は閉じない、というシンプルな流れにする。
        // btnOk は Designer 側で DialogResult.OK 既定なので、いったん DialogResult.None に
        // 上書きしておき、登録成功時に明示的に DialogResult.OK へ戻して Close する。
        btnOk.DialogResult = DialogResult.None;
        btnOk.Click += async (_, __) =>
        {
            // async ハンドラの再入防止：await 中にユーザーが再度ボタンを押せると
            // person / alias の二重登録が起きる（実害確認済み）。明示的に Enabled で抑止する。
            if (!btnOk.Enabled) return;
            btnOk.Enabled = false;
            btnCancel.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (await TryRegisterAsync())
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return; // Close 後は finally でも UI に触らない（Disposed 例外を避ける）
                }
                // 失敗時は何もしない（ダイアログ開いたまま継続入力させる）。
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                {
                    Cursor = Cursors.Default;
                    btnOk.Enabled = true;
                    btnCancel.Enabled = true;
                }
            }
        };
    }

    /// <summary>選択中のモードに応じて、片方の入力グループだけを有効化する。</summary>
    private void UpdateControlsByMode()
    {
        bool attach = rbAttachExisting.Checked;

        // 既存人物モードの UI
        lblExistingPerson.Enabled = attach;
        txtExistingPersonDisplay.Enabled = attach;
        btnPickExistingPerson.Enabled = attach;
        lblExistingPersonIdCaption.Enabled = attach;
        lblExistingPersonIdValue.Enabled = attach;

        // 新規人物モードの UI
        lblNewFullName.Enabled = !attach;
        txtNewFullName.Enabled = !attach;
        lblNewFullNameKana.Enabled = !attach;
        txtNewFullNameKana.Enabled = !attach;
        lblNewNameEn.Enabled = !attach;
        txtNewNameEn.Enabled = !attach;

        // alias 共通フィールド：
        //   - 表示上書き（display_text_override）は両モードで使える（任意）。
        //   - 名義かな・名義(英) は既存人物モードでのみ使う。新規モードでは persons の
        //     full_name_kana / name_en がそのまま alias の name_kana / name_en として使われるため
        //     再入力は不要 → UI 上で無効化して運用者を惑わせない。
        lblAliasNameKana.Enabled = attach;
        txtAliasNameKana.Enabled = attach;
        lblAliasNameEn.Enabled   = attach;
        txtAliasNameEn.Enabled   = attach;
    }

    /// <summary>
    /// 新規人物モードかつフル氏名に半角/全角スペースが含まれていない場合、姓・名分離が効かないので
    /// lblStatus に警告を出す。MessageBox は使わない（運用者の作業フローを止めない）。
    /// 既存人物モードや、フル氏名が空のときは警告を出さない。
    /// </summary>
    private void UpdateFullNameWarning()
    {
        if (!rbCreateNew.Checked)
        {
            // 既存人物モードではフル氏名を使わないので警告クリア。
            ClearStatus();
            return;
        }
        string text = txtNewFullName.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            ClearStatus();
            return;
        }
        // 半角スペース / 全角スペースのいずれかを含むかチェック。
        bool hasSpace = text.IndexOf(' ') >= 0 || text.IndexOf('\u3000') >= 0;
        if (!hasSpace)
        {
            lblStatus.Text = "⚠ フル氏名に半角/全角スペースがありません。姓と名が分離されません（family_name 列のみに格納されます）。";
            lblStatus.ForeColor = System.Drawing.Color.Firebrick;
        }
        else
        {
            ClearStatus();
        }
    }

    private void ClearStatus()
    {
        lblStatus.Text = "";
        lblStatus.ForeColor = System.Drawing.Color.DimGray;
    }

    private async Task OnPickExistingPersonAsync()
    {
        // 名義 picker を流用して person を選ぶ運用：選んだ alias の人物を取り、その person_id を結合先にする。
        // 専用の Persons Picker が無いため、PersonAliasPickerDialog を経由して person を解決する。
        using var dlg = new PersonAliasPickerDialog(_aliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.SelectedId is not int pickedAliasId) return;

        // 選んだ名義の主人物 person_id を引く（person_alias_persons の seq=1 が「主人物」）。
        // PersonAliasesRepository に直接 GetMainPersonId は無いので、ここでは
        // GetByPersonAsync を逆方向で使う代わりに、シンプルに「全 persons を引いて
        // 名義に紐付くものを探す」のはコスト的に厳しい。代わりに直接 SQL を投げる。
        int? personId = await GetMainPersonIdForAliasAsync(pickedAliasId);
        if (personId is null)
        {
            MessageBox.Show(this,
                "選択した名義に紐付く主人物が見つかりませんでした。person_alias_persons を確認してください。",
                "解決失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var person = await _personsRepo.GetByIdAsync(personId.Value);
        if (person is null)
        {
            MessageBox.Show(this,
                "person_id={0} の人物レコードが見つかりません。",
                "解決失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _selectedExistingPersonId = person.PersonId;
        txtExistingPersonDisplay.Text = person.FullName ?? "";
        lblExistingPersonIdValue.Text = person.PersonId.ToString();
        lblExistingPersonIdValue.ForeColor = System.Drawing.Color.Black;
    }

    /// <summary>
    /// 指定 alias_id の主人物（person_alias_persons.person_seq=1）の person_id を返す。
    /// 純粋に SQL クエリで解決する小ヘルパー（既存 Repository に該当 API が無いため、
    /// 本ダイアログ内に閉じた形で直 SQL を投げる）。
    /// </summary>
    private async Task<int?> GetMainPersonIdForAliasAsync(int aliasId)
    {
        // PersonAliasesRepository には IConnectionFactory が private で隠れているため、
        // 直 SQL は PersonsRepository / PersonAliasesRepository 経由ではなく、
        // 「全 persons を引いて中間表を一緒に解決する」より、PersonAliasesRepository
        // に小ヘルパーを追加する方が綺麗。だが今回は最小変更主義で、
        // 既に存在する GetByPersonAsync を逆向きに使うのは効率が悪い。
        // よって最小限の追加コードとして、Persons / Aliases の代わりに
        // ReassignToPersonAsync などが使う接続経路と同じ仕組みを別途用意することは避け、
        // 既存 API の組み合わせで近似する。
        //
        // 案：persons を全件 GetAllAsync で取り、そこから対象 alias_id に紐付くものを
        // 1 件返す処理を Repository に追加する手があるが、ここでは UI 動作を最小実装で
        // 通すため、PersonsRepository に存在する SearchAsync を逆順に使う代替手段が
        // 取れない。よって、ダイアログ層から直接 SQL を投げる軽量実装をここに置く。
        //
        // まずは PersonsRepository に依存して小さなクエリを通すヘルパーを呼ぶ：
        return await _personsRepo.GetMainPersonIdForAliasAsync(aliasId);
    }

    /// <summary>OK ボタン後の登録処理。成功で true。失敗で false（ダイアログは閉じない）。</summary>
    private async Task<bool> TryRegisterAsync()
    {
        string name = (_sourceText ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "対象テキストが空です。", "入力不正", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string? aliasKana = NullIfEmpty(txtAliasNameKana.Text);
        string? aliasEn   = NullIfEmpty(txtAliasNameEn.Text);
        string? aliasOverride = NullIfEmpty(txtAliasDisplayOverride.Text);

        if (rbAttachExisting.Checked)
        {
            // 既存人物モード：person_id 必須。
            if (_selectedExistingPersonId is not int personId)
            {
                MessageBox.Show(this, "既存人物が選択されていません。", "入力不正", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 1 トランザクション完結型の API で登録（途中失敗時も孤児 alias を残さない）。
            int aliasId = await _personsRepo.AddAliasToExistingPersonAsync(
                personId: personId,
                aliasName: name,
                aliasKana: aliasKana,
                aliasEn: aliasEn,
                aliasDisplayOverride: aliasOverride,
                createdBy: Environment.UserName);

            CreatedAliasId = aliasId;
            CreatedAliasDisplay = !string.IsNullOrEmpty(aliasOverride) ? aliasOverride : name;
            return true;
        }
        else
        {
            // 新規人物モード：person + alias + 中間表を 1 トランザクションで作る。
            //
            // 設計方針（Phase 3 hotfix11 で確定）：
            //   人物（persons）と最初の名義（person_aliases）は「同じ表記で同じ kana / en」で
            //   作る。つまり persons.full_name / full_name_kana / name_en の値を、そのまま
            //   alias.name / name_kana / name_en にも採用する。対象テキスト（_sourceText、
            //   過去のフリーテキスト由来で SP 無しで保存されているケースが多い）は alias.name に
            //   は使わない。
            //
            //   既存の検索ロジック（SearchSongColumnAsync 等）は、alias.name と元テキスト
            //   両方の半角/全角 SP を抜いてから比較するため、alias.name=「青木 久美子」(SP有) と
            //   元テキスト=「青木久美子」(SP無) は問題なく一致する。
            //
            //   別表記の名義を後から追加したい場合（例：「青木久美子」「青木 久美子」「Aoki Kumiko」
            //   をそれぞれ別 alias として登録）は、既存人物モードを使う運用。
            //
            // 姓・名は full_name から半角/全角スペース区切りで自動分割：
            //   - 1 単語のみ（スペースなし）→ family_name のみに代入、given_name は NULL
            //   - 2 単語以上 → 1 単語目 = family_name、残り全部 = given_name
            string fullName = NullIfEmpty(txtNewFullName.Text) ?? name;
            string? fullKana = NullIfEmpty(txtNewFullNameKana.Text);
            string? fullEn   = NullIfEmpty(txtNewNameEn.Text);

            (string? familyName, string? givenName) = SplitFullName(fullName);

            int aliasId = await _personsRepo.AddPersonWithAliasAsync(
                // alias.name / name_kana / name_en は persons の値そのまま採用。
                // alias 側の入力フィールド（txtAliasNameKana / txtAliasNameEn）は新規モード時
                // UI 上で無効化されており、ここでは参照しない。
                aliasName: fullName,
                aliasKana: fullKana,
                aliasEn: fullEn,
                aliasDisplayOverride: aliasOverride,
                fullName: fullName,
                fullNameKana: fullKana,
                familyName: familyName,
                givenName: givenName,
                nameEn: fullEn,
                notes: null,
                createdBy: Environment.UserName);

            CreatedAliasId = aliasId;
            // ダイアログ閉じ後の "対象名義" 表示用。display_text_override 指定があればそれを優先。
            CreatedAliasDisplay = !string.IsNullOrEmpty(aliasOverride) ? aliasOverride : fullName;
            return true;
        }
    }

    /// <summary>
    /// フル氏名を半角/全角スペース区切りで分割して、(family_name, given_name) のタプルを返す
    /// （v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// 1 単語しか無ければ family_name のみセット、given_name は null。
    /// 2 単語以上あれば 1 単語目を family_name、残りをすべてスペースで連結したものを given_name に。
    /// 連続スペース・前後スペースは整理する。
    /// </summary>
    private static (string? family, string? given) SplitFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return (null, null);

        // 半角・全角スペースの両方で分割。空要素は除去（連続スペースに耐性）。
        var parts = fullName.Trim().Split(
            new[] { ' ', '\u3000' },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (null, null);
        if (parts.Length == 1) return (parts[0], null);
        return (parts[0], string.Join(' ', parts, 1, parts.Length - 1));
    }

    private static string? NullIfEmpty(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }
}
