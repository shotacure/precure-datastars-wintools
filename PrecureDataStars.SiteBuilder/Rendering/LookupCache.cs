using PrecureDataStars.Data;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 名義 / 屋号 / ロゴ / キャラ等の ID → 表示名解決の同期辞書 lookup。Catalog 側
/// <c>PrecureDataStars.Catalog.Forms.LookupCache</c> の API シグネチャ（<c>ILookupCache</c>）に揃え、
/// SiteBuilder が取り込んだ <see cref="TemplateRendering.RoleTemplateRenderer"/> および
/// <see cref="TemplateRendering.Handlers.ThemeSongsHandler"/> がそのまま利用できるようにしている。
/// <para>
/// 旧版は per-id <c>GetByIdAsync</c> を呼んで内部 Lazy キャッシュに格納する構造だったため、
/// 初回参照時に DB 往復が発生していた（クレジット入りエピソードのページ生成で per-entry に
/// alias / logo の参照が走るたびに、未キャッシュなら 1 クエリずつ DB に行く）。
/// 本クラスはコンストラクタで <see cref="BuildContext"/> 由来の全件辞書を直接受け取り、
/// すべての lookup を同期辞書アクセスで完結させる（<see cref="ILookupCache"/> のシグネチャ互換のため
/// 戻り値は <see cref="Task{TResult}"/> のままだが、内部はすべて <see cref="Task.FromResult{TResult}"/>）。
/// </para>
/// SiteBuilder は GUI を持たず、ビルド 1 回限りの実行なのでキャッシュ無効化系メソッドは未実装。
/// 必要な参照系のみ提供する：
/// <list type="bullet">
///   <item><c>LookupPersonAliasNameAsync</c></item>
///   <item><c>LookupCharacterAliasNameAsync</c></item>
///   <item><c>LookupCompanyAliasNameAsync</c></item>
///   <item><c>LookupCompanyIdFromAliasAsync</c>（屋号 → 親企業 ID 解決）</item>
///   <item><c>LookupLogoNameAsync</c>（屋号名 + CI バージョンラベル付き）</item>
///   <item><c>GetLogoForRenderingAsync</c>（ロゴエンティティ取得）</item>
///   <item><c>Factory</c>（テンプレ展開時に DB 直クエリを発行するため）</item>
/// </list>
/// </summary>
internal sealed class LookupCache : ILookupCache
{
    private readonly IReadOnlyDictionary<int, PersonAlias> _personAliasById;
    private readonly IReadOnlyDictionary<int, CharacterAlias> _characterAliasById;
    private readonly IReadOnlyDictionary<int, CompanyAlias> _companyAliasById;
    private readonly IReadOnlyDictionary<int, Logo> _logoById;
    private readonly IReadOnlyDictionary<string, Role> _roleByCode;
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// 人物名義 → リンク化済み HTML を返すための解決器。
    /// 共有名義（1 alias → 複数 person）の添字付き複数リンク化はここに委譲する。
    /// テンプレ展開時の <c>{PERSONS}</c> プレースホルダの出力で使う。
    /// 注入は <see cref="SetStaffLinkResolver(Utilities.StaffNameLinkResolver)"/> で行う
    /// （コンストラクタ循環を避けるため、SiteBuilderPipeline 側で順序を組んで後注入する）。
    /// </summary>
    private Utilities.StaffNameLinkResolver? _staffLinkResolver;

    public LookupCache(BuildContext ctx, IConnectionFactory factory)
    {
        _personAliasById = ctx.PersonAliasById;
        _characterAliasById = ctx.CharacterAliasById;
        _companyAliasById = ctx.CompanyAliasById;
        _logoById = ctx.LogoById;
        _roleByCode = ctx.RoleByCode;
        _factory = factory;
    }

    /// <summary>人物名義のリンク化を担う <see cref="Utilities.StaffNameLinkResolver"/> を後注入する。 LookupCache を構築するタイミングでは <c>StaffNameLinkResolver</c> がまだ初期化されていないため、 パイプライン側で順番に組み立ててから本メソッドで結びつける。</summary>
    public void SetStaffLinkResolver(Utilities.StaffNameLinkResolver resolver)
    {
        _staffLinkResolver = resolver;
    }

    /// <summary>テンプレ展開時の DB 直クエリ用の接続ファクトリ。 Catalog 側の <c>LookupCache.Factory</c> と同じ役割。</summary>
    internal IConnectionFactory Factory => _factory;

