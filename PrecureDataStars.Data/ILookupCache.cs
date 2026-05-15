namespace PrecureDataStars.Data;

/// <summary>
/// テンプレ展開・クレジット展開・リポジトリ層の HTML 出力で必要となる参照解決インターフェース。
/// <para>
/// 本インターフェースは <c>PrecureDataStars.Data</c> 名前空間に置かれる。
/// 理由：
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>SongCreditsRepository</c> / <c>SongRecordingSingersRepository</c> から
///     「リンク化済み HTML を返す表示文字列ビルダ」を提供するために、Data 層自身が
///     名義解決インターフェースを参照できる必要がある。
///   </description></item>
///   <item><description>
///     上位プロジェクト群（Catalog / SiteBuilder / TemplateRendering）は Data を参照しており、
///     これらから <c>ILookupCache</c> を見える状態が維持される。
///   </description></item>
/// </list>
/// <para>
/// Catalog 側の <c>LookupCache</c>（GUI のメモリキャッシュ機構付き、エディタ間で共有）と
/// SiteBuilder 側の <c>LookupCache</c>（ビルド 1 回限りのオンメモリキャッシュ）の両方が
/// 本インターフェースを実装することで、テンプレ展開エンジンと Data 層の HTML 表示ロジックを
/// 1 本のコードベースで共有できる。
/// </para>
/// <para>
/// 公開メソッドは「テンプレ DSL のプレースホルダ展開で必要となる名義・屋号・ロゴ・キャラ・役職の
/// 5 系統の表示解決」に絞っている。Catalog 側 <c>LookupCache</c> はこれ以外にも多数の
/// メソッドを持つが、それらは GUI 専用の前段処理であり共通エンジンからは呼ばない。
/// </para>
/// <para>
/// <see cref="LookupCharacterAliasHtmlAsync"/> は、主題歌・挿入歌の歌唱クレジットで
/// 「キャラクター名義（CV: 声優）」のようにキャラクター由来の名義をリンク化するためのメソッド。
/// </para>
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
    /// 人物名義 ID から「人物詳細ページへリンク化済みの HTML 断片」を返す。
    /// SiteBuilder 側は <c>&lt;a href="/persons/{person_id}/"&gt;名義&lt;/a&gt;</c> を組み立て、
    /// Catalog 側プレビューは HTML エスケープのみ（リンクなし）。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupPersonAliasHtmlAsync(int aliasId);

    /// <summary>
    /// 企業屋号 ID から「企業詳細ページへリンク化済みの HTML 断片」を返す。
    /// SiteBuilder 側は <c>&lt;a href="/companies/{company_id}/"&gt;屋号名&lt;/a&gt;</c> を組み立て、
    /// Catalog 側プレビューは HTML エスケープのみ。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupCompanyAliasHtmlAsync(int aliasId);

    /// <summary>
    /// ロゴ ID から「親屋号名 + 企業詳細リンク」相当のリンク化済み HTML 断片を返す。
    /// SiteBuilder 側はロゴの親屋号を企業詳細ページにリンクし、Catalog 側プレビューはエスケープのみ。
    /// 未ヒット時は <c>null</c>。
    /// </summary>
    Task<string?> LookupLogoHtmlAsync(int logoId);

    /// <summary>
    /// 役職コードから「役職統計ページへリンク化済みの HTML 断片」を返す。
    /// <para>
    /// テンプレ DSL の新プレースホルダ <c>{ROLE_LINK:code=ROLE_CODE}</c> の解決経路として、
    /// 役職コードから役職表示名（<c>roles.name_ja</c>）を引き、SiteBuilder 側は
    /// <c>&lt;a href="/stats/roles/{role_code}/"&gt;表示名&lt;/a&gt;</c> を組み立てて返す。
    /// Catalog 側プレビューは表示名を HTML エスケープしただけのプレーンテキストを返す
    /// （プレビュー画面ではリンクなし表示で問題ない方針と整合）。
    /// </para>
    /// <para>
    /// 戻り値の HTML 断片は呼び出し側で必要に応じて <c>&lt;strong&gt;</c> 等のラップを行う
    /// （クレジット展開エンジンでは役職リンクを <c>&lt;strong&gt;</c> でラップする運用）。
    /// 未ヒット時（未登録の役職コードが指定されたとき）は <c>null</c> を返し、
    /// 呼び出し側で空文字に展開される。
    /// </para>
    /// </summary>
    Task<string?> LookupRoleHtmlAsync(string roleCode);

    /// <summary>
    /// 役職コードと「呼び出し側で指定する表示ラベル」から、リンク化済み HTML 断片を返す。
    /// <para>
    /// 設計動機：テンプレ DSL の <c>{ROLE_LINK:code=...}</c> 既定挙動は <c>roles.name_ja</c> をそのまま
    /// 表示ラベルに使うが、文脈ごとに表記揺れ（「歌」⇔「うた」、「漫画」⇔「マンガ」など）を
    /// テンプレ側で制御したいケースがある。本メソッドはテンプレで <c>{ROLE_LINK:code=VOCALS,label=うた}</c>
    /// のように <paramref name="label"/> を明示渡しできる経路で、その指定値をリンクテキストとして使う。
    /// </para>
    /// <para>
    /// 実装挙動：
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     SiteBuilder 側：roles マスタに <paramref name="roleCode"/> が存在すれば
    ///     <c>&lt;a href="/stats/roles/{roleCode}/"&gt;{HtmlEncode(label)}&lt;/a&gt;</c> を返す。
    ///     存在しなければリンクなしの <c>HtmlEncode(label)</c> 平文を返す（リンク先 404 を避けるため）。
    ///   </description></item>
    ///   <item><description>
    ///     Catalog 側プレビュー：常に <c>HtmlEncode(label)</c> 平文を返す（プレビュー画面はリンクなし方針と整合）。
    ///   </description></item>
    ///   <item><description>
    ///     <paramref name="label"/> が空文字のときは何も出さない（呼び出し側のテンプレ誤記の保険）。
    ///   </description></item>
    /// </list>
    /// </summary>
    /// <param name="roleCode">役職コード（リンク URL の構築に使用、未登録時はリンクなし）。</param>
    /// <param name="label">表示ラベル（テンプレ側で指定された文字列をそのまま使う）。HtmlEncode は本メソッド内で行う。</param>
    Task<string?> LookupRoleHtmlWithLabelAsync(string roleCode, string label);

    /// <summary>
    /// キャラクター名義 ID から「キャラクター詳細ページへリンク化済みの HTML 断片」を返す。
    /// <para>
    /// 主題歌・挿入歌の歌唱クレジットで「キャラクター名義（CV: 声優）」のような表記を
    /// HTML 化するときに使う。SiteBuilder 側は character_aliases から親キャラ ID を引いて
    /// <c>&lt;a href="/characters/{character_id}/"&gt;表示名&lt;/a&gt;</c> を組み立てる。
    /// Catalog 側プレビューは HTML エスケープのみ（プレビュー画面ではリンクなし表示で十分）。
    /// 未ヒット時は <c>null</c>。
    /// </para>
    /// </summary>
    Task<string?> LookupCharacterAliasHtmlAsync(int aliasId);
}