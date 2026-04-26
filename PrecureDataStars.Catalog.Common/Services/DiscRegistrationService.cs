using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// CDAnalyzer / BDAnalyzer から共通利用される、ディスク登録・照合ビジネスロジック。
/// <para>
/// MCN・CDDB-ID・TOC 曖昧一致の優先順位でディスクを検索し、
/// 見つかれば反映、見つからなければ商品選択 or 新規作成を案内する。
/// </para>
/// <para>
/// <b>同期の原則</b>: CDAnalyzer / BDAnalyzer は「ディスクから直接読み取れる物理情報」のみを
/// 提供するツールであり、Catalog 側で磨いたタイトル・種別・SONG/BGM 紐付け等を上書きしてはならない。
/// 既存ディスクに対する同期は <see cref="SyncPhysicalInfoAsync"/> を使う。全列置換の
/// <see cref="CommitAllAsync"/> は新規登録パスからのみ呼ばれる内部メソッドである。
/// </para>
/// </summary>
public sealed class DiscRegistrationService
{
    private readonly DiscsRepository _discsRepo;
    private readonly ProductsRepository _productsRepo;
    private readonly TracksRepository _tracksRepo;

    /// <summary>
    /// <see cref="DiscRegistrationService"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="discsRepo">ディスクリポジトリ。</param>
    /// <param name="productsRepo">商品リポジトリ。</param>
    /// <param name="tracksRepo">トラックリポジトリ。</param>
    public DiscRegistrationService(DiscsRepository discsRepo, ProductsRepository productsRepo, TracksRepository tracksRepo)
    {
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
    }

    /// <summary>
    /// ディスク照合結果。候補一覧と、一意に特定できたかの判定を保持する。
    /// </summary>
    public sealed class MatchResult
    {
        /// <summary>候補ディスク一覧（0 件なら未該当）。</summary>
        public IReadOnlyList<Disc> Candidates { get; init; } = Array.Empty<Disc>();

        /// <summary>一意に特定できた場合の唯一のディスク。</summary>
        public Disc? UniqueMatch { get; init; }

        /// <summary>照合に使用したキー種別（UI 表示用）。</summary>
        public string MatchedBy { get; init; } = "";
    }

