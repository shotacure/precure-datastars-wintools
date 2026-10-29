using System.Text.RegularExpressions;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.DeezerIsrcSync;

/// <summary>
/// 商品ごとに Deezer の配信アルバムを特定し、CD のトラックに配信版の ISRC を書き込む。
/// <para>
/// アルバムの候補は次の順に集める。
/// <list type="number">
///   <item>前回照らしたアルバム（<c>products.deezer_album_id</c>）</item>
///   <item>品番から組んだ JAN（マーベラスの品番 <c>MJxx-NNNNN</c> は JAN が <c>4535506</c>＋数字 5 桁＋チェックデジット）で <c>/album/upc:</c> を引いたもの</item>
///   <item>CD の ISRC を何曲か <c>/track/isrc:</c> で引き、その曲が入っているアルバム</item>
/// </list>
/// 3 は、1・2 に「曲の並びが CD と同じで、CD の ISRC の半分以上が入っている」アルバムが無いときだけ引く。
/// 同じ ISRC を持つ別の盤（主題歌の曲が入ったボーカルベストなど）も出てくるので、1 曲だけでは決めない。
/// </para>
/// <para>
/// 候補のうち、何枚目ごとの曲数が CD と同じで、同じ位置の曲の番号が入れ替わっていない（番号の違う位置に CD の別の位置の番号が来ていない）
/// ものだけを採り、CD の ISRC との重なりがいちばん多いものに決める。番号が CD と違う曲があっても採り、その曲も配信の番号で書く。
/// ただし ISRC から引いて決めたアルバムに CD と違う番号があり、そのアルバムがほかの盤のもの（この回で CD と全曲同じ番号で決まった盤か、
/// 前回そのアルバムに決まった盤）なら、書かずに「ほかの盤のアルバム」とする（CD+DVD 盤に、カラオケの番号だけが違う通常盤のアルバムが当たるため）。
/// 重なりが同じなら、前回のアルバム → JAN で引けたアルバム → ISRC から引いたアルバムの順に先にする
/// （配信で番号が振り直された盤は、CD の ISRC からはそのアルバムに届かないことがあるため）。
/// ISRC から引いたアルバムどうしで重なりが同じで曲目の ISRC が違うものが並んだら、決めずに「候補が割れた」とする。
/// トラックは「何枚目（CD のディスクの並び）・何曲目（データトラックを除いた並び）」で突き合わせる。
/// </para>
/// </summary>
internal sealed class DeezerIsrcSyncService
{
    /// <summary>マーベラスの JAN の頭 7 桁。</summary>
    private const string MarvelousJanPrefix = "4535506";

    private static readonly Regex MarvelousCatalogNo = new(@"^MJ[A-Z]+-(\d{5})$", RegexOptions.CultureInvariant);

    /// <summary>ISRC から候補を引くときに使う曲の数（先頭・中ほど・末尾）。</summary>
    private const int IsrcSamples = 3;

    private readonly DeezerApiClient _api;
    private readonly TracksRepository _tracksRepo;
    private readonly ProductsRepository _productsRepo;

    /// <summary>アルバムの取得結果（同じ回のうちに同じアルバムを何度も引かない）。</summary>
    private readonly Dictionary<long, DeezerAlbum?> _albumCache = new();

    public DeezerIsrcSyncService(DeezerApiClient api, TracksRepository tracksRepo, ProductsRepository productsRepo)
    {
        _api = api;
        _tracksRepo = tracksRepo;
        _productsRepo = productsRepo;
    }