    public Task<string?> LookupPersonAliasNameAsync(int aliasId)
        => Task.FromResult(_personAliasById.TryGetValue(aliasId, out var pa) ? pa.Name : null);

    public Task<string?> LookupCharacterAliasNameAsync(int aliasId)
        => Task.FromResult(_characterAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null);

    /// <summary>キャラ名義（character_alias）から所属キャラの character_id を解決する。 クレジットセクション内で「キャラ名 → キャラ詳細ページへのリンク」を組み立てるために使う。 character_alias は 1 つの character_id を直接持つため、追加のジョインは不要。 該当 alias が存在しない場合は null を返す。</summary>
    public Task<int?> LookupCharacterIdFromAliasAsync(int aliasId)
        => Task.FromResult(_characterAliasById.TryGetValue(aliasId, out var ca) ? (int?)ca.CharacterId : null);

    public Task<string?> LookupCompanyAliasNameAsync(int aliasId)
        => Task.FromResult(_companyAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null);

    /// <summary>屋号（company_alias）から親企業の company_id を解決する。 クレジットセクション内で「屋号 / ロゴ → 企業詳細ページへのリンク」を組み立てるために使う。 該当 alias が存在しない場合は null を返す。</summary>
    public Task<int?> LookupCompanyIdFromAliasAsync(int aliasId)
        => Task.FromResult(_companyAliasById.TryGetValue(aliasId, out var ca) ? (int?)ca.CompanyId : null);

    /// <summary>logo_id → "屋号名  CI バージョンラベル" の文字列。未登録なら null。</summary>
    public Task<string?> LookupLogoNameAsync(int logoId)
    {
        if (!_logoById.TryGetValue(logoId, out var lg)) return Task.FromResult<string?>(null);
        var aliasName = _companyAliasById.TryGetValue(lg.CompanyAliasId, out var ca)
            ? ca.Name
            : $"alias#{lg.CompanyAliasId}";
        return Task.FromResult<string?>($"{aliasName}  {lg.CiVersionLabel}");
    }

    /// <summary>
    /// 人物名義 ID → リンク化済み HTML 断片。
    /// <see cref="Utilities.StaffNameLinkResolver"/> 経由で人物詳細ページへの
    /// <c>&lt;a href="/persons/{person_id}/"&gt;名義&lt;/a&gt;</c> を組み立てる。
    /// 共有名義（1 alias → 複数 person）は内部で「名義[1] [2]」のような添字付き複数リンクになる。
    /// resolver 未注入時はベース実装のプレーンエスケープにフォールバックする。
    /// </summary>
    public Task<string?> LookupPersonAliasHtmlAsync(int aliasId)
    {
        if (!_personAliasById.TryGetValue(aliasId, out var pa)) return Task.FromResult<string?>(null);
        string? displayText = pa.Name;
        if (string.IsNullOrEmpty(displayText)) return Task.FromResult<string?>(null);
        if (_staffLinkResolver is null)
        {
            return Task.FromResult<string?>(System.Net.WebUtility.HtmlEncode(displayText));
        }
        return Task.FromResult<string?>(_staffLinkResolver.ResolveAsHtml(aliasId, displayText));
    }

    /// <summary>
    /// キャラクター名義 ID → リンク化済み HTML 断片。
    /// 名義表示名と親キャラ ID を組み合わせて
    /// <c>&lt;a href="/characters/{character_id}/"&gt;名義&lt;/a&gt;</c> を返す。
    /// 親キャラが引けないときは HTML エスケープしただけのプレーンテキストにフォールバック。
    /// </summary>
    public Task<string?> LookupCharacterAliasHtmlAsync(int aliasId)
    {
        if (!_characterAliasById.TryGetValue(aliasId, out var ca)) return Task.FromResult<string?>(null);
        var name = ca.Name;
        if (string.IsNullOrEmpty(name)) return Task.FromResult<string?>(null);
        var escapedName = System.Net.WebUtility.HtmlEncode(name);
        return Task.FromResult<string?>($"<a href=\"/characters/{ca.CharacterId}/\">{escapedName}</a>");
    }

