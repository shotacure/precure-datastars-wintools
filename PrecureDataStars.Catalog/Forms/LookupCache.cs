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
        RolesRepository rolesRepo)
    {
        _personAliasesRepo = personAliasesRepo;
        _companyAliasesRepo = companyAliasesRepo;
        _logosRepo = logosRepo;
        _characterAliasesRepo = characterAliasesRepo;
        _songRecRepo = songRecRepo;
        _rolesRepo = rolesRepo;
    }

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
    /// </summary>
    public async Task<string> BuildEntryPreviewAsync(CreditBlockEntry e)
    {
        string preview = e.EntryKind switch
        {
            "PERSON"          => await BuildPersonPreviewAsync(e),
            "CHARACTER_VOICE" => await BuildCharacterVoicePreviewAsync(e),
            "COMPANY"         => await BuildCompanyPreviewAsync(e),
            "LOGO"            => await BuildLogoPreviewAsync(e),
            "SONG"            => await BuildSongPreviewAsync(e),
            "TEXT"            => $"\"{e.RawText ?? ""}\"",
            _                 => $"(未対応の EntryKind: {e.EntryKind})"
        };
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

    private async Task<string> BuildSongPreviewAsync(CreditBlockEntry e)
    {
        if (e.SongRecordingId is null) return "(song_recording 未指定)";
        var rec = await GetSongRecordingAsync(e.SongRecordingId.Value);
        if (rec is null) return $"recording#{e.SongRecordingId} (未登録)";
        // 曲タイトルは songs を別途引かないと取れないが、B-1 段階では recording 単体の情報のみ
        // で簡略表示する（B-3 で SongsRepository 注入 + JOIN 付き取得に拡張する）。
        string singer = rec.SingerName ?? "(歌手未指定)";
        string variant = string.IsNullOrEmpty(rec.VariantLabel) ? "" : $" [{rec.VariantLabel}]";
        return $"recording#{rec.SongRecordingId}  {singer}{variant}";
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