    /// <summary>照合して、<paramref name="dryRun"/> でなければ一致した商品を書き込む。</summary>
    /// <param name="productCatalogNo">対象商品の代表品番。null なら全商品。</param>
    /// <param name="dryRun">true なら DB へ書かない。</param>
    /// <param name="progress">商品 1 点を終えるたびに呼ぶ（表示用）。</param>
    public async Task<DeezerIsrcSyncResult> SyncAsync(
        string? productCatalogNo, bool dryRun, Action<DeezerIsrcProductOutcome>? progress = null, CancellationToken ct = default)
    {
        // ほかの盤が前回決まったアルバムを見るため、1 点だけ照らすときも全商品の行を読む
        var rows = await _tracksRepo.GetDigitalIsrcMatchRowsAsync(null, ct).ConfigureAwait(false);
        var checkedAt = DateTime.Now;
        var result = new DeezerIsrcSyncResult { Written = !dryRun };

        var products = rows.GroupBy(r => r.ProductCatalogNo, StringComparer.Ordinal).ToList();
        var storedOwners = products
            .Where(g => g.First().DeezerAlbumId is not null)
            .ToLookup(g => g.First().DeezerAlbumId!.Value, g => (g.Key, g.First().ProductTitle));

        // 1 周目：全商品のアルバムを決める（ほかの盤のアルバムかどうかは、全商品が決まってから見る）
        foreach (var group in products)
        {
            if (productCatalogNo is not null && !string.Equals(group.Key, productCatalogNo, StringComparison.Ordinal)) continue;
            result.Products.Add(await MatchProductAsync(group.ToList(), ct).ConfigureAwait(false));
        }

        // 2 周目：ISRC から引いて CD と違う番号のあるアルバムが、ほかの盤のものなら書かない
        var cleanOwners = result.Products
            .Where(o => o.Status == DeezerIsrcStatus.Matched && o.Differences.Count == 0)
            .ToLookup(o => o.AlbumId!.Value, o => (o.ProductCatalogNo, o.ProductTitle));
        foreach (var outcome in result.Products)
        {
            if (outcome.Status != DeezerIsrcStatus.Matched || !outcome.FoundByIsrc || outcome.Differences.Count == 0) continue;
            long albumId = outcome.AlbumId!.Value;
            var owner = cleanOwners[albumId].Concat(storedOwners[albumId])
                .FirstOrDefault(p => !string.Equals(p.Item1, outcome.ProductCatalogNo, StringComparison.Ordinal));
            if (owner.Item1 is null) continue;
            outcome.Status = DeezerIsrcStatus.OtherProductsAlbum;
            outcome.Note = $"album {albumId}「{outcome.AlbumTitle}」は {owner.Item1}「{owner.Item2}」のアルバム（CD と違う {outcome.Differences.Count} 曲）";
            outcome.Assignments.Clear();
        }

        foreach (var outcome in result.Products)
        {
            if (!dryRun && outcome.Status == DeezerIsrcStatus.Matched)
            {
                await _productsRepo.UpdateDeezerAlbumIdAsync(outcome.ProductCatalogNo, outcome.AlbumId!.Value, ct).ConfigureAwait(false);
                await _tracksRepo.UpdateDigitalIsrcsAsync(outcome.Assignments, checkedAt, ct).ConfigureAwait(false);
            }
            progress?.Invoke(outcome);
        }
        result.RequestCount = _api.RequestCount;
        return result;
    }

