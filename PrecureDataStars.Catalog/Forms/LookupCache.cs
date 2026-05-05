using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット編集フォーム（<see cref="CreditEditorForm"/>）のツリーノード表示で使う
/// マスタ名解決のキャッシュ（v1.2.0 工程 B-1 追加）。
/// <para>
/// ツリー再構築時にエントリ毎の参照先（人物名義 / 企業屋号 / ロゴ / キャラクター名義 /
/// 歌録音 / 役職）をマスタから引いてプレビュー文字列を生成し、結果をキャッシュする。
/// 同フォーム内では同じ alias_id 等を何度も解決することが多いため、ヒット率の高い
/// シンプルな辞書キャッシュを採用する。
/// </para>
/// <para>
/// クレジットを別のものに切り替えたタイミングでキャッシュを破棄する必要は無い。
/// マスタ側が更新されてもセッション中は表示用の古い値を引きずるが、編集 UI 側で
/// 明示リロード（<see cref="ClearAll"/>）を呼べば再取得できる。B-1 段階では
/// マスタ更新がフォームをまたぐことは無い前提でキャッシュは破棄しない方針。
/// </para>
/// </summary>
internal sealed class LookupCache
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly RolesRepository _rolesRepo;
    // v1.2.0 工程 H 追加：役職テンプレ展開時に episode_theme_songs を JOIN するために
    // 直接接続を取れる IConnectionFactory を保持しておく。
    private readonly PrecureDataStars.Data.Db.IConnectionFactory _factory;

    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();
    private readonly Dictionary<int, CompanyAlias?> _companyAliasCache = new();
    private readonly Dictionary<int, Logo?> _logoCache = new();
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();
    private readonly Dictionary<int, SongRecording?> _songRecCache = new();
    private readonly Dictionary<string, Role?> _roleCache = new();

    /// <summary>
    /// 直近の <see cref="BuildEntryPreviewAsync"/> 結果（entry_id → プレビュー文字列）。
    /// ツリー選択時に同期取得で使う。
    /// </summary>
    private readonly Dictionary<int, string> _entryPreviewCache = new();

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

    /// <summary>
    /// IConnectionFactory アクセサ（v1.2.0 工程 H 追加）。役職テンプレ展開時に
    /// <see cref="TemplateRendering.RoleTemplateRenderer"/> へ渡すために公開する。
    /// </summary>
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

    // ─── 公開：個別キャッシュの無効化（v1.2.0 工程 B-3c 追加） ───
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

    // ─────────── 公開：単発 ID → 名前解決（v1.2.0 工程 B-3 追加） ───────────
    // EntryEditorPanel が ID 入力欄の隣にプレビュー文字列を出すために使う。
    // 既存マスタの ID が入っていれば人間可読なラベルを、無ければ null を返す。

    /// <summary>person_alias_id → 名義名。未登録なら null。</summary>
    public async Task<string?> LookupPersonAliasNameAsync(int aliasId)
    {
        var pa = await GetPersonAliasAsync(aliasId);
        return pa?.Name;
    }

    /// <summary>character_alias_id → キャラ名義名。未登録なら null。</summary>
    public async Task<string?> LookupCharacterAliasNameAsync(int aliasId)
    {
        var ca = await GetCharacterAliasAsync(aliasId);
        return ca?.Name;
    }

    /// <summary>company_alias_id → 屋号名。未登録なら null。</summary>
    public async Task<string?> LookupCompanyAliasNameAsync(int aliasId)
    {
        var ca = await GetCompanyAliasAsync(aliasId);
        return ca?.Name;
    }

    /// <summary>logo_id → "[屋号名]  [CI バージョンラベル]"。未登録なら null。</summary>
    public async Task<string?> LookupLogoNameAsync(int logoId)
    {
        var lg = await GetLogoAsync(logoId);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId);
        string aliasName = ca?.Name ?? $"alias#{lg.CompanyAliasId}";
        return $"{aliasName}  {lg.CiVersionLabel}";
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

    /// <summary>
    /// 役職コードを名前文字列に解決する。NULL コード（自由記述ロール）は "(自由記述)" を返す。
    /// </summary>
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

    /// <summary>
    /// エントリ 1 件のプレビュー文字列を組み立てる。EntryKind に応じて参照先を解決し、
    /// 「[種別] 名前 (補助情報)」形式の 1 行を返す。
    /// 本放送限定エントリ（<see cref="CreditBlockEntry.IsBroadcastOnly"/> = true）には
    /// 先頭に 🎬 マークを付けて区別できるようにする。
    /// </summary>
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

    /// <summary>
    /// 直近にプレビュー化したエントリの文字列を同期取得する。
    /// ツリーノード選択時の右ペイン更新で利用。キャッシュ未ヒットなら null。
    /// </summary>
    public string? LastPreviewFor(int entryId)
        => _entryPreviewCache.TryGetValue(entryId, out var s) ? s : null;

    // ─────────── 種別別の文字列組み立て ───────────

    private async Task<string> BuildPersonPreviewAsync(CreditBlockEntry e)
    {
        if (e.PersonAliasId is null) return "(person_alias 未指定)";
        var pa = await GetPersonAliasAsync(e.PersonAliasId.Value);
        string main = pa is null ? $"alias#{e.PersonAliasId} (未登録)" : pa.Name ?? "(名義名なし)";
        // 所属付き
        string suffix = "";
        if (e.AffiliationCompanyAliasId.HasValue)
        {
            var ca = await GetCompanyAliasAsync(e.AffiliationCompanyAliasId.Value);
            suffix = ca is null ? $" ({e.AffiliationCompanyAliasId})" : $" ({ca.Name})";
        }
        else if (!string.IsNullOrEmpty(e.AffiliationText))
        {
            suffix = $" ({e.AffiliationText})";
        }
        return main + suffix;
    }

    private async Task<string> BuildCharacterVoicePreviewAsync(CreditBlockEntry e)
    {
        // 声優側
        string voiceLabel = "(声優未指定)";
        if (e.PersonAliasId.HasValue)
        {
            var pa = await GetPersonAliasAsync(e.PersonAliasId.Value);
            voiceLabel = pa is null ? $"alias#{e.PersonAliasId}" : pa.Name ?? "(名義名なし)";
        }
        // キャラ側
        string charLabel;
        if (e.CharacterAliasId.HasValue)
        {
            var ca = await GetCharacterAliasAsync(e.CharacterAliasId.Value);
            charLabel = ca is null ? $"char_alias#{e.CharacterAliasId}" : ca.Name ?? "(名義名なし)";
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
        var ca = await GetCompanyAliasAsync(e.CompanyAliasId.Value);
        return ca is null ? $"alias#{e.CompanyAliasId} (未登録)" : ca.Name ?? "(屋号名なし)";
    }

    private async Task<string> BuildLogoPreviewAsync(CreditBlockEntry e)
    {
        if (e.LogoId is null) return "(logo 未指定)";
        var lg = await GetLogoAsync(e.LogoId.Value);
        if (lg is null) return $"logo#{e.LogoId} (未登録)";
        // 親屋号も引いて併記する
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId);
        string aliasName = ca?.Name ?? $"alias#{lg.CompanyAliasId}";
        return $"{aliasName}  {lg.CiVersionLabel}";
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

    /// <summary>
    /// ロゴ ID からロゴエンティティを取得する公開アクセサ（v1.2.0 工程 H-10 で追加）。
    /// クレジットプレビューレンダラがロゴエントリの表示時に「紐づく屋号名」を引くために、
    /// ロゴから company_alias_id を取り出す経路として使う（CI ラベルは表示しない方針のため）。
    /// 内部キャッシュ <see cref="GetLogoAsync"/> を再利用する。
    /// </summary>
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
