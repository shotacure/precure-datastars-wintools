using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット編集フォーム（<see cref="CreditEditorForm"/>）のツリーノード表示で使う
/// マスタ名解決のキャッシュ。
/// ツリー再構築時にエントリ毎の参照先（人物名義 / 企業屋号 / ロゴ / キャラクター名義 /
/// 歌録音 / 役職）をマスタから引いてプレビュー文字列を生成し、結果をキャッシュする。
/// 同フォーム内では同じ alias_id 等を何度も解決することが多いため、ヒット率の高い
/// シンプルな辞書キャッシュを採用する。
/// クレジットを別のものに切り替えるタイミングでキャッシュを破棄する必要は無い。
/// マスタ側が更新されてもセッション中は表示用の古い値を引きずるが、編集 UI 側で
/// 明示リロード（<see cref="ClearAll"/>）を呼べば再取得できる。B-1 段階では
/// マスタ更新がフォームをまたぐことは無い前提でキャッシュは破棄しない方針。
/// </summary>
internal sealed class LookupCache : ILookupCache
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly RolesRepository _rolesRepo;
    // 役職テンプレ展開時に episode_theme_songs を JOIN するために
    // 直接接続を取れる IConnectionFactory を保持しておく。
    private readonly PrecureDataStars.Data.Db.IConnectionFactory _factory;

    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();
    private readonly Dictionary<int, CompanyAlias?> _companyAliasCache = new();
    private readonly Dictionary<int, Logo?> _logoCache = new();
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();
    private readonly Dictionary<int, SongRecording?> _songRecCache = new();
    private readonly Dictionary<string, Role?> _roleCache = new();

    /// <summary>直近の <see cref="BuildEntryPreviewAsync"/> 結果（entry_id → プレビュー文字列）。 ツリー選択時に同期取得で使う。</summary>
    private readonly Dictionary<int, string> _entryPreviewCache = new();

    /// <summary>現セッションで「保存待ち」のマスタを引き当てるための Draft セッション参照。
    /// 各種 <c>Lookup*Async</c> / <c>Build*PreviewAsync</c> は ID が負数のとき DB ではなく
    /// このセッションの <c>PendingXxxAliases</c> / <c>PendingLogos</c> から表示名を解決し、
    /// 「⚠ 名前」プレフィクス付きで返す。未確定マスタが UI 上で視覚的に区別できるようにする
    /// （ステージD 仕上げ）。<c>null</c> のときは負数 ID の解決を行わず、従来挙動と同じ
    /// 「alias#-1 (未登録)」のような表示になる。</summary>
    private CreditDraftSession? _pendingSession;

    /// <summary>未確定マスタ名の前に付けるマーク。「保存待ち」を視覚化する。TreeView 等のプレーンテキスト経路向け。</summary>
    private const string PendingMark = "⚠ ";

    /// <summary>HTML プレビュー経路の未確定マスタを「⚠ + 名前」全体まとめて赤太字に包むラッパ。
    /// 名前本体は呼び出し側で HtmlEncode 済みの文字列を渡す。
    /// TreeView 側のノード全体赤色（PendingNodeColor = #cc0000）と意味論を揃える。</summary>
    private static string WrapPendingHtml(string encodedName)
        => $"<span style=\"color:#c00;font-weight:bold;\">⚠ {encodedName}</span>";

    public LookupCache(
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        SongRecordingsRepository songRecRepo,
        RolesRepository rolesRepo,
        PrecureDataStars.Data.Db.IConnectionFactory factory)
    {
        _personAliasesRepo = personAliasesRepo;
        _companyAliasesRepo = companyAliasesRepo;
        _logosRepo = logosRepo;
        _characterAliasesRepo = characterAliasesRepo;
        _songRecRepo = songRecRepo;
        _rolesRepo = rolesRepo;
        _factory = factory;
    }

    /// <summary>IConnectionFactory アクセサ。役職テンプレ展開時に <see cref="TemplateRendering.RoleTemplateRenderer"/> へ渡すために公開する。</summary>
    internal PrecureDataStars.Data.Db.IConnectionFactory Factory => _factory;

    /// <summary>キャッシュをすべて破棄する（マスタ更新後の明示リロード用）。</summary>
    public void ClearAll()
    {
        _personAliasCache.Clear();
        _companyAliasCache.Clear();
        _logoCache.Clear();
        _characterAliasCache.Clear();
        _songRecCache.Clear();
        _roleCache.Clear();
        _entryPreviewCache.Clear();
    }

    /// <summary>Draft セッション参照を差し替える（クレジット切替時に親フォームが呼ぶ）。
    /// セッションが変わったら前セッションの負数 ID 解決結果は無効なので、プレビューキャッシュも
    /// クリアする（個別マスタキャッシュは正 ID のみが入っているため流用可）。</summary>
    internal void SetPendingSession(CreditDraftSession? session)
    {
        _pendingSession = session;
        _entryPreviewCache.Clear();
    }

    /// <summary>指定 ID が未確定マスタ（Pending）の負数仮 ID なら、Pending マップから引いた
    /// "⚠ 名前" 文字列を返す。該当 Pending が見つからない場合は null。
    /// 正数 ID または Session 未設定なら null を返す（呼び出し元が DB 経路にフォールバックする）。</summary>
    private string? ResolvePendingPersonAliasName(int aliasId)
    {
        if (aliasId >= 0 || _pendingSession is null) return null;
        return _pendingSession.PendingPersonAliases.TryGetValue(aliasId, out var pending)
            ? PendingMark + pending.AliasName
            : null;
    }

    private string? ResolvePendingCharacterAliasName(int aliasId)
    {
        if (aliasId >= 0 || _pendingSession is null) return null;
        return _pendingSession.PendingCharacterAliases.TryGetValue(aliasId, out var pending)
            ? PendingMark + pending.AliasName
            : null;
    }

    private string? ResolvePendingCompanyAliasName(int aliasId)
    {
        if (aliasId >= 0 || _pendingSession is null) return null;
        return _pendingSession.PendingCompanyAliases.TryGetValue(aliasId, out var pending)
            ? PendingMark + pending.AliasName
            : null;
    }

    /// <summary>Pending Logo の表示用文字列を組み立てて返す（"⚠ {親屋号名}  {CIラベル}" 形式）。
    /// 親屋号も Pending の場合は再帰的に Pending CompanyAlias を引く。</summary>
    private string? ResolvePendingLogoName(int logoId)
    {
        if (logoId >= 0 || _pendingSession is null) return null;
        if (!_pendingSession.PendingLogos.TryGetValue(logoId, out var pending)) return null;

        // 親屋号の表示名：正の実 ID なら従来通り DB 引き、負数なら Pending マップから。
        // ここでは同期的な表示文字列の組み立てが要るため、Pending 解決のみ同期で行い、
        // 実 ID 解決は呼び出し側に委ねる前提で「alias#{id}」フォールバックに留める。
        string parentName = pending.CompanyAliasId < 0
            ? (ResolvePendingCompanyAliasName(pending.CompanyAliasId) ?? $"alias#{pending.CompanyAliasId}")
            : $"alias#{pending.CompanyAliasId}";
        return $"{PendingMark}{parentName}  {pending.CiVersionLabel ?? ""}";
    }

    // ─── 公開：個別キャッシュの無効化 ───
    // QuickAdd ダイアログでマスタを新規投入した直後に呼び、対応する個別キャッシュ + プレビュー
    // キャッシュを破棄する。これにより、直後の RefreshPreviewsAsync で新 ID の名前解決が
    // 確実に DB から行われる（AUTO_INCREMENT で再利用された ID が古い値を返す事故を防ぐ）。

    /// <summary>person_alias_id 単位でキャッシュを無効化する。プレビューキャッシュも併せてクリア。</summary>
    public void InvalidatePersonAlias(int aliasId)
    {
        _personAliasCache.Remove(aliasId);
        _entryPreviewCache.Clear(); // プレビューはエントリ ID をキーにしているため一括で破棄
    }

    /// <summary>company_alias_id 単位でキャッシュを無効化する。</summary>
    public void InvalidateCompanyAlias(int aliasId)
    {
        _companyAliasCache.Remove(aliasId);
        // ロゴプレビューが屋号名を組み合わせるため、ロゴキャッシュも併せて破棄しておく
        _logoCache.Clear();
        _entryPreviewCache.Clear();
    }

    /// <summary>logo_id 単位でキャッシュを無効化する。</summary>
    public void InvalidateLogo(int logoId)
    {
        _logoCache.Remove(logoId);
        _entryPreviewCache.Clear();
    }

    /// <summary>character_alias_id 単位でキャッシュを無効化する。</summary>
    public void InvalidateCharacterAlias(int aliasId)
    {
        _characterAliasCache.Remove(aliasId);
        _entryPreviewCache.Clear();
    }

    // ─────────── 公開：単発 ID → 名前解決 ───────────
    // EntryEditorPanel が ID 入力欄の隣にプレビュー文字列を出すために使う。
    // 既存マスタの ID が入っていれば人間可読なラベルを、無ければ null を返す。

    /// <summary>person_alias_id → 名義名。未登録なら null。
    /// 負数 ID は Pending マスタの仮 ID として扱い、現セッションの <c>PendingPersonAliases</c> から
    /// "⚠ 名前" を返す。</summary>
    public async Task<string?> LookupPersonAliasNameAsync(int aliasId)
    {
        if (ResolvePendingPersonAliasName(aliasId) is string pendingName) return pendingName;
        var pa = await GetPersonAliasAsync(aliasId);
        return pa?.Name;
    }

    /// <summary>character_alias_id → キャラ名義名。未登録なら null。負数なら Pending 経路。</summary>
    public async Task<string?> LookupCharacterAliasNameAsync(int aliasId)
    {
        if (ResolvePendingCharacterAliasName(aliasId) is string pendingName) return pendingName;
        var ca = await GetCharacterAliasAsync(aliasId);
        return ca?.Name;
    }

    /// <summary>company_alias_id → 屋号名。未登録なら null。負数なら Pending 経路。</summary>
    public async Task<string?> LookupCompanyAliasNameAsync(int aliasId)
    {
        if (ResolvePendingCompanyAliasName(aliasId) is string pendingName) return pendingName;
        var ca = await GetCompanyAliasAsync(aliasId);
        return ca?.Name;
    }

    /// <summary>logo_id → "[屋号名]  [CI バージョンラベル]"。未登録なら null。負数なら Pending 経路。</summary>
    public async Task<string?> LookupLogoNameAsync(int logoId)
    {
        if (ResolvePendingLogoName(logoId) is string pendingName) return pendingName;
        var lg = await GetLogoAsync(logoId);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId);
        string aliasName = ca?.Name ?? $"alias#{lg.CompanyAliasId}";
        return $"{aliasName}  {lg.CiVersionLabel}";
    }

    // ──────── HTML 版解決（クレジット内リンク化対応） ────────
    // Catalog 側プレビュー画面ではリンクは出さない方針（プレビューは編集中の見た目確認用途で、
    // 詳細ページへの遷移は不要）。なので各 HTML 版メソッドは表示名を取得して HTML エスケープした
    // 文字列を返すだけの素朴な実装。SiteBuilder 側 LookupCache 側ではこの実装をオーバーライド
    // して <a href> 付きの HTML 断片を返す。

    /// <summary>person_alias_id → 表示名を HTML エスケープしただけのプレーンテキスト。 プレビュー画面ではリンク不要のため、SiteBuilder 側のような <c>&lt;a href&gt;</c> ラップは行わない。
    /// 負数 ID なら Pending 経路で「⚠ + 名前」全体を赤太字で包んだ HTML を返す。</summary>
    public async Task<string?> LookupPersonAliasHtmlAsync(int aliasId)
    {
        if (aliasId < 0 && _pendingSession is not null
            && _pendingSession.PendingPersonAliases.TryGetValue(aliasId, out var pending))
        {
            return WrapPendingHtml(System.Net.WebUtility.HtmlEncode(pending.AliasName));
        }
        var pa = await GetPersonAliasAsync(aliasId);
        if (pa?.Name is not string name || string.IsNullOrEmpty(name)) return null;
        return System.Net.WebUtility.HtmlEncode(name);
    }

    /// <summary>character_alias_id → キャラ名を HTML エスケープしただけのプレーンテキスト。負数 ID は Pending 経路で ⚠ + 名前を赤太字。</summary>
    public async Task<string?> LookupCharacterAliasHtmlAsync(int aliasId)
    {
        if (aliasId < 0 && _pendingSession is not null
            && _pendingSession.PendingCharacterAliases.TryGetValue(aliasId, out var pending))
        {
            return WrapPendingHtml(System.Net.WebUtility.HtmlEncode(pending.AliasName));
        }
        var ca = await GetCharacterAliasAsync(aliasId);
        if (ca?.Name is not string name || string.IsNullOrEmpty(name)) return null;
        return System.Net.WebUtility.HtmlEncode(name);
    }

    /// <summary>company_alias_id → 屋号名を HTML エスケープしただけのプレーンテキスト。負数 ID は Pending 経路で ⚠ + 名前を赤太字。</summary>
    public async Task<string?> LookupCompanyAliasHtmlAsync(int aliasId)
    {
        if (aliasId < 0 && _pendingSession is not null
            && _pendingSession.PendingCompanyAliases.TryGetValue(aliasId, out var pending))
        {
            return WrapPendingHtml(System.Net.WebUtility.HtmlEncode(pending.AliasName));
        }
        var ca = await GetCompanyAliasAsync(aliasId);
        if (ca?.Name is not string name || string.IsNullOrEmpty(name)) return null;
        return System.Net.WebUtility.HtmlEncode(name);
    }

    /// <summary>logo_id → ロゴ親屋号名を HTML エスケープしただけのプレーンテキスト。 CI バージョンラベルは付けず、屋号名のみを返す（テンプレ展開の通常運用に合わせる）。
    /// 負数 logo_id なら Pending 経路で「⚠ + 親屋号名」全体を赤太字で包んで返す（親屋号も Pending の場合は親側の名前を使う）。</summary>
    public async Task<string?> LookupLogoHtmlAsync(int logoId)
    {
        if (logoId < 0 && _pendingSession is not null
            && _pendingSession.PendingLogos.TryGetValue(logoId, out var pending))
        {
            // 親屋号の表示名を Pending または DB から解決。Logo 自体が Pending（=保存待ち）なので
            // 親屋号が確定済み（正の実 ID）であっても、Logo 経路全体としては Pending なので
            // 赤太字でラップする。
            string? parentName;
            if (pending.CompanyAliasId < 0)
            {
                parentName = _pendingSession.PendingCompanyAliases.TryGetValue(pending.CompanyAliasId, out var parentPending)
                    ? parentPending.AliasName
                    : null;
            }
            else
            {
                var parentCa = await GetCompanyAliasAsync(pending.CompanyAliasId);
                parentName = parentCa?.Name;
            }
            string label = !string.IsNullOrEmpty(parentName)
                ? parentName
                : (pending.CiVersionLabel ?? "");
            return WrapPendingHtml(System.Net.WebUtility.HtmlEncode(label));
        }
        var lg = await GetLogoAsync(logoId);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId);
        if (ca is null) return null;
        return System.Net.WebUtility.HtmlEncode(ca.Name ?? "");
    }

    /// <summary>
    /// 役職コード → 役職表示名を HTML エスケープしただけのプレーンテキスト。
    /// テンプレ DSL の <c>{ROLE_LINK:code=ROLE_CODE}</c> プレースホルダ実装の解決経路として、
    /// <see cref="ILookupCache.LookupRoleHtmlAsync"/> を Catalog 側で実装する版。Catalog の
    /// クレジット編集プレビュー画面ではリンクは出さない方針（プレビューは編集中の見た目確認用途で、
    /// 詳細ページへの遷移は不要）なので、SiteBuilder 側の <c>&lt;a href&gt;</c> ラップ版とは異なり、
    /// HTML エスケープした表示名のみを返す。レンダラ側で <c>&lt;strong&gt;</c> ラップが付与されるので、
    /// プレビュー上では <c>&lt;strong&gt;漫画&lt;/strong&gt;</c> のような太字テキストとして表示される。
    /// 内部的には既存の <c>_roleCache</c>（<see cref="LookupRoleNameJaAsync"/> と共用）を利用し、
    /// 未登録の役職コードに対しては null を返す（レンダラ側で空文字に展開、太字タグも残らない）。
    /// </summary>
    public async Task<string?> LookupRoleHtmlAsync(string roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return null;
        if (!_roleCache.TryGetValue(roleCode, out var role))
        {
            role = await _rolesRepo.GetByCodeAsync(roleCode);
            _roleCache[roleCode] = role;
        }
        var nameJa = role?.NameJa;
        if (string.IsNullOrEmpty(nameJa)) return null;
        return System.Net.WebUtility.HtmlEncode(nameJa);
    }

    /// <summary>役職コード + 呼び出し側指定ラベルから「リンクなしのプレーン HTML（=エスケープ済みテキスト）」を返す。 Catalog プレビュー画面はリンクなし表示で十分の方針と整合する。 <paramref name="label"/> が空文字のときは null を返す（呼び出し側の保険）。</summary>
    public Task<string?> LookupRoleHtmlWithLabelAsync(string roleCode, string label)
    {
        if (string.IsNullOrEmpty(label)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(System.Net.WebUtility.HtmlEncode(label));
    }

    /// <summary>logo_id → (屋号名, CI バージョンラベル) を分解した形で返す。 <see cref="Drafting.CreditBulkInputEncoder"/> が <c>[屋号#CIバージョン]</c> 構文を組み立てるために使用する。 未登録の logo_id（または屋号 alias）が指定された場合は null を返す。</summary>
    public async Task<(string CompanyAliasName, string CiVersionLabel)?> LookupLogoComponentsAsync(int logoId)
    {
        var lg = await GetLogoAsync(logoId);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId);
        if (ca is null) return null;
        return (ca.Name, lg.CiVersionLabel);
    }

    /// <summary>役職コードから <c>name_ja</c> のみを返す。 <see cref="Drafting.CreditBulkInputEncoder"/> が <c>"役職名:"</c> 行を組み立てるために使用する。 未登録 / null コードの場合は null を返す（呼び出し側でフォールバック表記を選ぶ）。</summary>
    public async Task<string?> LookupRoleNameJaAsync(string? roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return null;
        if (!_roleCache.TryGetValue(roleCode, out var role))
        {
            role = await _rolesRepo.GetByCodeAsync(roleCode);
            _roleCache[roleCode] = role;
        }
        return role?.NameJa;
    }

    /// <summary>song_recording_id → "recording#{id}  [歌手名] [variant]"。未登録なら null。</summary>
    public async Task<string?> LookupSongRecordingNameAsync(int recordingId)
    {
        var rec = await GetSongRecordingAsync(recordingId);
        if (rec is null) return null;
        string singer = rec.SingerName ?? "(歌手未指定)";
        string variant = string.IsNullOrEmpty(rec.VariantLabel) ? "" : $" [{rec.VariantLabel}]";
        return $"recording#{rec.SongRecordingId}  {singer}{variant}";
    }

    /// <summary>役職コードを名前文字列に解決する。NULL コード（自由記述ロール）は "(自由記述)" を返す。</summary>
    public async Task<string> ResolveRoleNameAsync(string? roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return "(自由記述)";
        if (!_roleCache.TryGetValue(roleCode, out var role))
        {
            role = await _rolesRepo.GetByCodeAsync(roleCode);
            _roleCache[roleCode] = role;
        }
        return role is null ? $"{roleCode} (未登録)" : $"{roleCode}  {role.NameJa}";
    }

    /// <summary>エントリ 1 件のプレビュー文字列を組み立てる。EntryKind に応じて参照先を解決し、 「[種別] 名前 (補助情報)」形式の 1 行を返す。 本放送限定エントリ（<see cref="CreditBlockEntry.IsBroadcastOnly"/> = true）には 先頭に 🎬 マークを付けて区別できるようにする。</summary>
    public async Task<string> BuildEntryPreviewAsync(CreditBlockEntry e)
    {
        string body = e.EntryKind switch
        {
            "PERSON"          => await BuildPersonPreviewAsync(e),
            "CHARACTER_VOICE" => await BuildCharacterVoicePreviewAsync(e),
            "COMPANY"         => await BuildCompanyPreviewAsync(e),
            "LOGO"            => await BuildLogoPreviewAsync(e),
            "TEXT"            => $"\"{e.RawText ?? ""}\"",
            _                 => $"(未対応の EntryKind: {e.EntryKind})"
        };
        // 本放送限定マーク：is_broadcast_only=1 の行は本放送だけで表示される行であることを明示
        string preview = e.IsBroadcastOnly ? $"🎬[本放送] {body}" : body;
        _entryPreviewCache[e.EntryId] = preview;
        return preview;
    }

    /// <summary>直近にプレビュー化したエントリの文字列を同期取得する。 ツリーノード選択時の右ペイン更新で利用。キャッシュ未ヒットなら null。</summary>
    public string? LastPreviewFor(int entryId)
        => _entryPreviewCache.TryGetValue(entryId, out var s) ? s : null;

    // ─────────── 種別別の文字列組み立て ───────────

    private async Task<string> BuildPersonPreviewAsync(CreditBlockEntry e)
    {
        if (e.PersonAliasId is null) return "(person_alias 未指定)";
        // LookupPersonAliasNameAsync 経由で負数 ID なら Pending マップから "⚠ 名前" が返る。
        string main = await LookupPersonAliasNameAsync(e.PersonAliasId.Value)
                      ?? $"alias#{e.PersonAliasId} (未登録)";
        // 所属付き
        string suffix = "";
        if (e.AffiliationCompanyAliasId.HasValue)
        {
            string? affName = await LookupCompanyAliasNameAsync(e.AffiliationCompanyAliasId.Value);
            suffix = affName is null ? $" ({e.AffiliationCompanyAliasId})" : $" ({affName})";
        }
        else if (!string.IsNullOrEmpty(e.AffiliationText))
        {
            suffix = $" ({e.AffiliationText})";
        }
        return main + suffix;
    }

    private async Task<string> BuildCharacterVoicePreviewAsync(CreditBlockEntry e)
    {
        // 声優側（負数 ID は Pending 経路で "⚠ 名前"）
        string voiceLabel = "(声優未指定)";
        if (e.PersonAliasId.HasValue)
        {
            voiceLabel = await LookupPersonAliasNameAsync(e.PersonAliasId.Value)
                         ?? $"alias#{e.PersonAliasId}";
        }
        // キャラ側
        string charLabel;
        if (e.CharacterAliasId.HasValue)
        {
            charLabel = await LookupCharacterAliasNameAsync(e.CharacterAliasId.Value)
                        ?? $"char_alias#{e.CharacterAliasId}";
        }
        else if (!string.IsNullOrEmpty(e.RawCharacterText))
        {
            charLabel = $"\"{e.RawCharacterText}\"";
        }
        else
        {
            charLabel = "(キャラ未指定)";
        }
        return $"{charLabel} / {voiceLabel}";
    }

    private async Task<string> BuildCompanyPreviewAsync(CreditBlockEntry e)
    {
        if (e.CompanyAliasId is null) return "(company_alias 未指定)";
        return await LookupCompanyAliasNameAsync(e.CompanyAliasId.Value)
               ?? $"alias#{e.CompanyAliasId} (未登録)";
    }

    private async Task<string> BuildLogoPreviewAsync(CreditBlockEntry e)
    {
        if (e.LogoId is null) return "(logo 未指定)";
        // LookupLogoNameAsync 経由で負数 ID は Pending Logo（親屋号も Pending なら ⚠⚠ で二重）。
        return await LookupLogoNameAsync(e.LogoId.Value)
               ?? $"logo#{e.LogoId} (未登録)";
    }

    // ─────────── キャッシュ付き個別解決 ───────────

    private async Task<PersonAlias?> GetPersonAliasAsync(int id)
    {
        if (_personAliasCache.TryGetValue(id, out var v)) return v;
        v = await _personAliasesRepo.GetByIdAsync(id);
        _personAliasCache[id] = v;
        return v;
    }

    private async Task<CompanyAlias?> GetCompanyAliasAsync(int id)
    {
        if (_companyAliasCache.TryGetValue(id, out var v)) return v;
        v = await _companyAliasesRepo.GetByIdAsync(id);
        _companyAliasCache[id] = v;
        return v;
    }

    private async Task<Logo?> GetLogoAsync(int id)
    {
        if (_logoCache.TryGetValue(id, out var v)) return v;
        v = await _logosRepo.GetByIdAsync(id);
        _logoCache[id] = v;
        return v;
    }

    /// <summary>ロゴ ID からロゴエンティティを取得する公開アクセサ。 クレジットプレビューレンダラがロゴエントリの表示時に「紐づく屋号名」を引くために、 ロゴから company_alias_id を取り出す経路として使う（CI ラベルは表示しない方針のため）。 内部キャッシュ <see cref="GetLogoAsync"/> を再利用する。</summary>
    internal Task<Logo?> GetLogoForRenderingAsync(int logoId) => GetLogoAsync(logoId);

    private async Task<CharacterAlias?> GetCharacterAliasAsync(int id)
    {
        if (_characterAliasCache.TryGetValue(id, out var v)) return v;
        v = await _characterAliasesRepo.GetByIdAsync(id);
        _characterAliasCache[id] = v;
        return v;
    }

    private async Task<SongRecording?> GetSongRecordingAsync(int id)
    {
        if (_songRecCache.TryGetValue(id, out var v)) return v;
        v = await _songRecRepo.GetByIdAsync(id);
        _songRecCache[id] = v;
        return v;
    }
}