    /// <summary>商品 1 点の CD のトラック（並びは GetDigitalIsrcMatchRowsAsync のとおり）から配信アルバムを特定する。</summary>
    private async Task<DeezerIsrcProductOutcome> MatchProductAsync(List<DigitalIsrcMatchRow> rows, CancellationToken ct)
    {
        var first = rows[0];
        var outcome = new DeezerIsrcProductOutcome { ProductCatalogNo = first.ProductCatalogNo, ProductTitle = first.ProductTitle };

        // CD の並び：ディスクの順（取得順）ごとの曲。何曲目はデータトラックを除いた並びで数える
        var discs = rows.GroupBy(r => r.CatalogNo, StringComparer.Ordinal).Select(g => g.ToList()).ToList();
        var cdLayout = discs.Select(d => d.Count).ToList();
        var cdIsrcs = new HashSet<string>(rows.Where(r => !string.IsNullOrEmpty(r.Isrc)).Select(r => r.Isrc!), StringComparer.Ordinal);

        var cdFlat = discs.SelectMany(d => d).ToList();
        var candidates = new List<Candidate>();
        async Task AddCandidateAsync(long albumId, CandidateSource source)
        {
            if (candidates.Any(c => c.Album.AlbumId == albumId)) return;
            var album = await GetAlbumCachedAsync(albumId, ct).ConfigureAwait(false);
            if (album is null) return;
            var layout = album.Tracks.GroupBy(t => t.DiskNumber).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
            int overlap = album.Tracks.Count(t => t.Isrc is { } i && cdIsrcs.Contains(i));
            bool layoutMatches = layout.SequenceEqual(cdLayout);
            // 同じ位置の曲どうしを比べる。番号が違う位置で、配信の番号が CD の別の位置の番号なら、曲の並びが違う（別の盤）
            int disagree = 0;
            bool reordered = false;
            if (layoutMatches)
            {
                var digitalFlat = OrderedDisks(album).SelectMany(d => d).ToList();
                for (int i = 0; i < cdFlat.Count; i++)
                {
                    string? cd = cdFlat[i].Isrc, digital = digitalFlat[i].Isrc;
                    if (string.IsNullOrEmpty(cd) || string.IsNullOrEmpty(digital) || cd == digital) continue;
                    disagree++;
                    if (cdIsrcs.Contains(digital)) reordered = true;
                }
            }
            candidates.Add(new Candidate(album, source, layoutMatches, overlap, layout, disagree, reordered));
        }
        bool HasStrong() => candidates.Any(c => c.Acceptable && cdIsrcs.Count > 0 && c.Overlap * 2 >= cdIsrcs.Count);

        if (first.DeezerAlbumId is long previous) await AddCandidateAsync(previous, CandidateSource.Previous).ConfigureAwait(false);
        if (GuessMarvelousJan(first.ProductCatalogNo) is { } jan
            && await _api.FindAlbumIdByUpcAsync(jan, ct).ConfigureAwait(false) is long byUpc)
        {
            await AddCandidateAsync(byUpc, CandidateSource.Jan).ConfigureAwait(false);
        }
        if (!HasStrong())
        {
            foreach (var isrc in SampleIsrcs(rows))
            {
                if (await _api.FindAlbumIdByIsrcAsync(isrc, ct).ConfigureAwait(false) is long byIsrc)
                    await AddCandidateAsync(byIsrc, CandidateSource.Isrc).ConfigureAwait(false);
            }
        }

        var usable = candidates.Where(c => c.Acceptable).ToList();
        if (usable.Count == 0)
        {
            outcome.Status = candidates.Count == 0 ? DeezerIsrcStatus.NotFound : DeezerIsrcStatus.LayoutMismatch;
            outcome.Note = string.Join(" / ", candidates.Select(c =>
                $"album {c.Album.AlbumId}（{c.SourceLabel}・曲数 {string.Join("+", c.Layout)}{(c.Reordered ? "・並びが違う" : "")}・ISRC の重なり {c.Overlap}）"));
            if (outcome.Status == DeezerIsrcStatus.LayoutMismatch) outcome.Note = $"CD の曲数 {string.Join("+", cdLayout)}：" + outcome.Note;
            return outcome;
        }

        var best = usable.OrderByDescending(c => c.Overlap).ThenBy(c => c.Source).ThenBy(c => c.Album.AlbumId).First();
        var rivals = usable.Where(c => c != best && c.Overlap == best.Overlap && c.Source == best.Source
                                       && !c.Album.Tracks.Select(t => t.Isrc).SequenceEqual(best.Album.Tracks.Select(t => t.Isrc)))
                           .ToList();
        if (rivals.Count > 0)
        {
            outcome.Status = DeezerIsrcStatus.Ambiguous;
            outcome.Note = string.Join(" / ", new[] { best }.Concat(rivals).Select(c =>
                $"album {c.Album.AlbumId}「{c.Album.Title}」（{c.SourceLabel}・ISRC の重なり {c.Overlap}）"));
            return outcome;
        }

        outcome.Status = DeezerIsrcStatus.Matched;
        outcome.AlbumId = best.Album.AlbumId;
        outcome.AlbumTitle = best.Album.Title;
        outcome.Source = best.SourceLabel;
        outcome.FoundByIsrc = best.Source == CandidateSource.Isrc;
        outcome.Overlap = best.Overlap;
        outcome.CdIsrcCount = cdIsrcs.Count;
        outcome.PreviousAlbumId = first.DeezerAlbumId;
        CollectDifferences(outcome, discs, best.Album);
        return outcome;
    }

