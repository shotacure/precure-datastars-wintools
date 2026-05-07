using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 名義 / 屋号 / ロゴ / キャラ等の ID → 表示名解決のキャッシュ。
/// <para>
/// Catalog 側 <c>PrecureDataStars.Catalog.Forms.LookupCache</c> の API シグネチャに揃え、
/// SiteBuilder が取り込んだ <see cref="TemplateRendering.RoleTemplateRenderer"/> および
/// <see cref="TemplateRendering.Handlers.ThemeSongsHandler"/> がそのまま利用できるようにしている。
/// </para>
/// <para>
/// SiteBuilder は GUI を持たず、ビルド 1 回限りの実行なのでキャッシュ無効化系メソッドは未実装。
/// 必要な参照系のみ提供する：
/// </para>
/// <list type="bullet">
///   <item><c>LookupPersonAliasNameAsync</c></item>
///   <item><c>LookupCharacterAliasNameAsync</c></item>
///   <item><c>LookupCompanyAliasNameAsync</c></item>
///   <item><c>LookupLogoNameAsync</c>（屋号名 + CI バージョンラベル付き）</item>
///   <item><c>GetLogoForRenderingAsync</c>（ロゴエンティティ取得）</item>
///   <item><c>Factory</c>（テンプレ展開時に DB 直クエリを発行するため）</item>
/// </list>
/// </summary>
internal sealed class LookupCache
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly IConnectionFactory _factory;

    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();
    private readonly Dictionary<int, CompanyAlias?> _companyAliasCache = new();
    private readonly Dictionary<int, Logo?> _logoCache = new();
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();

    public LookupCache(
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        IConnectionFactory factory)
    {
        _personAliasesRepo = personAliasesRepo;
        _companyAliasesRepo = companyAliasesRepo;
        _logosRepo = logosRepo;
        _characterAliasesRepo = characterAliasesRepo;
        _factory = factory;
    }

    /// <summary>
    /// テンプレ展開時の DB 直クエリ用の接続ファクトリ。
    /// Catalog 側の <c>LookupCache.Factory</c> と同じ役割。
    /// </summary>
    internal IConnectionFactory Factory => _factory;

    public async Task<string?> LookupPersonAliasNameAsync(int aliasId)
    {
        var pa = await GetPersonAliasAsync(aliasId).ConfigureAwait(false);
        return pa?.Name;
    }

    public async Task<string?> LookupCharacterAliasNameAsync(int aliasId)
    {
        var ca = await GetCharacterAliasAsync(aliasId).ConfigureAwait(false);
        return ca?.Name;
    }

    public async Task<string?> LookupCompanyAliasNameAsync(int aliasId)
    {
        var ca = await GetCompanyAliasAsync(aliasId).ConfigureAwait(false);
        return ca?.Name;
    }

    /// <summary>logo_id → "屋号名  CI バージョンラベル" の文字列。未登録なら null。</summary>
    public async Task<string?> LookupLogoNameAsync(int logoId)
    {
        var lg = await GetLogoAsync(logoId).ConfigureAwait(false);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId).ConfigureAwait(false);
        string aliasName = ca?.Name ?? $"alias#{lg.CompanyAliasId}";
        return $"{aliasName}  {lg.CiVersionLabel}";
    }

    /// <summary>レンダリング用のロゴエンティティ取得。</summary>
    internal Task<Logo?> GetLogoForRenderingAsync(int logoId) => GetLogoAsync(logoId);

    // ─── 内部キャッシュ付きヘルパ ───

    private async Task<PersonAlias?> GetPersonAliasAsync(int aliasId)
    {
        if (_personAliasCache.TryGetValue(aliasId, out var hit)) return hit;
        var v = await _personAliasesRepo.GetByIdAsync(aliasId).ConfigureAwait(false);
        _personAliasCache[aliasId] = v;
        return v;
    }

    private async Task<CharacterAlias?> GetCharacterAliasAsync(int aliasId)
    {
        if (_characterAliasCache.TryGetValue(aliasId, out var hit)) return hit;
        var v = await _characterAliasesRepo.GetByIdAsync(aliasId).ConfigureAwait(false);
        _characterAliasCache[aliasId] = v;
        return v;
    }

    private async Task<CompanyAlias?> GetCompanyAliasAsync(int aliasId)
    {
        if (_companyAliasCache.TryGetValue(aliasId, out var hit)) return hit;
        var v = await _companyAliasesRepo.GetByIdAsync(aliasId).ConfigureAwait(false);
        _companyAliasCache[aliasId] = v;
        return v;
    }

    private async Task<Logo?> GetLogoAsync(int logoId)
    {
        if (_logoCache.TryGetValue(logoId, out var hit)) return hit;
        var v = await _logosRepo.GetByIdAsync(logoId).ConfigureAwait(false);
        _logoCache[logoId] = v;
        return v;
    }
}
