using PrecureDataStars.Data;
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
/// <para>
/// v1.3.1 stage 21：テンプレ DSL の <c>{ROLE_LINK:code=...}</c> プレースホルダ実装のため
/// <see cref="LookupRoleHtmlAsync"/> を追加。役職コードから役職統計ページ
/// <c>/stats/roles/{role_code}/</c> へのリンク化済み HTML 断片を返す。<c>roles</c> マスタを
/// 引くための <see cref="RolesRepository"/> をコンストラクタで受け取り、内部キャッシュで
/// role_code → Role を保持する（ビルド 1 回中に何度も同じ役職が引かれるため）。
/// </para>
/// </summary>
internal sealed class LookupCache : ILookupCache
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // v1.3.1 stage 21: 役職コード → Role 解決用（{ROLE_LINK:code=...} プレースホルダで使用）。
    private readonly RolesRepository _rolesRepo;
    private readonly IConnectionFactory _factory;

    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();
    private readonly Dictionary<int, CompanyAlias?> _companyAliasCache = new();
    private readonly Dictionary<int, Logo?> _logoCache = new();
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();
    // v1.3.1 stage 21: role_code → Role キャッシュ。ビルド 1 回の実行中に同じ役職が
    // 複数のクレジット内で何度も引かれることが多いため、シンプルな辞書キャッシュを採用。
    // null 値もキャッシュ（未登録の負の結果も保存し、繰り返し DB に問い合わせない）。
    private readonly Dictionary<string, Role?> _roleCache = new(StringComparer.Ordinal);

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
        RolesRepository rolesRepo,
        IConnectionFactory factory)
    {
        _personAliasesRepo = personAliasesRepo;
        _companyAliasesRepo = companyAliasesRepo;
        _logosRepo = logosRepo;
        _characterAliasesRepo = characterAliasesRepo;
        // v1.3.1 stage 21: roles マスタ引きのための Repository を保持。
        _rolesRepo = rolesRepo;
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

    /// <summary>
    /// キャラ名義（character_alias）から所属キャラの character_id を解決する。
    /// クレジットセクション内で「キャラ名 → キャラ詳細ページへのリンク」を組み立てるために使う
    /// （v1.3.0 公開直前のデザイン整理 第 N+2 弾で追加）。
    /// character_alias は 1 つの character_id を直接持つため、追加のジョインは不要。
    /// 該当 alias が存在しない場合は null を返す。
    /// </summary>
    public async Task<int?> LookupCharacterIdFromAliasAsync(int aliasId)
    {
        var ca = await GetCharacterAliasAsync(aliasId).ConfigureAwait(false);
        return ca?.CharacterId;
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
    /// キャラクター名義 ID → リンク化済み HTML 断片（v1.3.1 stage B-4-prep 追加）。
    /// 名義表示名（<see cref="LookupCharacterAliasNameAsync"/>）と親キャラ ID
    /// （<see cref="LookupCharacterIdFromAliasAsync"/>）を組み合わせて
    /// <c>&lt;a href="/characters/{character_id}/"&gt;名義&lt;/a&gt;</c> を返す。
    /// 親キャラが引けないときは HTML エスケープしただけのプレーンテキストにフォールバック。
    /// </summary>
    public async Task<string?> LookupCharacterAliasHtmlAsync(int aliasId)
    {
        var name = await LookupCharacterAliasNameAsync(aliasId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(name)) return null;
        var characterId = await LookupCharacterIdFromAliasAsync(aliasId).ConfigureAwait(false);
        var escapedName = System.Net.WebUtility.HtmlEncode(name);
        if (characterId is int cid)
        {
            return $"<a href=\"/characters/{cid}/\">{escapedName}</a>";
        }
        return escapedName;
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

    /// <summary>
    /// 役職コード → リンク化済み HTML 断片（v1.3.1 stage 21 で追加）。
    /// <para>
    /// テンプレ DSL の <c>{ROLE_LINK:code=ROLE_CODE}</c> プレースホルダ実装の解決経路。
    /// 役職マスタ（<c>roles</c>）から <see cref="Role.NameJa"/> を引き、役職統計ページ
    /// <c>/stats/roles/{role_code}/</c> へのリンク付き HTML（<c>&lt;a href&gt;表示名&lt;/a&gt;</c>）を返す。
    /// </para>
    /// <para>
    /// 未登録の役職コードが指定された場合は null を返す（レンダラ側で空文字に展開され、
    /// <c>&lt;strong&gt;</c> ラップも省略される）。Role エンティティが取れても <c>name_ja</c> が
    /// 空文字なら同様に null 扱い。
    /// </para>
    /// <para>
    /// 注意：本メソッドは「テンプレ作者が指定した role_code をそのまま URL に埋める」設計で、
    /// 役職継承（<c>role_successions</c>）による代表 role_code への自動置換は行わない。テンプレ
    /// 作者の責任で「現在有効な役職コード」を書く運用とする（将来 role_successions と連動させたく
    /// なれば <see cref="Utilities.RoleSuccessorResolver"/> を本クラスに注入する拡張が可能）。
    /// </para>
    /// </summary>
    public async Task<string?> LookupRoleHtmlAsync(string roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return null;
        var role = await GetRoleAsync(roleCode).ConfigureAwait(false);
        if (role is null) return null;
        var nameJa = role.NameJa;
        if (string.IsNullOrEmpty(nameJa)) return null;
        var escapedName = System.Net.WebUtility.HtmlEncode(nameJa);
        // 役職コードは英大文字 + アンダースコア（例: MANGA, SERIALIZED_IN）想定なので URL エスケープは
        // 行わない（Uri.EscapeDataString を通すとアンダースコアはそのままだが、念のため将来別ケースが
        // 入っても安全になるよう EscapeUriString 的な扱いは保留。役職コードが想定外の文字を含む場合は
        // マスタ管理 UI 側で弾く前提）。
        return $"<a href=\"/stats/roles/{roleCode}/\">{escapedName}</a>";
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

    /// <summary>
    /// role_code → Role 取得（v1.3.1 stage 21 で追加）。負の結果もキャッシュする。
    /// <see cref="LookupRoleHtmlAsync"/> から呼ばれる。
    /// </summary>
    private async Task<Role?> GetRoleAsync(string roleCode)
    {
        if (_roleCache.TryGetValue(roleCode, out var hit)) return hit;
        var v = await _rolesRepo.GetByCodeAsync(roleCode).ConfigureAwait(false);
        _roleCache[roleCode] = v;
        return v;
    }
}