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
}