    /// <summary>アルバムの曲を「何枚目・何曲目」で CD のトラックに当て、書き込む値と CD との違いを集める。</summary>
    private static void CollectDifferences(DeezerIsrcProductOutcome outcome, List<List<DigitalIsrcMatchRow>> discs, DeezerAlbum album)
    {
        var disks = OrderedDisks(album);
        for (int d = 0; d < discs.Count; d++)
        {
            for (int i = 0; i < discs[d].Count; i++)
            {
                var cd = discs[d][i];
                var digital = disks[d][i];
                if (string.IsNullOrEmpty(digital.Isrc)) continue;
                outcome.Assignments.Add(new DigitalIsrcAssignment { CatalogNo = cd.CatalogNo, TrackNo = cd.TrackNo, DigitalIsrc = digital.Isrc });
                if (!string.Equals(cd.Isrc, digital.Isrc, StringComparison.Ordinal))
                    outcome.Differences.Add(new DeezerIsrcDifference(cd.CatalogNo, cd.TrackNo, cd.Isrc, digital.Isrc, digital.Title));
                if (!string.Equals(cd.DigitalIsrc, digital.Isrc, StringComparison.Ordinal)) outcome.ChangedCount++;
            }
        }
    }

    /// <summary>アルバムの曲を何枚目ごとに、何曲目の順に並べる。</summary>
    private static List<List<DeezerTrack>> OrderedDisks(DeezerAlbum album) =>
        album.Tracks.GroupBy(t => t.DiskNumber).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(t => t.TrackPosition).ToList()).ToList();

    private async Task<DeezerAlbum?> GetAlbumCachedAsync(long albumId, CancellationToken ct)
    {
        if (_albumCache.TryGetValue(albumId, out var cached)) return cached;
        var album = await _api.GetAlbumAsync(albumId, ct).ConfigureAwait(false);
        _albumCache[albumId] = album;
        return album;
    }

    /// <summary>候補を引くのに使う CD の ISRC（先頭・中ほど・末尾。重複は除く）。</summary>
    private static IEnumerable<string> SampleIsrcs(List<DigitalIsrcMatchRow> rows)
    {
        var isrcs = rows.Where(r => !string.IsNullOrEmpty(r.Isrc)).Select(r => r.Isrc!).Distinct(StringComparer.Ordinal).ToList();
        if (isrcs.Count <= IsrcSamples) return isrcs;
        return Enumerable.Range(0, IsrcSamples)
            .Select(k => isrcs[k * (isrcs.Count - 1) / (IsrcSamples - 1)])
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>マーベラスの品番（<c>MJxx-NNNNN</c>）から JAN を組む。それ以外の品番は null。</summary>
    internal static string? GuessMarvelousJan(string productCatalogNo)
    {
        var m = MarvelousCatalogNo.Match(productCatalogNo);
        if (!m.Success) return null;
        string body = MarvelousJanPrefix + m.Groups[1].Value;
        int sum = 0;
        for (int i = 0; i < body.Length; i++) sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return body + ((10 - sum % 10) % 10);
    }

    /// <summary>候補の出どころ。値の小さいほうを、重なりが同じときに先にする。</summary>
    private enum CandidateSource { Previous = 0, Jan = 1, Isrc = 2 }

    /// <param name="Disagree">同じ位置で CD と配信の番号が違う曲の数（どちらかが空の位置は数えない）。</param>
    /// <param name="Reordered">番号が違う位置に、CD の別の位置の番号が来ている（曲の並びが違う別の盤）。</param>
    private sealed record Candidate(
        DeezerAlbum Album, CandidateSource Source, bool LayoutMatches, int Overlap, IReadOnlyList<int> Layout, int Disagree, bool Reordered)
    {
        /// <summary>
        /// 採ってよい候補か。何枚目ごとの曲数が同じで、並びが入れ替わっていないこと。番号が CD と違う曲があってもよい。
        /// ISRC から引いた候補は、さらに CD の ISRC が 1 曲以上入っていること。
        /// </summary>
        public bool Acceptable => LayoutMatches && !Reordered && (Source != CandidateSource.Isrc || Overlap > 0);

        public string SourceLabel => Source switch
        {
            CandidateSource.Previous => "前回のアルバム",
            CandidateSource.Jan => "JAN",
            _ => "ISRC"
        };
    }
}

