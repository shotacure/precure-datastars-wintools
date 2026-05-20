namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>出力パスと URL パスの組み立てヘルパー。</summary>
public static class PathUtil
{
    /// <summary>「URL パス」（先頭スラッシュ付き、末尾スラッシュ付き）を「出力ファイルパス」に変換する。 末尾は <c>index.html</c> を付与。</summary>
    /// <param name="outputRoot">出力ルートディレクトリ。</param>
    /// <param name="urlPath">URL パス（例 "/series/precure/"）。先頭スラッシュは必須。</param>
    public static string ToOutputFilePath(string outputRoot, string urlPath)
    {
        if (string.IsNullOrEmpty(urlPath) || urlPath[0] != '/')
            throw new ArgumentException("urlPath must start with '/'.", nameof(urlPath));

        // 先頭スラッシュを除去 → OS 区切り文字に変換 → index.html を末尾につなぐ。
        // urlPath が "/" の場合はサイトトップなので、出力は <root>/index.html。
        var trimmed = urlPath.TrimStart('/').TrimEnd('/');
        var relativeDir = trimmed.Length == 0
            ? string.Empty
            : trimmed.Replace('/', Path.DirectorySeparatorChar);
        var fullDir = string.IsNullOrEmpty(relativeDir)
            ? outputRoot
            : Path.Combine(outputRoot, relativeDir);
        return Path.Combine(fullDir, "index.html");
    }

    /// <summary>シリーズページの URL パスを返す（末尾スラッシュ付き）。</summary>
    public static string SeriesUrl(string slug) => $"/series/{slug}/";

    /// <summary>エピソードページの URL パスを返す。</summary>
    public static string EpisodeUrl(string slug, int seriesEpNo) => $"/series/{slug}/{seriesEpNo}/";

    /// <summary>人物詳細ページの URL パス。</summary>
    public static string PersonUrl(int personId) => $"/persons/{personId}/";

    /// <summary>企業詳細ページの URL パス。</summary>
    public static string CompanyUrl(int companyId) => $"/companies/{companyId}/";

    /// <summary>プリキュア詳細ページの URL パス。</summary>
    public static string PrecureUrl(int precureId) => $"/precures/{precureId}/";

    /// <summary>キャラクター詳細ページの URL パス。</summary>
    public static string CharacterUrl(int characterId) => $"/characters/{characterId}/";

    /// <summary>商品詳細ページの URL パス（catalog_no を URL エンコードして安全に格納）。</summary>
    public static string ProductUrl(string productCatalogNo)
        => $"/products/{Uri.EscapeDataString(productCatalogNo)}/";

    /// <summary>楽曲詳細ページの URL パス。</summary>
    public static string SongUrl(int songId) => $"/songs/{songId}/";

    /// <summary>音楽カテゴリのランディングページ URL。</summary>
    public static string MusicUrl() => "/music/";

    /// <summary>劇伴シリーズ一覧ページの URL。</summary>
    public static string BgmsIndexUrl() => "/bgms/";

    /// <summary>シリーズ別の劇伴詳細ページ URL。</summary>
    public static string BgmsForSeriesUrl(string slug) => $"/bgms/{slug}/";