    /// <summary>
    /// CD-DA 用: 優先順位（MCN → CDDB-ID → TOC 曖昧）で既存ディスクを検索する。
    /// <para>
    /// v1.1.1 より、動画メディア (BD/DVD) の照合は別メソッド
    /// <see cref="FindCandidatesForVideoAsync"/> に分離した。
    /// </para>
    /// </summary>
    /// <param name="mcn">MCN（無ければ null）。</param>
    /// <param name="cddbDiscId">freedb 互換 Disc ID（無ければ null）。</param>
    /// <param name="totalTracks">総トラック数（TOC 曖昧照合用）。</param>
    /// <param name="totalLengthFrames">総尺フレーム数（TOC 曖昧照合用、1/75 秒単位）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<MatchResult> FindCandidatesForCdAsync(
        string? mcn,
        string? cddbDiscId,
        byte totalTracks,
        uint totalLengthFrames,
        CancellationToken ct = default)
    {
        // 1. MCN 完全一致
        if (!string.IsNullOrWhiteSpace(mcn))
        {
            var byMcn = await _discsRepo.FindByMcnAsync(mcn, ct).ConfigureAwait(false);
            if (byMcn.Count > 0)
            {
                return new MatchResult
                {
                    Candidates = byMcn,
                    UniqueMatch = byMcn.Count == 1 ? byMcn[0] : null,
                    MatchedBy = "MCN"
                };
            }
        }

        // 2. CDDB Disc ID 完全一致
        if (!string.IsNullOrWhiteSpace(cddbDiscId))
        {
            var byCddb = await _discsRepo.FindByCddbIdAsync(cddbDiscId, ct).ConfigureAwait(false);
            if (byCddb.Count > 0)
            {
                return new MatchResult
                {
                    Candidates = byCddb,
                    UniqueMatch = byCddb.Count == 1 ? byCddb[0] : null,
                    MatchedBy = "CDDB"
                };
            }
        }

        // 3. TOC 曖昧一致（トラック数完全一致 + 総尺 ±75 フレーム ≒ ±1 秒）
        if (totalTracks > 0 && totalLengthFrames > 0)
        {
            var byToc = await _discsRepo.FindByTocFuzzyForCdAsync(totalTracks, totalLengthFrames, 75u, ct).ConfigureAwait(false);
            if (byToc.Count > 0)
            {
                return new MatchResult
                {
                    Candidates = byToc,
                    UniqueMatch = byToc.Count == 1 ? byToc[0] : null,
                    MatchedBy = "TOC"
                };
            }
        }

        // 未該当
        return new MatchResult();
    }

    /// <summary>
    /// BD/DVD 用: TOC 曖昧（チャプター数 + 総尺 ms）のみで既存ディスクを検索する。
    /// <para>
    /// 動画メディアは MCN・CDDB-ID が取得できないため、TOC 曖昧照合のみがフォールバックとなる。
    /// v1.1.1 で新設。
    /// </para>
    /// </summary>
    /// <param name="numChapters">チャプター数。</param>
    /// <param name="totalLengthMs">総尺（ミリ秒）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<MatchResult> FindCandidatesForVideoAsync(
        ushort numChapters,
        ulong totalLengthMs,
        CancellationToken ct = default)
    {
        // チャプター数完全一致 + 総尺 ±1000 ms（≒ ±1 秒、CD の ±1 秒相当）
        if (numChapters > 0 && totalLengthMs > 0)
        {
            var byToc = await _discsRepo.FindByTocFuzzyForVideoAsync(numChapters, totalLengthMs, 1000UL, ct).ConfigureAwait(false);
            if (byToc.Count > 0)
            {
                return new MatchResult
                {
                    Candidates = byToc,
                    UniqueMatch = byToc.Count == 1 ? byToc[0] : null,
                    MatchedBy = "TOC"
                };
            }
        }

        return new MatchResult();
    }

    /// <summary>
    /// <b>既存ディスクの物理情報同期専用</b>。CDAnalyzer / BDAnalyzer が読み取ったディスクが
    /// 既に DB に存在するケースで使う。
    /// <para>
    /// ディスクから直接読み取れる物理情報（MCN・TOC・LBA・尺・CD-Text・CDDB-ID 等）のみを
    /// 上書きし、Catalog 側で磨いた情報（title、disc_kind、content_kind_code、song_recording_id、
    /// bgm_* 参照、track_title_override、notes 等）は一切保全する。
    /// </para>
    /// <para>
    /// 参考: 破壊的な全列置換は <see cref="CommitAllAsync"/>（新規登録専用）側で行う。
    /// </para>
    /// </summary>
    /// <param name="disc">CDAnalyzer / BDAnalyzer が読み取ったディスク情報。</param>
    /// <param name="tracks">同じく読み取った各トラックの物理情報。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task SyncPhysicalInfoAsync(Disc disc, IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        await _discsRepo.UpsertPhysicalInfoAsync(disc, ct).ConfigureAwait(false);
        await _tracksRepo.UpsertPhysicalInfoForDiscAsync(disc.CatalogNo, tracks, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <b>新規登録専用</b>。ディスクと付随トラック群を全列で登録・更新する（商品は既存のものを指定）。
    /// <para>
    /// このメソッドは <see cref="CreateProductAndCommitAsync"/> または「既存商品に新しいディスクを追加」
    /// ケース（新規ディスクなので既存の磨き込み情報は存在しない）からのみ呼ばれる想定。
    /// <b>既にカタログされたディスクを CDAnalyzer から同期する用途では絶対に使用しない。</b>
    /// その場合は <see cref="SyncPhysicalInfoAsync"/> を使う。
    /// </para>
    /// </summary>
    /// <param name="disc">ディスク情報（product_catalog_no は呼び出し側で設定済み）。</param>
    /// <param name="tracks">トラック一覧（catalog_no は自動付与される）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task CommitAllAsync(Disc disc, IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        await _discsRepo.UpsertAsync(disc, ct).ConfigureAwait(false);
        await _tracksRepo.ReplaceAllForDiscAsync(disc.CatalogNo, tracks, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 新規商品を作成し、そのディスクとトラックを登録する。
    /// <para>
    /// 商品の代表品番 (product.ProductCatalogNo) には、渡された disc.CatalogNo を
    /// 内部でコピーして割り当てる（1 枚目のディスクの catalog_no を商品代表品番とする運用に合わせる）。
    /// 複数枚組で「既に作成済みの商品に 2 枚目以降のディスクを追加する」ケースは
    /// <see cref="CommitAllAsync"/> を直接使うこと（product.ProductCatalogNo に 1 枚目の品番を明示セットして disc に代入）。
    /// </para>
    /// </summary>
    public async Task CreateProductAndCommitAsync(Product product, Disc disc, IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        // 代表品番 = このディスクの品番 に固定して一貫性を担保
        product.ProductCatalogNo = disc.CatalogNo;
        disc.ProductCatalogNo    = disc.CatalogNo;

        await _productsRepo.InsertAsync(product, ct).ConfigureAwait(false);
        await CommitAllAsync(disc, tracks, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 既存商品の追加ディスクとして登録する（v1.1.3 追加）。
    /// <para>
    /// 例: 既に登録済みの BOX 商品（Disc 1 だけ登録済み）に Disc 2 として新しい BD を追加する用途。
    /// 商品本体は新規作成せず、既存商品の <c>disc_count</c> を所属ディスク数 + 1 に更新したうえで、
    /// 新規ディスクを INSERT する。
    /// </para>
    /// <para>
    /// 組内番号 <c>disc_no_in_set</c> は呼び出し側で指定させず、本メソッドが自動採番する。
    /// 既存ディスクと新ディスクをまとめて品番（<c>catalog_no</c>）の昇順
    /// （<see cref="StringComparison.Ordinal"/>）でソートし、1 始まりの連番に置き換える。
    /// 既存ディスクの組内番号が 1 始まりでなかったり歯抜けだったりしても、本メソッドの実行を契機に
    /// きれいに整列される。
    /// </para>
    /// <para>
    /// 処理順序:
    /// <list type="number">
    ///   <item>指定された <paramref name="productCatalogNo"/> で商品を取得（無ければ例外）</item>
    ///   <item>既存ディスク一覧 + 新ディスクを品番昇順にソート → 1 始まり連番で再採番</item>
    ///   <item>既存ディスクの <c>disc_no_in_set</c> を <see cref="DiscsRepository.UpdateDiscNoInSetAsync"/> で更新（タイトル等は保全）</item>
    ///   <item>新ディスクの <c>product_catalog_no</c> と <c>disc_no_in_set</c> を確定し、本体を <see cref="DiscsRepository.UpsertAsync"/></item>
    ///   <item><c>disc_count</c> を「現在の所属ディスク数 + 1」に更新</item>
    ///   <item>関連トラック／チャプターを INSERT（<see cref="CommitAllAsync"/> 経由）</item>
    /// </list>
    /// MySQL のオートコミット動作のため、各ステップは個別に確定する。途中で失敗した場合の手動修正は、
    /// 既存の <see cref="CreateProductAndCommitAsync"/> と同様に呼び出し側の責務とする。
    /// </para>
    /// </summary>
    /// <param name="productCatalogNo">追加先となる既存商品の代表品番。</param>
    /// <param name="disc">新規登録するディスク（<c>CatalogNo</c> は事前に設定済みのこと）。</param>
    /// <param name="tracks">新規ディスクに紐づくトラック群。空でも可。</param>
    /// <exception cref="InvalidOperationException">指定の商品が見つからない場合。</exception>
    public async Task AttachDiscToExistingProductAsync(
        string productCatalogNo,
        Disc disc,
        IEnumerable<Track> tracks,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productCatalogNo))
            throw new ArgumentException("追加先商品の代表品番が空です。", nameof(productCatalogNo));
        if (disc is null) throw new ArgumentNullException(nameof(disc));
        if (string.IsNullOrWhiteSpace(disc.CatalogNo))
            throw new ArgumentException("新規ディスクの品番が空です。", nameof(disc));

        // 新ディスクの品番が DB 上に既存しないかを事前確認する。
        // catalog_no は discs テーブルの主キーであり、後続の DiscsRepository.UpsertAsync は
        // INSERT ... ON DUPLICATE KEY UPDATE で動くため、重複したまま実行すると
        // 既存ディスクが新ディスクの内容で上書きされてしまう（本来意図しない破壊）。
        // ここで明示的に検出して例外を投げることで、UI 側に「同じ品番が既にあるので登録しなかった」旨を
        // 伝えて操作を中断させる。
        // 論理削除済みレコードも GetByCatalogNoAsync は返すため、is_deleted を問わず重複扱いとする
        // （論理削除済み品番の再利用は明示的な削除取消フローを想定しており、本フローからは禁じる方針）。
        var duplicate = await _discsRepo.GetByCatalogNoAsync(disc.CatalogNo, ct).ConfigureAwait(false);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"品番 [{disc.CatalogNo}] は既に登録されています。別の品番を指定してください。");
        }

        // 既存商品の取得（存在チェックも兼ねる）
        var product = await _productsRepo.GetByCatalogNoAsync(productCatalogNo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"商品 [{productCatalogNo}] が見つかりません。");

        // 新ディスクのリレーションを商品に固定
        disc.ProductCatalogNo = product.ProductCatalogNo;

        // 既存ディスク + 新ディスクを品番昇順で並べ、1 始まり連番に再採番する。
        // 比較は StringComparison.Ordinal（プリキュア BD/DVD/CD は「アルファベット 4 文字 + ハイフン + 数字 4-5 桁」が大半で、
        // 単純な文字列順序が自然順と一致する）。
        // 同一品番（万一の重複）が現れた場合も決定論的に並ぶよう、StringComparer.Ordinal を明示する。
        var existingDiscs = await _discsRepo.GetByProductCatalogNoAsync(product.ProductCatalogNo, ct).ConfigureAwait(false);
        var allCatalogNos = existingDiscs
            .Select(d => d.CatalogNo)
            .Append(disc.CatalogNo)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        // 既存ディスクの組内番号を更新する（必要な行のみ）。
        // 既に正しい採番値の行は無駄な UPDATE を避けるためスキップする。
        for (int i = 0; i < allCatalogNos.Count; i++)
        {
            int newDiscNo = i + 1;
            string catalogNo = allCatalogNos[i];

            if (string.Equals(catalogNo, disc.CatalogNo, StringComparison.Ordinal))
            {
                // 新ディスクの採番値はメモリ上にだけ反映し、後段の Upsert で書き込む
                disc.DiscNoInSet = (uint)newDiscNo;
                continue;
            }

            // 既存ディスクのうち、現状の disc_no_in_set と新採番値が異なる場合のみ UPDATE を発行
            var current = existingDiscs.First(d => string.Equals(d.CatalogNo, catalogNo, StringComparison.Ordinal));
            if ((int?)current.DiscNoInSet != newDiscNo)
            {
                await _discsRepo.UpdateDiscNoInSetAsync(catalogNo, newDiscNo, disc.UpdatedBy, ct).ConfigureAwait(false);
            }
        }

        // disc_count を所属ディスク数 + 1 に更新（再採番後の連番の終端に一致する）
        product.DiscCount = (byte)(existingDiscs.Count + 1);
        product.UpdatedBy = disc.UpdatedBy ?? product.UpdatedBy;
        await _productsRepo.UpdateAsync(product, ct).ConfigureAwait(false);

        // 新ディスク本体 + トラック / チャプターの登録
        await CommitAllAsync(disc, tracks, ct).ConfigureAwait(false);
    }
}