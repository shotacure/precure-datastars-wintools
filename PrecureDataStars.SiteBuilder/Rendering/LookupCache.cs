using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.TemplateRendering;

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
///   <item><c>LookupCompanyIdFromAliasAsync</c>（屋号 → 親企業 ID 解決）</item>
///   <item><c>LookupLogoNameAsync</c>（屋号名 + CI バージョンラベル付き）</item>
///   <item><c>GetLogoForRenderingAsync</c>（ロゴエンティティ取得）</item>
///   <item><c>Factory</c>（テンプレ展開時に DB 直クエリを発行するため）</item>
/// </list>
/// <para>
/// v1.3.0 続編：クレジット展開（<see cref="RoleTemplateRenderer"/> の <c>{PERSONS}</c> /
/// <c>{COMPANIES}</c> / <c>{LOGOS}</c> プレースホルダ、および
/// <see cref="Handlers.ThemeSongsHandler"/> の楽曲展開）の中で人物名・屋号・ロゴを
/// 詳細ページにリンク化するため、<see cref="ILookupCache.LookupPersonAliasHtmlAsync"/> 系の
/// HTML 版メソッドをオーバーライドして実装した。リンク URL の組み立ては以下のとおり：
/// </para>
/// <list type="bullet">
///   <item><description>人物名義 → <c>/persons/{person_id}/</c>（<see cref="Utilities.StaffNameLinkResolver"/> で
///     共有名義（1 alias → 複数 person）を「名義[1] [2]」のように複数リンクに展開）</description></item>
///   <item><description>企業屋号 → <c>/companies/{company_id}/</c>（屋号 → 親企業 ID 解決）</description></item>
///   <item><description>ロゴ → 親屋号の <c>/companies/{company_id}/</c> リンク</description></item>
/// </list>
/// </summary>
internal sealed class LookupCache : ILookupCache
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

    /// <summary>
    /// 人物名義 → リンク化済み HTML を返すための解決器（v1.3.0 続編で注入）。
    /// 共有名義（1 alias → 複数 person）の添字付き複数リンク化はここに委譲する。
    /// テンプレ展開時の <c>{PERSONS}</c> プレースホルダの出力で使う。
    /// 注入は <see cref="SetStaffLinkResolver(Utilities.StaffNameLinkResolver)"/> で行う
    /// （コンストラクタ循環を避けるため、SiteBuilderPipeline 側で順序を組んで後注入する）。
    /// </summary>
    private Utilities.StaffNameLinkResolver? _staffLinkResolver;

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
    /// 人物名義のリンク化を担う <see cref="Utilities.StaffNameLinkResolver"/> を後注入する（v1.3.0 続編で追加）。
    /// LookupCache を構築するタイミングでは <c>StaffNameLinkResolver</c> がまだ初期化されていないため、
    /// パイプライン側で順番に組み立ててから本メソッドで結びつける。
    /// </summary>
    public void SetStaffLinkResolver(Utilities.StaffNameLinkResolver resolver)
    {
        _staffLinkResolver = resolver;
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

    /// <summary>
    /// 屋号（company_alias）から親企業の company_id を解決する。
    /// クレジットセクション内で「屋号 / ロゴ → 企業詳細ページへのリンク」を組み立てるために使う。
    /// 該当 alias が存在しない場合は null を返す。
    /// </summary>
    public async Task<int?> LookupCompanyIdFromAliasAsync(int aliasId)
    {
        var ca = await GetCompanyAliasAsync(aliasId).ConfigureAwait(false);
        return ca?.CompanyId;
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

    /// <summary>
    /// 人物名義 ID → リンク化済み HTML 断片（v1.3.0 続編で追加）。
    /// <see cref="Utilities.StaffNameLinkResolver"/> 経由で人物詳細ページへの
    /// <c>&lt;a href="/persons/{person_id}/"&gt;名義&lt;/a&gt;</c> を組み立てる。
    /// 共有名義（1 alias → 複数 person）は内部で「名義[1] [2]」のような添字付き複数リンクになる。
    /// resolver 未注入時はベース実装のプレーンエスケープにフォールバックする。
    /// </summary>
    public async Task<string?> LookupPersonAliasHtmlAsync(int aliasId)
    {
        var displayText = await LookupPersonAliasNameAsync(aliasId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(displayText)) return null;
        if (_staffLinkResolver is null)
        {
            return System.Net.WebUtility.HtmlEncode(displayText);
        }
        return _staffLinkResolver.ResolveAsHtml(aliasId, displayText);
    }

    /// <summary>
    /// 企業屋号 ID → リンク化済み HTML 断片（v1.3.0 続編で追加）。
    /// 屋号 → 親企業の company_id を解決し、<c>&lt;a href="/companies/{company_id}/"&gt;屋号名&lt;/a&gt;</c>
    /// を返す。親企業が引けないときは HTML エスケープしただけのプレーンテキストにフォールバック。
    /// </summary>
    public async Task<string?> LookupCompanyAliasHtmlAsync(int aliasId)
    {
        var name = await LookupCompanyAliasNameAsync(aliasId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(name)) return null;
        var companyId = await LookupCompanyIdFromAliasAsync(aliasId).ConfigureAwait(false);
        var escapedName = System.Net.WebUtility.HtmlEncode(name);
        if (companyId is int cid)
        {
            return $"<a href=\"/companies/{cid}/\">{escapedName}</a>";
        }
        return escapedName;
    }

    /// <summary>
    /// ロゴ ID → リンク化済み HTML 断片（v1.3.0 続編で追加）。
    /// ロゴの親屋号を <c>/companies/{company_id}/</c> にリンク化したテキストを返す。
    /// CI バージョンラベルは付けず、屋号名のみを表示する（テンプレ展開の通常運用に合わせる）。
    /// </summary>
    public async Task<string?> LookupLogoHtmlAsync(int logoId)
    {
        var lg = await GetLogoAsync(logoId).ConfigureAwait(false);
        if (lg is null) return null;
        var ca = await GetCompanyAliasAsync(lg.CompanyAliasId).ConfigureAwait(false);
        if (ca is null) return null;
        var escapedName = System.Net.WebUtility.HtmlEncode(ca.Name ?? "");
        if (ca.CompanyId > 0)
        {
            return $"<a href=\"/companies/{ca.CompanyId}/\">{escapedName}</a>";
        }
        return escapedName;
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