    /// <summary>
    /// 役職詳細ページの URL パス。
    /// 「クリエーター」セクション <c>/creators/roles/</c> 配下の役職詳細を指す。
    /// 当該役職に関わった人物・企業/団体を 1 リストに混在させ、五十音順／初参加順／
    /// 担当話数が多い順のタブで切り替える脱ランキング型の一覧ページ。
    /// CreditTreeRenderer の役職アンカー（hover 時の出典リンク等）、シリーズ／
    /// エピソード／楽曲詳細のスタッフバッジなど、サイト各所からここへ集約参照される。
    /// roles テーブルはサロゲートの数値 ID を持たず PK が role_code（業務コード）であるため、
    /// URL に役職コードをそのまま埋めると <c>SCREENPLAY</c> のような内部コードが
    /// 露出してしまう。URL 体裁を整える目的で、URL パス上のコードのみ
    /// <see cref="string.ToLowerInvariant"/> で小文字化する。
    /// 役職コードは規約上「英大文字 + アンダースコア」のみで構成されるため、
    /// 小文字化しても元コードと 1 対 1 に対応し衝突しない。
    /// 静的サイトは生成時に URL→ファイルパスを確定させるだけでリクエスト時に
    /// DB 照合を行わないため、本変換は出力パスと参照リンクの双方で
    /// 同じ本メソッドを通している限り常に整合する。
    /// なお内部のデータ処理（集計キー・系譜解決など）は実コード（大文字）の
    /// ままで行い、本メソッドが組み立てる URL 文字列だけを小文字化する。
    /// </summary>
    public static string RoleStatsUrl(string roleCode)
        => $"/creators/roles/{roleCode.ToLowerInvariant()}/";

    /// <summary>クリエーターのトップ（ランディング）ページ URL（<c>/creators/</c>）。</summary>
    public static string CreatorsLandingUrl() => "/creators/";

    /// <summary>スタッフ一覧ページの URL（/creators/staff/）。</summary>
    public static string CreatorsStaffUrl() => "/creators/staff/";

    /// <summary>声の出演（声優）一覧ページの URL（/creators/voice-cast/）。</summary>
    public static string CreatorsVoiceCastUrl() => "/creators/voice-cast/";

    /// <summary>全ファイルパスから親ディレクトリを再帰的に作成する。</summary>
    public static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>アセット（CSS など）の URL パス。</summary>
    public static string AssetUrl(string assetRelative) => "/assets/" + assetRelative.TrimStart('/');

    /// <summary>
    /// 劇伴 cue の m_no_detail 値を URL-safe な短い文字列に正規化する（HTML id 属性および
    /// URL fragment の両方で安全に使える形）。
    /// 仕様：
    /// <list type="bullet">
    ///   <item>ASCII の英数字（A-Z / a-z / 0-9）はそのまま。</item>
    ///   <item>その他の文字は <c>%XX</c> 形式の URL エンコードに置換（CJK や記号も安全に通る）。</item>
    /// </list>
    /// 「商品詳細ページのトラック行 → 劇伴詳細ページの該当 cue 行へのアンカーリンク」のように、
    /// 生成サイト内で同じ正規化規則を使う必要のある場所すべてで本メソッドを共有することで、
    /// アンカー id とリンク先 fragment の食い違いを起こさない設計。
    /// </summary>
    public static string SlugifyMNoDetail(string mNoDetail)
    {
        if (string.IsNullOrEmpty(mNoDetail)) return "";
        var sb = new System.Text.StringBuilder(mNoDetail.Length);
        foreach (var ch in mNoDetail)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
            }
            else
            {
                // 文字単位で UTF-8 バイト列を取り、それぞれを %XX に。CJK 等の多バイト文字も
                // この処理で確実に URL-safe になる。
                var bytes = System.Text.Encoding.UTF8.GetBytes(new[] { ch });
                foreach (var b in bytes) sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 劇伴詳細ページ <c>/bgms/{slug}/</c> 内の特定 cue 行へのアンカー URL を返す
    /// （fragment は <c>#cue-{SlugifyMNoDetail(mNoDetail)}</c>）。商品詳細ページのトラック行から
    /// この URL を <c>&lt;a&gt;</c> で参照することで、ユーザーが「この M ナンバーの cue 詳細を見たい」と
    /// 思った時に 1 クリックで劇伴詳細ページの該当行へ飛べる。
    /// </summary>
    public static string BgmCueAnchorUrl(string seriesSlug, string mNoDetail)
        => $"/bgms/{seriesSlug}/#cue-{SlugifyMNoDetail(mNoDetail)}";
}