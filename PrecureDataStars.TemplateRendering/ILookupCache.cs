namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// <see cref="RoleTemplateRenderer"/> がプレースホルダ解決のために必要とする
/// 最小限の参照解決インターフェース（v1.3.0 で重複コード解消のため抽出）。
/// <para>
/// Catalog 側の <c>LookupCache</c>（GUI のメモリキャッシュ機構付き、エディタ間で共有）と
/// SiteBuilder 側の <c>LookupCache</c>（ビルド 1 回限りのオンメモリキャッシュ）の両方が
/// 本インターフェースを実装することで、テンプレ展開エンジン本体（<see cref="RoleTemplateRenderer"/>
/// と <see cref="Handlers.ThemeSongsHandler"/>）を 1 本のコードベースで共有できる。
/// </para>
/// <para>
/// 公開しているメソッドは「テンプレ DSL の <c>{COMPANIES}</c> / <c>{PERSONS}</c> /
/// <c>{LOGOS}</c> / <c>{LEADING_COMPANY}</c> プレースホルダ展開で必要となる 3 系統の
/// 表示名解決」のみに絞っている。Catalog 側 <c>LookupCache</c> はこれ以外にも多数の
/// メソッドを持つが、それらは GUI 専用の前段処理であり共通エンジンからは呼ばない。
/// </para>
/// <para>
/// v1.3.0 続編で、クレジット展開時のリンク化対応として「リンク化済み HTML を返す」系の
/// メソッド（<see cref="LookupPersonAliasHtmlAsync"/> 等）を抽象として追加した。
/// 各実装側で明示的に実装する必要がある：
/// </para>
/// <list type="bullet">
///   <item><description>SiteBuilder 側 <c>LookupCache</c> は <c>&lt;a href&gt;</c> 付きの HTML 断片を返す。</description></item>
///   <item><description>Catalog 側 <c>LookupCache</c> はリンクなしのプレーンエスケープ版を返す
///     （プレビュー画面ではリンクなし表示で問題ない）。</description></item>
/// </list>
/// </summary>
public interface ILookupCache
{
    /// <summary>
    /// 人物名義 ID から表示名を解決する（<c>display_text_override</c> 優先、無ければ <c>name</c>）。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupPersonAliasNameAsync(int aliasId);

    /// <summary>
    /// 企業屋号 ID から表示名を解決する。未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupCompanyAliasNameAsync(int aliasId);

    /// <summary>
    /// ロゴ ID から表示名を解決する。テンプレ展開時はロゴそのものではなく
    /// 「親屋号名」を表示するのが通常運用なので、戻り値は実装側の都合に従う。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupLogoNameAsync(int logoId);

    /// <summary>
    /// 人物名義 ID から「人物詳細ページへリンク化済みの HTML 断片」を返す（v1.3.0 続編で追加）。
    /// SiteBuilder 側は <c>&lt;a href="/persons/{person_id}/"&gt;名義&lt;/a&gt;</c> を組み立て、
    /// Catalog 側プレビューは HTML エスケープのみ（リンクなし）。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupPersonAliasHtmlAsync(int aliasId);

    /// <summary>
    /// 企業屋号 ID から「企業詳細ページへリンク化済みの HTML 断片」を返す（v1.3.0 続編で追加）。
    /// SiteBuilder 側は <c>&lt;a href="/companies/{company_id}/"&gt;屋号名&lt;/a&gt;</c> を組み立て、
    /// Catalog 側プレビューは HTML エスケープのみ。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupCompanyAliasHtmlAsync(int aliasId);

    /// <summary>
    /// ロゴ ID から「親屋号名 + 企業詳細リンク」相当のリンク化済み HTML 断片を返す（v1.3.0 続編で追加）。
    /// SiteBuilder 側はロゴの親屋号を企業詳細ページにリンクし、Catalog 側プレビューはエスケープのみ。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupLogoHtmlAsync(int logoId);
}
