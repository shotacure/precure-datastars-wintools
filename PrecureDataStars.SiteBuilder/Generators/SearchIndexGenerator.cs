using System.Text.Encodings.Web;
using System.Text.Json;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サイト内検索用の静的 JSON インデックスを <c>/search-index.json</c> に書き出す（v1.3.0 後半追加）。
/// <para>
/// 完全静的サイトでクライアント側 JS による検索を成立させるための索引ファイル。
/// バックエンドサーバーを持たず、AWS S3 等で配信できる構成を維持したまま、
/// JS 側で本ファイルを fetch → クライアント側でフィルタする運用。
/// </para>
/// <para>
/// 含めるアイテム種別（v1.3.0 時点）：
/// </para>
/// <list type="bullet">
///   <item><description>シリーズ（series：TV / 映画 / 短編 / スピンオフ）</description></item>
///   <item><description>エピソード（episode）</description></item>
///   <item><description>プリキュア（precure）— 変身ヒロイン</description></item>
///   <item><description>キャラクター（character）— 全 character_kinds</description></item>
///   <item><description>人物（person）— スタッフ・声優</description></item>
///   <item><description>企業（company）— 制作会社等</description></item>
///   <item><description>楽曲（song）— 主題歌・劇中歌</description></item>
///   <item><description>商品（product）— CD / Blu-ray / DVD</description></item>
/// </list>
/// <para>
/// JSON のフォーマットは検索 UI と密結合。短いキー名で容量を抑える設計：
/// </para>
/// <list type="table">
///   <item><term><c>u</c></term><description>URL（先頭スラッシュ付き、末尾スラッシュ付き）</description></item>
///   <item><term><c>t</c></term><description>表示タイトル</description></item>
///   <item><term><c>k</c></term><description>アイテム種別コード（"series" / "episode" / ...）</description></item>
///   <item><term><c>s</c></term><description>サブテキスト（属性ラベルや所属シリーズ名）。空文字可。</description></item>
///   <item><term><c>x</c></term><description>検索用の正規化された読み（ひらがな・小文字、空文字なら t を使う）</description></item>
/// </list>
/// </summary>
public sealed class SearchIndexGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;
    private readonly IConnectionFactory _factory;

    public SearchIndexGenerator(BuildContext ctx, BuildConfig config, IConnectionFactory factory)
    {
        _ctx = ctx;
        _config = config;
        _factory = factory;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating search index");

        var items = new List<SearchIndexItem>();

        // ── シリーズ ──
        // BuildContext._ctx.Series が起動時にロード済みなのでそれを使う。
        // v1.3.0：子作品（parent_series_id != NULL の映画系、SPIN-OFF を除く）は単独詳細ページを
        // 生成しないので、検索インデックスからも除外する。除外対象は IsChildOfMovie 判定で識別。
        foreach (var s in _ctx.Series)
        {
            if (IsChildOfMovie(s)) continue;
            items.Add(new SearchIndexItem
            {
                u = $"/series/{s.Slug}/",
                t = s.Title,
                k = "series",
                s = SeriesKindLabel(s.KindCode),
                x = NormalizeForSearch(s.Title)
            });
        }

        // ── エピソード ──
        // EpisodesBySeries から全件取得。エピソード詳細 URL は /series/{slug}/{episode_no}/。
        foreach (var (sid, eps) in _ctx.EpisodesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var series)) continue;
            foreach (var e in eps)
            {
                items.Add(new SearchIndexItem
                {
                    u = $"/series/{series.Slug}/{e.SeriesEpNo}/",
                    t = $"第{e.SeriesEpNo}話 {e.TitleText}",
                    k = "episode",
                    s = series.Title,
                    // タイトルの読みは title_kana に入っているのでそれを使う。空なら t から正規化。
                    x = !string.IsNullOrEmpty(e.TitleKana)
                        ? NormalizeForSearch(e.TitleKana)
                        : NormalizeForSearch(e.TitleText)
                });
            }
        }

        // ── プリキュア ──
        var precuresRepo = new PrecuresRepository(_factory);
        var characterAliasesRepo = new CharacterAliasesRepository(_factory);
        var charactersRepo = new CharactersRepository(_factory);
        var allPrecures = await precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allCharacterAliases = (await characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacters = (await charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var characterAliasMap = allCharacterAliases.ToDictionary(a => a.AliasId);
        var characterMap = allCharacters.ToDictionary(c => c.CharacterId);
        foreach (var p in allPrecures)
        {
            // 表示名は変身後の名義を主とする（未設定なら変身前を使う）。
            string displayName = "";
            string readingName = "";
            if (characterAliasMap.TryGetValue(p.TransformAliasId, out var ta))
            {
                displayName = ta.Name;
                readingName = ta.NameKana ?? ta.Name;
            }
            else if (characterAliasMap.TryGetValue(p.PreTransformAliasId, out var pta))
            {
                displayName = pta.Name;
                readingName = pta.NameKana ?? pta.Name;
            }

            // 補助情報として変身前名義も付ける。
            string subText = "プリキュア";
            if (characterAliasMap.TryGetValue(p.PreTransformAliasId, out var preAlias)
                && !string.Equals(preAlias.Name, displayName, StringComparison.Ordinal))
            {
                subText = $"プリキュア（{preAlias.Name}）";
            }

            items.Add(new SearchIndexItem
            {
                u = PathUtil.PrecureUrl(p.PrecureId),
                t = displayName,
                k = "precure",
                s = subText,
                x = NormalizeForSearch(readingName)
            });
        }

        // ── キャラクター ──
        // 出演履歴を持つキャラクターはサイトに詳細ページがある前提で全件出す。
        // 表示は character.name + 種別ラベル。読みは name_kana を使う。
        foreach (var c in allCharacters)
        {
            items.Add(new SearchIndexItem
            {
                u = PathUtil.CharacterUrl(c.CharacterId),
                t = c.Name,
                k = "character",
                s = CharacterKindLabel(c.CharacterKind),
                x = NormalizeForSearch(c.NameKana ?? c.Name)
            });
        }

        // ── 人物 ──
        var personsRepo = new PersonsRepository(_factory);
        var allPersons = await personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        foreach (var p in allPersons)
        {
            items.Add(new SearchIndexItem
            {
                u = PathUtil.PersonUrl(p.PersonId),
                t = p.FullName,
                k = "person",
                s = "",
                x = NormalizeForSearch(p.FullNameKana ?? p.FullName)
            });
        }

        // ── 企業 ──
        var companiesRepo = new CompaniesRepository(_factory);
        var allCompanies = await companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        foreach (var c in allCompanies)
        {
            items.Add(new SearchIndexItem
            {
                u = PathUtil.CompanyUrl(c.CompanyId),
                t = c.Name,
                k = "company",
                s = "",
                x = NormalizeForSearch(c.NameKana ?? c.Name)
            });
        }

        // ── 楽曲 ──
        var songsRepo = new SongsRepository(_factory);
        var allSongs = await songsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        foreach (var sg in allSongs)
        {
            // サブテキスト：出自シリーズ名（あれば）。
            string subText = "";
            if (sg.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var series))
                subText = series.Title;
            items.Add(new SearchIndexItem
            {
                u = PathUtil.SongUrl(sg.SongId),
                t = sg.Title,
                k = "song",
                s = subText,
                x = NormalizeForSearch(sg.TitleKana ?? sg.Title)
            });
        }

        // ── 商品 ──
        var productsRepo = new ProductsRepository(_factory);
        var allProducts = await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var productKindsRepo = new ProductKindsRepository(_factory);
        var productKinds = (await productKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        foreach (var pr in allProducts)
        {
            items.Add(new SearchIndexItem
            {
                u = PathUtil.ProductUrl(pr.ProductCatalogNo),
                t = pr.Title,
                k = "product",
                s = productKindMap.TryGetValue(pr.ProductKindCode, out var pk) ? pk.NameJa : pr.ProductKindCode,
                // 商品には title_kana がないので Title から正規化する。
                x = NormalizeForSearch(pr.Title)
            });
        }

        // 検索 JSON を /search-index.json に書き出す。インデックスは UTF-8（BOM なし）。
        // 容量削減のため、HTML 埋め込み用エスケープは緩めにし、インデント無しで出力する。
        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            PropertyNamingPolicy = null  // プロパティ名（u/t/k/s/x）をそのまま小文字 1 文字で出力
        };
        var json = JsonSerializer.Serialize(items, jsonOptions);

        var outputFile = Path.Combine(_config.OutputDirectory, "search-index.json");
        PathUtil.EnsureParentDirectory(outputFile);
        await File.WriteAllTextAsync(outputFile, json, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"search-index.json: {items.Count} 件");
    }

    /// <summary>
    /// 検索インデックスの「読み」フィールド用に文字列を正規化する。
    /// 全角カタカナ → ひらがな、英数字 → 小文字、空白除去。JS 側でクエリも同じ正規化を行うことで
    /// マッチ判定がシンプルになる。
    /// </summary>
    private static string NormalizeForSearch(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = new char[s.Length];
        int idx = 0;
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch)) continue; // 空白除去
            char c = ch;
            // 全角カタカナをひらがなに：U+30A1〜U+30F6 を U+3041〜U+3096 にシフト。
            if (c >= '\u30A1' && c <= '\u30F6') c = (char)(c - 0x60);
            // 半角英大文字を小文字に。
            else if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            chars[idx++] = c;
        }
        return new string(chars, 0, idx);
    }

    /// <summary>シリーズ種別コードを日本語ラベルに変換（検索結果のサブテキスト用）。</summary>
    private static string SeriesKindLabel(string kindCode) => kindCode switch
    {
        "TV" => "TV シリーズ",
        "MOVIE" => "映画",
        "SHORT" => "短編",
        "SPIN_OFF" => "スピンオフ",
        _ => kindCode
    };

    /// <summary>キャラ種別コードを日本語ラベルに変換（検索結果のサブテキスト用）。</summary>
    private static string CharacterKindLabel(string kindCode) => kindCode switch
    {
        "PRECURE" => "プリキュア",
        "ALLY" => "仲間",
        "VILLAIN" => "敵",
        "SUPPORTING" => "サブキャラ",
        _ => kindCode
    };

    /// <summary>
    /// 子作品判定：親シリーズが存在し、かつ自分が SPIN-OFF ではない場合は子作品扱い。
    /// 子作品（秋映画併映短編・子映画など）は単独詳細ページを生成しないため、
    /// 検索インデックスからも除外する。SeriesGenerator 側と同じ判定ロジック。
    /// </summary>
    private static bool IsChildOfMovie(PrecureDataStars.Data.Models.Series s)
    {
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// 検索インデックス JSON のアイテム 1 件分。プロパティ名は短縮形（容量削減のため）。
    /// </summary>
    private sealed class SearchIndexItem
    {
        public string u { get; set; } = "";
        public string t { get; set; } = "";
        public string k { get; set; } = "";
        public string s { get; set; } = "";
        public string x { get; set; } = "";
    }
}