    /// <summary>企業屋号 ID → リンク化済み HTML 断片。 屋号 → 親企業の company_id を解決し、<c>&lt;a href="/companies/{company_id}/"&gt;屋号名&lt;/a&gt;</c> を返す。親企業が引けないときは HTML エスケープしただけのプレーンテキストにフォールバック。</summary>
    public Task<string?> LookupCompanyAliasHtmlAsync(int aliasId)
    {
        if (!_companyAliasById.TryGetValue(aliasId, out var ca)) return Task.FromResult<string?>(null);
        var name = ca.Name;
        if (string.IsNullOrEmpty(name)) return Task.FromResult<string?>(null);
        var escapedName = System.Net.WebUtility.HtmlEncode(name);
        if (ca.CompanyId > 0)
        {
            return Task.FromResult<string?>($"<a href=\"/companies/{ca.CompanyId}/\">{escapedName}</a>");
        }
        return Task.FromResult<string?>(escapedName);
    }

    /// <summary>ロゴ ID → リンク化済み HTML 断片。</summary>
    public Task<string?> LookupLogoHtmlAsync(int logoId)
    {
        if (!_logoById.TryGetValue(logoId, out var lg)) return Task.FromResult<string?>(null);
        if (!_companyAliasById.TryGetValue(lg.CompanyAliasId, out var ca)) return Task.FromResult<string?>(null);
        var escapedName = System.Net.WebUtility.HtmlEncode(ca.Name ?? "");
        if (ca.CompanyId > 0)
        {
            return Task.FromResult<string?>($"<a href=\"/companies/{ca.CompanyId}/\">{escapedName}</a>");
        }
        return Task.FromResult<string?>(escapedName);
    }

    /// <summary>
    /// 役職コード → リンク化済み HTML 断片。
    /// テンプレ DSL の <c>{ROLE_LINK:code=ROLE_CODE}</c> プレースホルダ実装の解決経路。
    /// 役職マスタ（<c>roles</c>）から <see cref="Role.NameJa"/> を引き、役職統計ページ
    /// <c>/stats/roles/{role_code}/</c> へのリンク付き HTML（<c>&lt;a href&gt;表示名&lt;/a&gt;</c>）を返す。
    /// 未登録の役職コードが指定された場合は null を返す（レンダラ側で空文字に展開され、
    /// <c>&lt;strong&gt;</c> ラップも省略される）。Role エンティティが取れても <c>name_ja</c> が
    /// 空文字なら同様に null 扱い。
    /// 注意：本メソッドは「テンプレ作者が指定した role_code をそのまま URL に埋める」設計で、
    /// 役職継承（<c>role_successions</c>）による代表 role_code への自動置換は行わない。テンプレ
    /// 作者の責任で「現在有効な役職コード」を書く運用とする。
    /// </summary>
    public Task<string?> LookupRoleHtmlAsync(string roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return Task.FromResult<string?>(null);
        if (!_roleByCode.TryGetValue(roleCode, out var role)) return Task.FromResult<string?>(null);
        var nameJa = role.NameJa;
        if (string.IsNullOrEmpty(nameJa)) return Task.FromResult<string?>(null);
        var escapedName = System.Net.WebUtility.HtmlEncode(nameJa);
        return Task.FromResult<string?>($"<a href=\"{PathUtil.RoleStatsUrl(roleCode)}\">{escapedName}</a>");
    }

    /// <summary>
    /// 役職コード + 呼び出し側指定ラベルから「リンク化済み HTML 断片」を返す。
    /// 役職コードがマスタに存在すれば <c>&lt;a href="/stats/roles/{roleCode}/"&gt;{escapedLabel}&lt;/a&gt;</c>。
    /// 存在しないコードのときはリンク先 404 を避けるため、リンクなしの <c>{escapedLabel}</c> 平文を返す。
    /// <paramref name="label"/> が空文字のときは null を返し、呼び出し側のテンプレ誤記に対する保険とする。
    /// </summary>
    public Task<string?> LookupRoleHtmlWithLabelAsync(string roleCode, string label)
    {
        if (string.IsNullOrEmpty(label)) return Task.FromResult<string?>(null);
        var escapedLabel = System.Net.WebUtility.HtmlEncode(label);
        if (string.IsNullOrEmpty(roleCode)) return Task.FromResult<string?>(escapedLabel);
        if (!_roleByCode.TryGetValue(roleCode, out _))
        {
            return Task.FromResult<string?>(escapedLabel);
        }
        return Task.FromResult<string?>($"<a href=\"{PathUtil.RoleStatsUrl(roleCode)}\">{escapedLabel}</a>");
    }

    /// <summary>レンダリング用のロゴエンティティ取得。</summary>
    internal Task<Logo?> GetLogoForRenderingAsync(int logoId)
        => Task.FromResult(_logoById.TryGetValue(logoId, out var lg) ? lg : null);
}