/// <summary>照合の結果の種類。</summary>
internal enum DeezerIsrcStatus
{
    /// <summary>アルバムを特定した（書き込む）。</summary>
    Matched,
    /// <summary>候補が無かった（配信されていない・CD の ISRC も JAN も手がかりにならない）。</summary>
    NotFound,
    /// <summary>候補はあったが、何枚目ごとの曲数か曲の並びが CD と同じものが無かった。</summary>
    LayoutMismatch,
    /// <summary>ISRC から引いたアルバムに CD と違う番号があり、そのアルバムがほかの盤のもの（CD+DVD 盤に対する通常盤など。書かない）。</summary>
    OtherProductsAlbum,
    /// <summary>重なりが同じで中身の違う候補が並び、決められなかった。</summary>
    Ambiguous
}

/// <summary>商品 1 点の照合の結果。</summary>
internal sealed class DeezerIsrcProductOutcome
{
    public string ProductCatalogNo { get; set; } = "";
    public string ProductTitle { get; set; } = "";
    public DeezerIsrcStatus Status { get; set; }
    public long? AlbumId { get; set; }
    public long? PreviousAlbumId { get; set; }
    public string AlbumTitle { get; set; } = "";

    /// <summary>採ったアルバムの出どころ（前回のアルバム / JAN / ISRC）。</summary>
    public string Source { get; set; } = "";

    /// <summary>採ったアルバムを CD の ISRC から引いたか（ほかの盤のアルバムかどうかを見るのはこのときだけ）。</summary>
    public bool FoundByIsrc { get; set; }

    /// <summary>採ったアルバムに入っていた CD の ISRC の数。</summary>
    public int Overlap { get; set; }

    /// <summary>CD の ISRC の数（重複を除く）。</summary>
    public int CdIsrcCount { get; set; }

    /// <summary>決められなかった・合わなかったときの候補の説明。</summary>
    public string Note { get; set; } = "";

    public List<DigitalIsrcAssignment> Assignments { get; } = new();

    /// <summary>配信版の ISRC が CD と違うトラック（CD に ISRC が無いトラックも含む）。</summary>
    public List<DeezerIsrcDifference> Differences { get; } = new();

    /// <summary>今入っている配信版の ISRC から変わるトラックの数。</summary>
    public int ChangedCount { get; set; }
}

/// <summary>配信版の ISRC が CD と違うトラック 1 行。</summary>
internal sealed record DeezerIsrcDifference(string CatalogNo, byte TrackNo, string? CdIsrc, string DigitalIsrc, string DigitalTitle);

/// <summary>1 回の照合の結果。</summary>
internal sealed class DeezerIsrcSyncResult
{
    public List<DeezerIsrcProductOutcome> Products { get; } = new();
    public bool Written { get; set; }
    public int RequestCount { get; set; }
}
