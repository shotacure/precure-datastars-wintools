using System.Globalization;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// エピソード詳細ページ <c>/series/{slug}/{seriesEpNo}/</c> の生成。
/// 本ジェネレータがサイト全体の中核。以下を 1 ページに集約する:
/// <list type="bullet">
///   <item>サブタイトル（プレーン + ルビ + かな）</item>
///   <item>外部 URL（東映あらすじ／ラインナップ／YouTube 予告）</item>
///   <item>放送日時（duration_minutes が登録されていれば「8:30〜9:00」形式の終了時刻併記）</item>
///   <item>フォーマット表（OA / 配信(Amazon Prime) / Blu-ray・DVD の累積タイムコード）</item>
///   <item>サブタイトル文字情報（初出 / 唯一 / N年Mか月ぶり、いま現在の参照点キャプション付き）</item>
///   <item>サブタイトル文字統計（title_char_stats JSON のカテゴリ別件数表示）</item>
///   <item>パート尺偏差値（AVANT/PART_A/PART_B シリーズ内・歴代、2 段ヘッダ表）</item>
///   <item>主題歌（OP / ED / 挿入歌、本放送限定行も区別表示。テーブルではなく縦リスト 1 行表現）</item>
///   <item>クレジット階層（OP / ED、役職／名義／屋号／ロゴをそれぞれの詳細ページにリンク）</item>
///   <item>前後話ページネーション（端ボタンに「#N サブタイトル」のラベル付き）</item>
/// </list>
/// </summary>
public sealed class EpisodeGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    // ── 既存リポジトリ群 ──
    // パート / 主題歌 / クレジット / 使用音声 / クレジット階層 6 段 / 名義などの per-page・per-id
    // 取得経路はすべて BuildContext の事前展開辞書（EpisodePartsByEpisode / ThemeSongsByEpisode /
    // CreditsByEpisode / EpisodeUsesByEpisode / SongCreditsBySong / SingersByRecording /
    // CreditTree / RoleByCode / PersonAliasById）への同期 lookup に置き換え済みのため、
    // ここに残るのは「ビルド中 1 度だけ遅延ロードする小型マスタ」のリポジトリのみ。
    private readonly CreditKindsRepository _creditKindsRepo;
    // stage B-7：主題歌・挿入歌セクションの種別ラベル（OP/ED/INSERT → 「オープニング主題歌」等）を
    // <c>song_music_classes</c> マスタから引くために追加。
    private readonly SongMusicClassesRepository _songMusicClassesRepo;

    // ── クレジット種別マスタの一括キャッシュ（kind_code → 表示名）。コンストラクタでは
    // 構築しない（GetAllAsync が非同期のため）。最初に必要になったタイミングで遅延ロードする。
    private IReadOnlyDictionary<string, string>? _creditKindLabelMap;

    // ── 音楽種別マスタ（song_music_classes）のキャッシュ（class_code → name_ja）。
    // stage B-7：主題歌・挿入歌セクションで <c>episode_theme_songs.theme_kind</c>
    // ("OP" / "ED" / "INSERT") を「オープニング主題歌」「エンディング主題歌」「挿入歌」と
    // 表示文字列に翻訳するためのマスタ参照。class_code は theme_kind と同じ表記体系で運用。
    private IReadOnlyDictionary<string, string>? _songMusicClassLabelMap;

    // ── 役職マスタキャッシュ（role_code → Role）。スタッフ抽出ロジックで使う。
    private IReadOnlyDictionary<string, Role>? _roleMap;

    // ── スタッフ名リンク化（人物名義 → 人物詳細ページへのリンク化用） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── 役職コードリンク化：エピソード詳細のスタッフセクションで
    //    脚本／絵コンテ／演出／作画監督／美術 の各役職ラベルを役職統計ページ
    //    /creators/roles/{rep_role_code}/ にリンクするのに使う。
    //    系譜代表の role_code を引くだけのため、Persons/CompaniesGenerator と同じ Resolver を共有する。
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    // ── 使用音声（episode_uses）の表示ラベル解決用マスタリポジトリ群 ──
    // episode_uses 行そのものは BuildContext.EpisodeUsesByEpisode から引く。
    // 各マスタ（トラック内容種別 / サイズ違い / パート違い）は初回参照時に 1 度だけ全件ロードして
    // 下記の遅延キャッシュ辞書に保持する（旧実装は使用音声を持つエピソードごとに毎回 GetAllAsync を
    // 発火しており、ページ数分の重複クエリが走っていた）。
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;

    // ── 使用音声マスタの遅延キャッシュ（ビルド中 1 度だけロードして全エピソードで使い回す） ──
    private IReadOnlyDictionary<string, TrackContentKind>? _trackKindMap;
    private IReadOnlyDictionary<string, SongSizeVariant>? _sizeVariantMap;
    private IReadOnlyDictionary<string, SongPartVariant>? _partVariantMap;

    // ── パート尺ヒストグラム（偏差値ゲージの背景分布）のキャッシュ ──
    // 偏差値 25〜75 を等幅ビンに割った棒高さ %（0〜100）の配列。シリーズ内スコープは
    // (series_id, part_type)、歴代全体スコープは part_type をキーに持つ。
    // 母集団・偏差値式は順位算出 SQL（GetAllPartLengthStatsAsync）と完全に一致させており、
    // 並列フェーズ前に EnsureSharedMastersLoadedAsync で一括構築する読み取り専用キャッシュ。
    private IReadOnlyDictionary<(int SeriesId, string PartType), int[]>? _seriesPartHist;
    private IReadOnlyDictionary<string, int[]>? _globalPartHist;

    // ── 描画ヘルパ ──
    private readonly TitleCharInfoRenderer _titleCharInfo;
    private readonly CreditTreeRenderer _creditRenderer;

    public EpisodeGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinkResolver,
        RoleSuccessorResolver roleSuccessorResolver)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _staffLinkResolver = staffLinkResolver;
        _roleSuccessorResolver = roleSuccessorResolver;

        _creditKindsRepo = new CreditKindsRepository(factory);
        _songMusicClassesRepo = new SongMusicClassesRepository(factory);

        // 使用音声セクションの表示ラベル解決用マスタ Repository（初回参照時に 1 度だけ全件ロード）。
        _trackContentKindsRepo = new TrackContentKindsRepository(factory);
        _songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
        _songPartVariantsRepo = new SongPartVariantsRepository(factory);

        // サブタイトル文字情報の初出 / 唯一 / N年Mか月ぶり判定は、ビルド開始時に SiteDataLoader が
        // 1 度だけ構築した TitleCharIndex（BuildContext 共有）への辞書参照で完結させる。
        _titleCharInfo = new TitleCharInfoRenderer(_ctx.TitleCharIndex);

        // クレジットレンダラ：Catalog 側 CreditPreviewRenderer と同一仕様。
        // 名義／屋号／ロゴ／キャラ／役職の ID → 名前解決はすべて SiteDataLoader が事前展開した
        // BuildContext 由来の辞書を直接参照する形に純化（per-id GetByIdAsync 完全撲滅）。
        // テンプレ展開時に DB 直クエリが必要なケース（例：THEME_SONGS ハンドラ）のために
        // 接続ファクトリのみ追加で受け取る。
        var lookup = new LookupCache(ctx, factory);
        // テンプレ展開時の {PERSONS} プレースホルダ等のリンク化のため、後注入で resolver を結ぶ。
        lookup.SetStaffLinkResolver(staffLinkResolver);

        // クレジット階層 6 段・役職マスタ・役職テンプレはすべて SiteDataLoader が
        // 事前展開済み（BuildContext.CreditTree / RoleByCode / RoleTemplateResolver）のため、
        // レンダラには BuildContext + 接続ファクトリ（テンプレ DSL の動的取得フック用）+
        // LookupCache + 人物リンク解決器だけを渡す。Repository 注入は不要。
        _creditRenderer = new CreditTreeRenderer(ctx, factory, lookup, staffLinkResolver);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating episodes");

        // 遅延ロード系のマスタキャッシュ（credit_kinds / song_music_classes / 使用音声マスタ 3 種）を
        // 並列フェーズに入る前に確定させる。以降の per-page 処理は共有状態への書き込みを持たない。
        await EnsureSharedMastersLoadedAsync(ct).ConfigureAwait(false);

        // ページ一覧を決定論的な順序（シリーズ順 → 話数順）で組み立てる。
        var jobs = new List<(Series Series, Episode Episode)>();
        foreach (var s in _ctx.Series)
        {
            // 子作品（parent_series_id != NULL の映画系、SPIN-OFF を除く）は単独詳細ページを
            // 持たないため、配下のエピソードページも生成しない（仕様上 credit_attach_to=SERIES なので
            // エピソード自体を持たないはずだが念のためスキップ）。
            if (IsChildOfMovie(s)) continue;
            if (!_ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps) jobs.Add((s, e));
        }

        // 2 相生成：レンダリング＋ファイル書き出し（出力先はページごとに別パス）は並列、
        // サマリ・進捗・sitemap 記録だけを元順序で逐次に行う。
        // ページ間に依存は無く、レンダ経路（BuildContext 辞書 / CreditTreeRenderer / LookupCache /
        // StaffNameLinkResolver / ScribanRenderer）は読み取り専用またはスレッドセーフであることを
        // 確認済み。記録を逐次に分離することで、sitemap.xml の URL 並びがビルドごとに揺れない
        // 決定論を保ったまま、レンダリングとファイル書き出しの時間をコア数で割る。
        var urlPaths = new string[jobs.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, jobs.Count),
            new ParallelOptions { CancellationToken = ct },
            async (i, token) =>
            {
                urlPaths[i] = await RenderOneAsync(jobs[i].Series, jobs[i].Episode, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        foreach (var urlPath in urlPaths)
        {
            _page.RecordWritten(urlPath, "episodes");
        }
        _ctx.Logger.Success($"episodes: {jobs.Count} ページ");
    }

    /// <summary>
    /// per-page の遅延初期化に頼っていたマスタキャッシュ群を、並列レンダリング開始前に一括で確定させる。
    /// 並列フェーズ中の lazy-init は「複数スレッドが同時に null 判定を通過して二重ロードする」無駄と
    /// 紙一重のため、ここで先に温めておく（以降の per-page 経路は読み取りのみになる）。
    /// </summary>
    private async Task EnsureSharedMastersLoadedAsync(CancellationToken ct)
    {
        EnsureThemeMastersLoaded();
        _roleMap ??= _ctx.RoleByCode;
        if (_creditKindLabelMap is null)
        {
            var allKinds = await _creditKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
            _creditKindLabelMap = allKinds.ToDictionary(k => k.KindCode, k => k.NameJa, StringComparer.Ordinal);
        }
        if (_songMusicClassLabelMap is null)
        {
            var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _songMusicClassLabelMap = allClasses.ToDictionary(c => c.ClassCode, c => c.NameJa, StringComparer.Ordinal);
        }
        if (_trackKindMap is null)
        {
            _trackKindMap = (await _trackContentKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        }
        if (_sizeVariantMap is null)
        {
            _sizeVariantMap = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        }
        if (_partVariantMap is null)
        {
            _partVariantMap = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        }
        if (_seriesPartHist is null || _globalPartHist is null)
        {
            BuildPartLengthHistograms();
        }
        _subtitleBuildPointCaption ??= BuildSubtitleCoverageCaption();
    }

    // ── サブタイトル分析専用の参照点キャプション（全ページ共通なので 1 度だけ組み立てる） ──
    private string? _subtitleBuildPointCaption;

    /// <summary>
    /// サブタイトル分析の参照点キャプションを組み立てる。
    /// 分析の母集団（<see cref="Pipeline.TitleCharIndex"/>）は「サブタイトル文字統計が登録済みの
    /// 全エピソード」であり、放送済みかどうかを問わない（先行判明したサブタイトルも登録され次第
    /// 比較対象になる）。そのためパート尺統計と同じ「最新放送済話」ではなく、サブタイトル統計
    /// ページのカバレッジラベルと同じ <see cref="StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle"/>
    /// （サブタイトル登録済みの最新 TV 話。未放送回も対象）を参照点にする。
    /// 表記はパート尺統計側と同一書式で、未放送回が参照点のときは未来日付になり得る
    /// （例:「2026年6月28日現在 『名探偵プリキュア！』第22話時点」）。
    /// </summary>
    private string BuildSubtitleCoverageCaption()
    {
        return BuildLatestAiredCaption(StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle(_ctx));
    }

    /// <summary>偏差値ゲージ背景のヒストグラムのビン数。ビン幅は (75-25)/25 = 偏差値 2.0 刻み。</summary>
    private const int HistBinCount = 25;

    /// <summary>
    /// パート尺ヒストグラムを全 (シリーズ, パート種別) と全 (歴代, パート種別) について一括構築する。
    /// 母集団は順位・偏差値 SQL（<see cref="EpisodePartsRepository.GetAllPartLengthStatsAsync"/>）と
    /// 同一：非削除エピソード × AVANT/PART_A/PART_B の SUM(oa_length)。偏差値式も SQL と同じ
    /// 50 + 10 × (x - AVG) / STDDEV_POP を使い、ゲージ上のマーカー位置と分布が正確に重なるようにする。
    /// </summary>
    private void BuildPartLengthHistograms()
    {
        var statParts = new[] { "AVANT", "PART_A", "PART_B" };
        var seriesValues = new Dictionary<(int SeriesId, string PartType), List<int>>();
        var globalValues = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var (episodeId, parts) in _ctx.EpisodePartsByEpisode)
        {
            // EpisodeById は SiteDataLoader が非削除のみロード済み。引けないエピソードは母集団外。
            if (!_ctx.EpisodeById.TryGetValue(episodeId, out var ep)) continue;

            foreach (var grp in parts
                .Where(p => statParts.Contains(p.PartType, StringComparer.Ordinal) && p.OaLength.HasValue)
                .GroupBy(p => p.PartType, StringComparer.Ordinal))
            {
                int sum = grp.Sum(p => (int)p.OaLength!.Value);

                var seriesKey = (ep.SeriesId, grp.Key);
                if (!seriesValues.TryGetValue(seriesKey, out var sList))
                {
                    seriesValues[seriesKey] = sList = new List<int>();
                }
                sList.Add(sum);

                if (!globalValues.TryGetValue(grp.Key, out var gList))
                {
                    globalValues[grp.Key] = gList = new List<int>();
                }
                gList.Add(sum);
            }
        }

        _seriesPartHist = seriesValues.ToDictionary(kv => kv.Key, kv => BuildHistogramBins(kv.Value));
        _globalPartHist = globalValues.ToDictionary(kv => kv.Key, kv => BuildHistogramBins(kv.Value), StringComparer.Ordinal);
    }

    /// <summary>尺秒のリストを偏差値 25〜75 のビンに割り、最頻ビン = 100 で正規化した高さ % 配列を返す。
    /// 範囲外の偏差値は端のビンに丸める（ゲージマーカーのクランプと同じ扱い）。
    /// 件数 1 以上のビンは最低 6% の高さを保証して「存在するが少ない」が見えるようにする。</summary>
    private static int[] BuildHistogramBins(List<int> seconds)
    {
        var counts = new int[HistBinCount];
        int n = seconds.Count;
        if (n == 0) return counts;

        double mean = seconds.Average();
        double std = Math.Sqrt(seconds.Sum(v => (v - mean) * (v - mean)) / n);

        foreach (var v in seconds)
        {
            // SQL 側は NULLIF(std, 0) で偏差値 NULL（≒ 全話同尺）になるが、ここでは中央ビンに積む。
            double hensachi = std == 0 ? 50.0 : 50.0 + 10.0 * (v - mean) / std;
            int bin = (int)Math.Floor((hensachi - 25.0) / 50.0 * HistBinCount);
            counts[Math.Clamp(bin, 0, HistBinCount - 1)]++;
        }

        int max = counts.Max();
        return counts
            .Select(c => c == 0 ? 0 : Math.Max(6, (int)Math.Round(c * 100.0 / max)))
            .ToArray();
    }

    /// <summary>
    /// 子作品判定：親シリーズが存在し、かつ自分が SPIN-OFF ではない場合は子作品扱い。
    /// ただし <c>kind_code == 'TV'</c> のシリーズは親シリーズ（<c>parent_series_id</c>）の
    /// 有無に関わらず単独のエピソード詳細ページを持つため、子作品扱いには決してしない（映画子作品のみを除外する）。
    /// 子作品（秋映画併映短編・子映画など）は単独詳細ページを生成しないため、
    /// 配下のエピソードページも生成しない。
    /// 本判定は <c>ParentSeriesId</c> の有無と SPIN-OFF 除外で判定する独自ロジックであり、
    /// <c>kind_code == 'MOVIE_SHORT'</c> のみで判定する
    /// <see cref="Utilities.SeriesClassifier.IsMovieShortChild"/>（シリーズ索引・一覧・ホーム集計用）
    /// とは判定基準が異なる。両者は意図的に別物として併存させているため統合しないこと。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
    {
        // kind_code='TV' のシリーズは親シリーズ（parent_series_id）の有無に関わらず
        // 単独のエピソード詳細ページを持つ。映画子作品扱い（配下エピソード非生成）には決して含めない。
        if (string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) return false;
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>エピソード 1 件分の詳細ページをレンダリングしてファイルへ書き出し、URL パスを返す。
    /// 並列レンダリングフェーズから複数スレッドで同時に呼ばれるため、本メソッド以下の経路は
    /// 共有状態への書き込みを行わない（出力ファイルパスはページごとに異なるため書き出しは安全。
    /// サマリ・sitemap 記録は呼び出し側が逐次フェーズで行う）。</summary>
    private async Task<string> RenderOneAsync(Series series, Episode ep, CancellationToken ct)
    {
        // 同シリーズ内の前後話を引き当てる（一覧から自分のインデックスを探す）。
        var siblings = _ctx.EpisodesBySeries[series.SeriesId];
        var idx = -1;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].EpisodeId == ep.EpisodeId) { idx = i; break; }
        }
        Episode? prev = (idx > 0) ? siblings[idx - 1] : null;
        Episode? next = (idx >= 0 && idx + 1 < siblings.Count) ? siblings[idx + 1] : null;

        // パート群とフォーマット表（本放送は ep.OnAirAt を起点に絶対時刻、配信は series.vod_intro を起点に累積秒）。
        // パート行は SiteDataLoader が全件ロード済み（episode_seq 昇順を維持）の辞書から引く。
        var parts = _ctx.EpisodePartsByEpisode.TryGetValue(ep.EpisodeId, out var cachedParts)
            ? cachedParts
            : Array.Empty<EpisodePart>();
        var formatTable = FormatTableBuilder.Build(parts, ep.OnAirAt, series.VodIntro, _ctx);

        // パート尺偏差値（AVANT/PART_A/PART_B のみ。対象パートが無い場合は空リスト）。
        // 表内の値表記から接頭辞「○○プリキュア内」「歴代」を取り除き、ヘッダ側で
        // 「『○○プリキュア』 / 歴代プリキュア全体」を 2 段ヘッダで示す方針。
        // 全エピソード分の偏差値は SiteDataLoader でビルド開始時に 1 度だけ算出して
        // BuildContext に詰めてあるので、本ループでは episode_id 経由の辞書参照に切り替える
        // （per-page で全件 CTE 集計を繰り返さないため）。
        var partLengthStats = _ctx.PartLengthStatsByEpisode.TryGetValue(ep.EpisodeId, out var cachedPartStats)
            ? cachedPartStats
            : (IReadOnlyList<EpisodePartsRepository.PartLengthStat>)Array.Empty<EpisodePartsRepository.PartLengthStat>();
        // カード見出しに出す「この回の実尺」。統計の母集団と同じく OA 尺をパート種別ごとに合算する
        // （同種パートが 1 話に複数あるケースも統計 SQL の SUM(oa_length) と揃う）。
        var oaSecondsByPartType = parts
            .Where(p => p.OaLength.HasValue)
            .GroupBy(p => p.PartType)
            .ToDictionary(g => g.Key, g => g.Sum(p => (int)p.OaLength!.Value));
        var partLengthStatRows = partLengthStats.Select(s => new PartLengthStatRow
        {
            PartName = s.PartTypeNameJa,
            PartCss = FormatTableBuilder.PaletteCss(s.PartType),
            DurationLabel = oaSecondsByPartType.TryGetValue(s.PartType, out var oaSec)
                ? HtmlUtil.FormatSeconds(oaSec)
                : "",
            SeriesRank = s.SeriesRank,
            SeriesTotal = s.SeriesTotal,
            SeriesHensachi = s.SeriesHensachi.ToString("0.00"),
            SeriesGaugePct = HensachiGaugePercent(s.SeriesHensachi),
            SeriesHist = _seriesPartHist!.TryGetValue((series.SeriesId, s.PartType), out var sHist)
                ? sHist
                : Array.Empty<int>(),
            GlobalRank = s.GlobalRank,
            GlobalTotal = s.GlobalTotal,
            GlobalHensachi = s.GlobalHensachi.ToString("0.00"),
            GlobalGaugePct = HensachiGaugePercent(s.GlobalHensachi),
            GlobalHist = _globalPartHist!.TryGetValue(s.PartType, out var gHist)
                ? gHist
                : Array.Empty<int>()
        }).ToList();

        // パート尺統計表のヘッダ用に、当該シリーズの正式タイトル（series.title）をテンプレに渡す。
        // 後段：略称（series.title_short）は生成・UI ともに一切使わない方針に変更し、
        // シリーズ表記は正式名（Title）を使う。プロパティ名は SeriesTitleShortQuoted の
        // ままだが（既存テンプレ参照との互換のため）、中身は常に正式タイトルの『〜』囲み文字列となる。
        string seriesTitleShortQuoted = $"『{series.Title}』";

        // 文字情報 HTML を作る（既存 BuildTitleInformationPerCharAsync の移植）。
        string titleCharInfoHtml = "";
        if (!string.IsNullOrEmpty(ep.TitleCharStats))
        {
            titleCharInfoHtml = await _titleCharInfo.RenderAsync(ep, ct).ConfigureAwait(false);
        }
        else
        {
            _ctx.Logger.Warn(
                $"title_char_stats が未生成: episode_id={ep.EpisodeId} ({series.Slug} #{ep.SeriesEpNo})。" +
                "Catalog 側のサブタイトル編集で再計算してください。");
        }

        // 主題歌（OP / ED / 挿入歌）。
        // episode_theme_songs.seq 列が「エピソード内の劇中順」を表す汎用カラムに
        // 変わったため、ソートは (is_broadcast_only, seq) の単純昇順だけで劇中流れる順番
        // どおりに並ぶ。OP/ED が冒頭・末尾とは限らない作品でも、運用者が seq に任意の順を
        // 入れていれば自然に再現される。
        // 本放送限定行（is_broadcast_only=1）は通常行の後ろに並ぶ扱い。
        var themes = (_ctx.ThemeSongsByEpisode.TryGetValue(ep.EpisodeId, out var cachedThemes)
                ? cachedThemes
                : (IReadOnlyList<EpisodeThemeSong>)Array.Empty<EpisodeThemeSong>())
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => x.Seq)
            .ToList();
        var themeRows = await BuildThemeRowsAsync(themes, ct).ConfigureAwait(false);

        // クレジット階層（エピソードスコープのもののみ）。
        static int KindOrder(string k) => k switch { "OP" => 1, "ED" => 2, _ => 999 };
        // クレジット行は SiteDataLoader が全件ロード済み（is_deleted=0、credit_seq, credit_id 昇順）の
        // 辞書から引く。保険の論理削除フィルタは旧経路と同じく残す。
        var credits = (_ctx.CreditsByEpisode.TryGetValue(ep.EpisodeId, out var cachedCredits)
                ? cachedCredits
                : (IReadOnlyList<Credit>)Array.Empty<Credit>())
            .Where(c => !c.IsDeleted)
            .OrderBy(c => KindOrder(c.CreditKind))
            .ThenBy(c => c.CreditKind, StringComparer.Ordinal)
            .ToList();

        // credit_kinds マスタから日本語名（"オープニングクレジット" / "エンディングクレジット" 等）を取り出す。
        // プレビュー側の RenderOneCreditFromDbAsync と同じ参照ルート。
        if (_creditKindLabelMap is null)
        {
            var allKinds = await _creditKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
            _creditKindLabelMap = allKinds.ToDictionary(k => k.KindCode, k => k.NameJa, StringComparer.Ordinal);
        }

        var creditBlocks = new List<CreditBlockView>();
        foreach (var c in credits)
        {
            var html = await _creditRenderer.RenderAsync(c, _ctx.Logger, ct).ConfigureAwait(false);
            creditBlocks.Add(new CreditBlockView
            {
                CreditKindLabel = _creditKindLabelMap.TryGetValue(c.CreditKind, out var nm) ? nm : c.CreditKind,
                Html = html
            });
        }

        // スタッフ情報（クレジット階層から脚本／絵コンテ／演出／作画監督／美術監督を抽出）。
        // クレジットセクションとは別に「主要スタッフ」セクションとして上部基本情報の近くに出す。
        var staffRows = BuildStaffRows(credits);

        // 使用音声（episode_uses）セクションをパート別に構築。
        var episodeUseSections = await BuildEpisodeUsesViewAsync(ep.EpisodeId, ct).ConfigureAwait(false);

        // 通算情報を 1 行にまとめる（基本情報を整理して行数を抑える）。
        // ラベルは「話数」「回数」を含めない短縮形（単位は値側の「第N話」「N話」「N回」が持つため、
        // タイルのラベルが長くて折り返す問題を避けつつ意味が通る）。
        // 各タイルには説明文（Help）を添え、テンプレ側でカード全体をツールチップのトリガにする。
        var totalsItems = new List<TotalsItem>
        {
            new TotalsItem
            {
                Label = "シリーズ内",
                Value = $"第{ep.SeriesEpNo}話",
                Help = $"『{series.Title}』内でのこのエピソードの話数です。"
            }
        };
        // 通算系の値は順序数ではなく累計数なので「第」を付けない（「1089話」「1103回」表記）。
        if (ep.TotalEpNo is int tep) totalsItems.Add(new TotalsItem
        {
            Label = "全プリキュアTV通算",
            Value = $"{tep}話",
            Help = "『ふたりはプリキュア』第1話から通算した、プリキュアTVシリーズ全体の話数です。"
        });
        if (ep.TotalOaNo is int toa) totalsItems.Add(new TotalsItem
        {
            Label = "全プリキュアTV通算放送",
            Value = $"{toa}回",
            Help = "『ふたりはプリキュア』第1話から通算した放送回数です。" +
                   "『映画プリキュアオールスターズNewStage』が放送された「スーパーヒーロー&ヒロイン夏休みスペシャル」（2013年8月25日）は除き、" +
                   "『ヒーリングっど♥プリキュア』と『デリシャスパーティ♡プリキュア』の各「おさらいセレクション」、" +
                   "および『映画HUGっと!プリキュア♡ふたりはプリキュア オールスターズメモリーズ』の3週連続放送は含みます。" +
                   "公式の放送1000回記念のカウントとも一致しています。"
        });
        if (ep.NitiasaOaNo is int nio) totalsItems.Add(new TotalsItem
        {
            Label = "全ニチアサ通算放送",
            Value = $"{nio}回",
            Help = "『とんがり帽子のメモル』が第29話でABC日曜朝8時30分枠へ移動し、いわゆる「ニチアサ」が始まった週から通算した放送回数です。" +
                   "ただし『新メイプルタウン物語とビックリマン』は毎週1回分としてカウントしています。" +
                   "また『ビックリマン』「きらきら特別増刊号」（1988年10月5日）は除き、" +
                   "「年末アニメ大会」（1988年12月28日）枠で振替放送となった『ビックリマン』第63話は含みます。" +
                   "プリキュア以降は全プリキュアTV通算放送と同期しています。"
        });

        // 「いま現在の参照点」キャプション。
        string buildPointCaption = BuildLatestAiredCaption(_ctx.LatestAiredTvEpisode);

        // ページネーション端ボタン用ラベル：上下ページネーションの「« 前話」「次話 »」を
        // 「« #N サブタイトル」「#N サブタイトル »」に置き換えるため。
        string prevPagerLabel = prev is not null ? $"#{prev.SeriesEpNo} {prev.TitleText}" : "";
        string nextPagerLabel = next is not null ? $"#{next.SeriesEpNo} {next.TitleText}" : "";

        // テンプレートに渡すモデル。
        var content = new EpisodeContentModel
        {
            Series = new SeriesRefView
            {
                Slug = series.Slug,
                Title = series.Title,
                // 後段：略称（title_short）は使わない方針のため常に正式タイトルを詰める。
                // プロパティ名 TitleShort は既存テンプレ参照との互換のため温存。
                TitleShort = series.Title
            },
            Episode = new EpisodeView
            {
                SeriesEpNo = ep.SeriesEpNo,
                TotalEpNo = ep.TotalEpNo?.ToString() ?? "",
                TotalOaNo = ep.TotalOaNo?.ToString() ?? "",
                NitiasaOaNo = ep.NitiasaOaNo?.ToString() ?? "",
                TitleText = ep.TitleText,
                TitleRichHtml = ep.TitleRichHtml ?? "",  // ルビ付き HTML はそのまま流す
                TitleKana = ep.TitleKana ?? "",
                // 放送日時は「2004年2月1日 8:30〜9:00」フォーマット。
                // duration_minutes が NULL（尺未登録）のエピソードは「2004年2月1日 8:30」で出す。
                OnAirDateTime = FormatJpDateTimeWithDuration(ep.OnAirAt, ep.DurationMinutes),
                ToeiAnimSummaryUrl = ep.ToeiAnimSummaryUrl ?? "",
                ToeiAnimLineupUrl = ep.ToeiAnimLineupUrl ?? "",
                YoutubeTrailerUrl = ep.YoutubeTrailerUrl ?? "",
                YoutubeId = ExtractYoutubeId(ep.YoutubeTrailerUrl),
                Notes = ep.Notes ?? ""
            },
            FormatTable = formatTable,
            TitleCharInfoHtml = titleCharInfoHtml,
            PartLengthStats = partLengthStatRows,
            SeriesTitleShortQuoted = seriesTitleShortQuoted,
            ThemeSongs = themeRows,
            CreditBlocks = creditBlocks,
            Staff = staffRows,
            EpisodeUseSections = episodeUseSections,
            Totals = totalsItems,
            BuildPointCaption = buildPointCaption,
            SubtitleBuildPointCaption = _subtitleBuildPointCaption ?? "",
            CoverageLabel = _ctx.CreditCoverageLabel,
            PrevUrl = prev != null ? PathUtil.EpisodeUrl(series.Slug, prev.SeriesEpNo) : "",
            PrevLabel = prev != null ? $"第{prev.SeriesEpNo}話 {prev.TitleText}" : "",
            NextUrl = next != null ? PathUtil.EpisodeUrl(series.Slug, next.SeriesEpNo) : "",
            NextLabel = next != null ? $"第{next.SeriesEpNo}話 {next.TitleText}" : "",
            PrevPagerLabel = prevPagerLabel,
            NextPagerLabel = nextPagerLabel,
            // 同シリーズ全話分の話数ページネーションを「圧縮表示」用に整形しておく
            // （現在話の前後 ±2 件 + 先頭・末尾 + 省略記号「…」、典型的な Web ページネーション風）。
            Pagination = BuildPagination(siblings, ep, series.Slug)
        };

        // MetaDescription を実データから動的に組み立てる。
        // 単純な定型文「N話のフォーマット表・スタッフ・主題歌情報」だと全エピソードで重複コンテンツ化し、
        // SERP の CTR にも反映されにくいため、放送日・主要スタッフ 2 役職・OP/ED の楽曲名まで含めて
        // 個別性の高い 140 字目安の説明文を作る。
        var metaDescription = BuildMetaDescription(ep, staffRows, _ctx.Config.SiteName);

        // エピソード詳細の構造化データは Schema.org の TVEpisode 型。
        string baseUrl = _ctx.Config.BaseUrl;
        string episodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "TVEpisode",
            ["name"] = ep.TitleText,
            ["episodeNumber"] = ep.SeriesEpNo,
            ["datePublished"] = ep.OnAirAt.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja",
            // 構造化データの description は MetaDescription と同じ文面を流用。これにより検索エンジンが
            // 同じエピソードに関する記述として OGP と JSON-LD を整合的に解釈できる。
            ["description"] = metaDescription
        };
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + episodeUrl;
        // 親シリーズ参照を partOfSeries に埋め込み（TVEpisode → TVSeries の入れ子）。
        var partOfSeries = new Dictionary<string, object?>
        {
            ["@type"] = "TVSeries",
            ["name"] = series.Title
        };
        if (!string.IsNullOrEmpty(baseUrl)) partOfSeries["url"] = baseUrl + PathUtil.SeriesUrl(series.Slug);
        jsonLdDict["partOfSeries"] = partOfSeries;

        // 演出役職の人物（"演出" 単独行または「絵コンテ・演出」統合行から取り出す）を director プロパティに、
        // 脚本役職の人物を creator プロパティに、それぞれ Person 型の配列として埋め込む。
        // 単独役職に複数人いる場合は配列、1 人だけでも配列のまま渡す（Schema.org 仕様上いずれも有効）。
        // 人物が居ない場合は当該プロパティを出力しない。
        var directorPersons = ExtractDirectorPersons(staffRows);
        if (directorPersons.Count > 0)
        {
            jsonLdDict["director"] = directorPersons
                .Select(name => new Dictionary<string, object?>
                {
                    ["@type"] = "Person",
                    ["name"] = name
                })
                .ToArray();
        }
        var screenplayPersons = ExtractScreenplayPersons(staffRows);
        if (screenplayPersons.Count > 0)
        {
            // 脚本は creator ロールでラップ（"脚本" という日本語の役職名を roleName に明示）。
            jsonLdDict["creator"] = screenplayPersons
                .Select(name => new Dictionary<string, object?>
                {
                    ["@type"] = "Role",
                    ["roleName"] = "脚本",
                    ["creator"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Person",
                        ["name"] = name
                    }
                })
                .ToArray();
        }

        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            // シリーズタイトルは『』で囲む（ページ <title>・OG・シェア文に共通で反映される）。
            PageTitle = $"『{series.Title}』 第{ep.SeriesEpNo}話「{ep.TitleText}」",
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュアシリーズ", Url = "/series/" },
                new BreadcrumbItem { Label = series.Title, Url = PathUtil.SeriesUrl(series.Slug) },
                new BreadcrumbItem { Label = $"第{ep.SeriesEpNo}話", Url = "" }
            },
            OgType = "video.episode",
            JsonLd = jsonLd
        };

        // レンダリングとファイル書き出しまでを並列フェーズ内で実施する。
        // サマリ・sitemap 記録は呼び出し側（GenerateAsync）が元のページ順で逐次実行する。
        _page.RenderAndWriteFile(episodeUrl, "episode-detail.sbn", content, layout);
        return episodeUrl;
    }

    /// <summary>
    /// 「いま現在」キャプションを組み立てる。例: 「2026年5月3日現在 『キミとアイドルプリキュア♪』第14話時点」。
    /// 日付とシリーズ名の間は読点ではなく空白で区切る（サイト共通のカバレッジラベル
    /// <see cref="Utilities.StatsCoverageLabel"/> と同じ書式に揃える）。
    /// 対象エピソードが存在しない場合は空文字を返す（テンプレ側で表示自体を抑止する）。
    /// シリーズ名は正式名称（<see cref="Series.Title"/>）を使う。
    /// シリーズ表記は正式名を使う（TitleShort は「『プリキュア』第N話時点」のような曖昧な表記を生むため使わない）。
    /// </summary>
    private static string BuildLatestAiredCaption((Series Series, Episode Episode)? latest)
    {
        if (latest is not { } la) return "";
        var d = la.Episode.OnAirAt;
        string seriesLabel = la.Series.Title;
        return $"{d.Year}年{d.Month}月{d.Day}日現在 『{seriesLabel}』第{la.Episode.SeriesEpNo}話時点";
    }

    /// <summary>主題歌行を表示用 DTO に変換する（縦リスト 1 行表現）。 テンプレ側で「OP「タイトル」 うた：歌唱者」のように 1 行ずつ並べる前提。 楽曲タイトルは詳細ページへのリンクを張れるよう、SongLink プロパティで URL を渡す。</summary>
    // ── 主題歌・挿入歌セクション専用：構造化クレジット表示でマスタを参照するためのキャッシュ。
    //    全エピソードのループの最初に 1 度だけロードして、以後はメモリ参照で済ませる。
    private IReadOnlyDictionary<string, Role>? _themeRolesMap;
    private IReadOnlyDictionary<int, PersonAlias>? _themePersonAliasMap;
    private IReadOnlyDictionary<int, CharacterAlias>? _themeCharacterAliasMap;

    /// <summary>主題歌・挿入歌セクション用のマスタは BuildContext で事前展開済みのため、 単に参照を結びつけるだけの軽い同期処理。旧版は per-call DB 全件 SELECT 3 本を発火していた。</summary>
    private void EnsureThemeMastersLoaded()
    {
        _themeRolesMap ??= _ctx.RoleByCode;
        _themePersonAliasMap ??= _ctx.PersonAliasById;
        _themeCharacterAliasMap ??= _ctx.CharacterAliasById;
    }

    private async Task<IReadOnlyList<ThemeSongRow>> BuildThemeRowsAsync(
        IReadOnlyList<EpisodeThemeSong> themes,
        CancellationToken ct)
    {
        // 役職マスタ・名義マスタはエピソード横断で共有する（インスタンス変数キャッシュ）。
        EnsureThemeMastersLoaded();
        var roleMap = _themeRolesMap!;
        var personAliasMap = _themePersonAliasMap!;
        var characterAliasMap = _themeCharacterAliasMap!;

        // SongRecording / Song は SiteDataLoader が BuildContext.SongRecordingById /
        // SongById に全件辞書化済み。本ヘルパーは Task ベースを維持（呼び出し側互換）しつつ
        // 内部は同期辞書 lookup で完結する。
        Task<(SongRecording? rec, Song? song)> ResolveAsync(int srId)
        {
            SongRecording? rec = _ctx.SongRecordingById.TryGetValue(srId, out var r) ? r : null;
            Song? song = null;
            if (rec is not null && _ctx.SongById.TryGetValue(rec.SongId, out var s)) song = s;
            return Task.FromResult((rec, song));
        }

        // 構造化クレジット（song_credits / song_recording_singers）は SiteDataLoader が
        // 全件辞書化済み（BuildContext.SongCreditsBySong / SingersByRecording、並びは per-id 取得と同一）。
        // 旧実装の per-song / per-recording DB 引き＋ローカルキャッシュを同期 lookup に置き換える。
        Task<IReadOnlyList<SongCredit>> GetSongCreditsAsync(int songId)
            => Task.FromResult(_ctx.SongCreditsBySong.TryGetValue(songId, out var rows)
                ? rows
                : Array.Empty<SongCredit>());

        Task<IReadOnlyList<SongRecordingSinger>> GetSingersAsync(int songRecordingId)
            => Task.FromResult(_ctx.SingersByRecording.TryGetValue(songRecordingId, out var rows)
                ? rows
                : Array.Empty<SongRecordingSinger>());

        var rows = new List<ThemeSongRow>(themes.Count);
        // seq 列が劇中順を表すため、(IsBroadcastOnly, Seq) の単純昇順だけで
        // 劇中で流れる順番に並ぶ（OP/ED/INSERT を区別する独自ソートは不要）。
        // 本放送限定行は通常行の後ろに並ぶ扱い。
        // usage_actuality='CREDITED_NOT_BROADCAST' は
        // 「クレジットされているが実際には流れていない」ので、エピソード主題歌セクションには
        // 表示しない（クレジット側だけが事実として残る）。
        // 'BROADCAST_NOT_CREDITED' は逆に「クレジットなしで流れた」なので
        // エピソード側には表示する（クレジット側は CreditInvolvementIndex 巡回で除外済み）。
        foreach (var t in themes
            .Where(x => !string.Equals(x.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => x.Seq))
        {
            var (rec, song) = await ResolveAsync(t.SongRecordingId).ConfigureAwait(false);
            // stage B-7：種別ラベルは song_music_classes マスタから NameJa を引く。
            // theme_kind と class_code は同じ表記体系（"OP" / "ED" / "INSERT" 等）で運用しているため、
            // theme_kind をそのままキーにできる。未登録コードのときは元の theme_kind 文字列を
            // フォールバックして表示（マスタ未投入や追加コードへの耐性）。
            // 遅延ロード：初回のみ song_music_classes 全件を取得して辞書化、以降はキャッシュ参照。
            if (_songMusicClassLabelMap is null)
            {
                var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
                _songMusicClassLabelMap = allClasses.ToDictionary(c => c.ClassCode, c => c.NameJa, StringComparer.Ordinal);
            }
            string kindLabel = _songMusicClassLabelMap.TryGetValue(t.ThemeKind, out var classNameJa)
                ? classNameJa
                : t.ThemeKind;

            // 楽曲詳細ページへのリンク URL を組み立てる（song_id が引けたときだけ）。
            int? songId = song?.SongId;
            string songLink = songId.HasValue ? PathUtil.SongUrl(songId.Value) : "";

            // 構造化クレジット由来の役職別 HTML を組む。
            // song_credits（作詞・作曲・編曲）と song_recording_singers（歌唱者）の両方を見て、
            // 構造化が無ければ Song.LyricistName / Song.ComposerName / Song.ArrangerName /
            // SongRecording.SingerName のフリーテキストにフォールバックする。
            string lyricsHtml = "";
            string lyricsRoleLabelHtml = "";
            string compositionHtml = "";
            string compositionRoleLabelHtml = "";
            string arrangementHtml = "";
            string arrangementRoleLabelHtml = "";
            if (song is not null)
            {
                var credits = await GetSongCreditsAsync(song.SongId).ConfigureAwait(false);
                lyricsHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Lyrics, song.LyricistName, personAliasMap);
                compositionHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Composition, song.ComposerName, personAliasMap);
                arrangementHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Arrangement, song.ArrangerName, personAliasMap);
                // 役職ラベルは roles マスタから引いてリンク化。未登録時はフォールバック固定文字列。
                lyricsRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Lyrics, roleMap, "作詞");
                compositionRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Composition, roleMap, "作曲");
                arrangementRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Arrangement, roleMap, "編曲");
            }

            string vocalistsHtml = "";
            // stage B-5：「歌」役職ラベルもリンク化。役職統計ページに飛ばすことで、
            // 他の作詞・作曲・編曲と同じ扱いに揃える。テンプレ側ではハードコード文字列「歌」の代わりに
            // 本フィールドを描画する。rec が null（録音情報なし）でもラベル自体は出すケースは無いので
            // ここでは rec != null のときだけ解決する。
            string vocalistsRoleLabelHtml = "";
            // コーラス（BACKING_VOCALS 役）の行も歌と同じ青系バッジで併出する。
            // 該当録音にコーラス行が無ければ空文字列のままで、テンプレ側でも行を出さない。
            string chorusHtml = "";
            string chorusRoleLabelHtml = "";
            if (rec is not null)
            {
                var singers = await GetSingersAsync(rec.SongRecordingId).ConfigureAwait(false);
                vocalistsHtml = BuildVocalistsHtml(singers, rec.SingerName, personAliasMap, characterAliasMap);
                vocalistsRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongRecordingSingerRoles.Vocals, roleMap, "歌");
                chorusHtml = BuildChorusHtml(singers, personAliasMap, characterAliasMap);
                if (!string.IsNullOrEmpty(chorusHtml))
                {
                    chorusRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongRecordingSingerRoles.Chorus, roleMap, "コーラス");
                }
            }

            // 表示タイトルは VariantLabel が非空ならそれを優先（SongsGenerator displayTitle 慣例）。
            // recording 単位で「(MOVIE EDIT)」等を含む完全な表示文字列が VariantLabel に入っている前提。
            string displayTitle = !string.IsNullOrEmpty(rec?.VariantLabel)
                ? rec!.VariantLabel!
                : (song?.Title ?? "(曲名未登録)");

            rows.Add(new ThemeSongRow
            {
                KindLabel = kindLabel,
                Title = displayTitle,
                SongTitle = song?.Title ?? "",
                SongLink = songLink,
                VariantLabel = "",
                SingerName = rec?.SingerName ?? "",
                LyricsHtml = lyricsHtml,
                LyricsRoleLabelHtml = lyricsRoleLabelHtml,
                CompositionHtml = compositionHtml,
                CompositionRoleLabelHtml = compositionRoleLabelHtml,
                ArrangementHtml = arrangementHtml,
                ArrangementRoleLabelHtml = arrangementRoleLabelHtml,
                VocalistsHtml = vocalistsHtml,
                VocalistsRoleLabelHtml = vocalistsRoleLabelHtml,
                ChorusHtml = chorusHtml,
                ChorusRoleLabelHtml = chorusRoleLabelHtml,
                Notes = t.Notes ?? "",
                IsBroadcastOnly = t.IsBroadcastOnly
            });
        }
        return rows;
    }

    /// <summary>構造化 song_credits 行を「PrecedingSeparator + 名義リンク」の連結 HTML に整形する。 行が無く <paramref name="fallbackText"/> が非空ならフリーテキストの HTML エスケープ平文を返す。 SongsGenerator の同名ヘルパと同等のロジック。</summary>
    private string BuildCreditRoleHtml(
        IReadOnlyList<SongCredit> rows,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var roleRows = rows
            .Where(r => string.Equals(r.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(r => r.CreditSeq)
            .ToList();
        if (roleRows.Count == 0)
        {
            return string.IsNullOrEmpty(fallbackText) ? "" : HtmlEscape(fallbackText);
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < roleRows.Count; i++)
        {
            var row = roleRows[i];
            if (i > 0) sb.Append(HtmlEscape(row.PrecedingSeparator ?? ""));
            if (personAliasMap.TryGetValue(row.PersonAliasId, out var alias))
            {
                sb.Append(_staffLinkResolver.ResolveAsHtml(row.PersonAliasId, alias.GetDisplayName()));
            }
            else
            {
                sb.Append("[alias#").Append(row.PersonAliasId).Append("]");
            }
        }
        return sb.ToString();
    }

    /// <summary>役職ラベルを <c>/creators/roles/{rep_role_code}/</c> リンク付き HTML に整形する。 SongsGenerator の同名ヘルパと同等のロジック。</summary>
    private string BuildSongRoleLabelLinkHtml(string roleCode, IReadOnlyDictionary<string, Role> roleMap, string fallbackLabel)
    {
        if (roleMap.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(roleCode);
            string href = PathUtil.CreatorsRoleUrl(string.IsNullOrEmpty(rep) ? roleCode : rep);
            return $"<a href=\"{HtmlEscape(href)}\">{HtmlEscape(role.NameJa)}</a>";
        }
        return HtmlEscape(fallbackLabel);
    }

    /// <summary>録音の歌唱者群を「キャラ(CV:声優) / 個人名義」のリンク付き HTML に整形する。 SongsGenerator の同名ヘルパと同等のロジック。</summary>
    private string BuildVocalistsHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string? fallbackSingerName,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        string html = BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Vocals, personAliasMap, characterAliasMap);
        if (!string.IsNullOrEmpty(html)) return html;
        return string.IsNullOrEmpty(fallbackSingerName) ? "" : HtmlEscape(fallbackSingerName);
    }

    /// <summary>録音のコーラス（BACKING_VOCALS 役）連名のリンク付き HTML を返す。 該当行が無ければ空文字列（VOCALS と違いフリーテキストフォールバックは持たない）。</summary>
    private string BuildChorusHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
        => BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Chorus, personAliasMap, characterAliasMap);

    /// <summary>指定 <paramref name="roleCode"/> の歌唱者行を抽出し連名 HTML に整形する内部ヘルパ。</summary>
    private string BuildSingersByRoleHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string roleCode,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var rows = singers
            .Where(s => string.Equals(s.RoleCode, roleCode, StringComparison.Ordinal))
            .OrderBy(s => s.SingerSeq)
            .ToList();
        if (rows.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            if (i > 0) sb.Append(HtmlEscape(s.PrecedingSeparator ?? ""));
            sb.Append(RenderSingerEntry(s, personAliasMap, characterAliasMap));
            if (!string.IsNullOrEmpty(s.AffiliationText))
            {
                sb.Append(' ').Append(HtmlEscape(s.AffiliationText));
            }
        }
        return sb.ToString();
    }

    private string RenderSingerEntry(
        SongRecordingSinger s,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (s.BillingKind == SingerBillingKind.Person)
        {
            string main = ResolvePersonAliasLink(s.PersonAliasId, personAliasMap);
            if (s.SlashPersonAliasId.HasValue)
            {
                string slash = ResolvePersonAliasLink(s.SlashPersonAliasId, personAliasMap);
                return $"{main} / {slash}";
            }
            return main;
        }
        else
        {
            string mainChar = ResolveCharacterAliasLink(s.CharacterAliasId, characterAliasMap);
            string charPart = mainChar;
            if (s.SlashCharacterAliasId.HasValue)
            {
                string slashChar = ResolveCharacterAliasLink(s.SlashCharacterAliasId, characterAliasMap);
                charPart = $"{mainChar}/{slashChar}";
            }
            string cv = ResolvePersonAliasLink(s.VoicePersonAliasId, personAliasMap);
            return $"{charPart}(CV:{cv})";
        }
    }

    private string ResolvePersonAliasLink(int? aliasId, IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!personAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[alias#{aliasId.Value}]";
        return _staffLinkResolver.ResolveAsHtml(aliasId, alias.GetDisplayName());
    }

    private static string ResolveCharacterAliasLink(int? aliasId, IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!characterAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[char-alias#{aliasId.Value}]";
        // CharacterAlias は PersonAlias と違い DisplayTextOverride / GetDisplayName() を持たないため、
        // 表示テキストは常に Name そのもの。
        return $"<a href=\"/characters/{alias.CharacterId}/\">{HtmlEscape(alias.Name)}</a>";
    }

    /// <summary>HTML 5 における &amp;・&lt;・&gt;・&quot;・&#39; の最小限のエスケープ。</summary>
    private static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    /// <summary>当該エピソードの episode_uses 行群をパート別にグルーピングして表示用 DTO に変換する。</summary>
    private async Task<IReadOnlyList<EpisodeUseSection>> BuildEpisodeUsesViewAsync(int episodeId, CancellationToken ct)
    {
        var uses = _ctx.EpisodeUsesByEpisode.TryGetValue(episodeId, out var cachedUses)
            ? cachedUses
            : Array.Empty<EpisodeUse>();
        if (uses.Count == 0) return Array.Empty<EpisodeUseSection>();

        // 表示ラベル解決用マスタ 3 種は初回参照時に 1 度だけロードしてビルド全体で使い回す
        // （旧実装は使用音声を持つエピソードごとに毎回 GetAllAsync を発火していた）。
        if (_trackKindMap is null)
        {
            _trackKindMap = (await _trackContentKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        }
        if (_sizeVariantMap is null)
        {
            _sizeVariantMap = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        }
        if (_partVariantMap is null)
        {
            _partVariantMap = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
                .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        }
        var trackKindMap = _trackKindMap;
        var sizeVariantMap = _sizeVariantMap;
        var partVariantMap = _partVariantMap;
        // パート種別マスタは BuildContext で事前展開済み（part_type → PartType）。
        var partTypeMap = _ctx.PartTypeByCode;

        // 楽曲・劇伴の参照は BuildContext で全件辞書化済み。本セクションで必要な ID 群だけを
        // ローカル辞書に切り出して使う（既存テンプレ側の引き方を温存するため）。
        var songRecIds = uses.Where(u => u.SongRecordingId.HasValue).Select(u => u.SongRecordingId!.Value).Distinct().ToList();
        var songRecCache = new Dictionary<int, SongRecording>();
        var songCache = new Dictionary<int, Song>();
        foreach (var rid in songRecIds)
        {
            if (!_ctx.SongRecordingById.TryGetValue(rid, out var rec)) continue;
            songRecCache[rid] = rec;
            if (!songCache.ContainsKey(rec.SongId)
                && _ctx.SongById.TryGetValue(rec.SongId, out var song))
            {
                songCache[rec.SongId] = song;
            }
        }

        // BGM cue 参照は (series_id, m_no_detail) で複合。BuildContext.BgmCuesBySeries から
        // 必要シリーズ分のみ取り出してローカルマップ化する。
        var bgmSeriesIds = uses
            .Where(u => u.BgmSeriesId.HasValue && !string.IsNullOrEmpty(u.BgmMNoDetail))
            .Select(u => u.BgmSeriesId!.Value)
            .Distinct()
            .ToList();
        var bgmCueMap = new Dictionary<(int seriesId, string mNoDetail), BgmCue>();
        foreach (var sid in bgmSeriesIds)
        {
            if (!_ctx.BgmCuesBySeries.TryGetValue(sid, out var cues)) continue;
            foreach (var cue in cues)
                bgmCueMap[(cue.SeriesId, cue.MNoDetail)] = cue;
        }

        // パート種別ごとにグルーピングして DTO 化。
        var sections = uses
            .GroupBy(u => u.PartKind)
            .Select(g =>
            {
                string label = partTypeMap.TryGetValue(g.Key, out var pt) ? pt.NameJa : g.Key;
                int order = partTypeMap.TryGetValue(g.Key, out var pt2) ? (pt2.DisplayOrder ?? byte.MaxValue) : byte.MaxValue;

                var rows = g.OrderBy(u => u.UseOrder)
                            .ThenBy(u => u.SubOrder)
                            .Select(u => BuildEpisodeUseRow(u, trackKindMap, sizeVariantMap, partVariantMap, songRecCache, songCache, bgmCueMap))
                            .ToList();

                return new { Order = order, Section = new EpisodeUseSection { PartLabel = label, Uses = rows } };
            })
            .OrderBy(x => x.Order)
            .Select(x => x.Section)
            .ToList();

        return sections;
    }

    /// <summary>1 つの <see cref="EpisodeUse"/> 行を表示用 <see cref="EpisodeUseRow"/> に変換する。</summary>
    private static EpisodeUseRow BuildEpisodeUseRow(
        EpisodeUse u,
        IReadOnlyDictionary<string, TrackContentKind> trackKindMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<int, SongRecording> songRecCache,
        IReadOnlyDictionary<int, Song> songCache,
        IReadOnlyDictionary<(int seriesId, string mNoDetail), BgmCue> bgmCueMap)
    {
        string contentKindLabel = trackKindMap.TryGetValue(u.ContentKindCode, out var ck) ? ck.NameJa : u.ContentKindCode;
        string title = "";
        string subTitle = "";
        string songLink = "";

        switch (u.ContentKindCode)
        {
            case "SONG":
                if (u.SongRecordingId is int rid && songRecCache.TryGetValue(rid, out var rec)
                    && songCache.TryGetValue(rec.SongId, out var song))
                {
                    // タイトル：use_title_override があればそちら優先（特殊表記用）、なければ歌のタイトル。
                    title = !string.IsNullOrEmpty(u.UseTitleOverride) ? u.UseTitleOverride! : song.Title;
                    songLink = PathUtil.SongUrl(song.SongId);
                    var subParts = new List<string>();
                    if (!string.IsNullOrEmpty(rec.SingerName)) subParts.Add(rec.SingerName!);
                    if (!string.IsNullOrEmpty(u.SongSizeVariantCode)
                        && sizeVariantMap.TryGetValue(u.SongSizeVariantCode!, out var sv))
                        subParts.Add(sv.NameJa);
                    if (!string.IsNullOrEmpty(u.SongPartVariantCode)
                        && partVariantMap.TryGetValue(u.SongPartVariantCode!, out var pv))
                        subParts.Add(pv.NameJa);
                    if (!string.IsNullOrEmpty(rec.VariantLabel)) subParts.Add(rec.VariantLabel!);
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = u.UseTitleOverride ?? "(歌情報未登録)";
                }
                break;

            case "BGM":
                if (u.BgmSeriesId is int bsid && u.BgmMNoDetail is string mnd
                    && bgmCueMap.TryGetValue((bsid, mnd), out var cue))
                {
                    string mNoLabel = cue.IsTempMNo ? "(Mナンバー不明)" : cue.MNoDetail;
                    title = !string.IsNullOrEmpty(u.UseTitleOverride)
                        ? u.UseTitleOverride!
                        : (cue.MenuTitle ?? "(タイトル未登録)");
                    var subParts = new List<string> { mNoLabel };
                    if (!string.IsNullOrEmpty(cue.ComposerName)) subParts.Add($"作曲: {cue.ComposerName}");
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = u.UseTitleOverride ?? "(劇伴情報未登録)";
                }
                break;

            default:
                // DRAMA / RADIO / JINGLE / OTHER 等。
                title = u.UseTitleOverride ?? "";
                break;
        }

        return new EpisodeUseRow
        {
            UseOrder = u.UseOrder,
            SubOrder = u.SubOrder,
            ContentKindLabel = contentKindLabel,
            Title = title,
            SubTitle = subTitle,
            SceneLabel = u.SceneLabel ?? "",
            DurationLabel = FormatDurationSeconds(u.DurationSeconds),
            SongLink = songLink,
            IsBroadcastOnly = u.IsBroadcastOnly
        };
    }

    /// <summary>使用尺の秒数を「m:ss」表記に整形。NULL は空文字。</summary>
    private static string FormatDurationSeconds(ushort? seconds)
    {
        if (!seconds.HasValue) return "";
        ushort s = seconds.Value;
        int min = s / 60;
        int sec = s % 60;
        return $"{min}:{sec:00}";
    }

    /// <summary>
    /// エピソード詳細ページの <c>&lt;meta name="description"&gt;</c> 用の説明文を、実データから組み立てる。
    /// 構成は下記の優先度で「シリーズ名・話数・サブタイトル・放送日 → 主要スタッフ 2 行 →
    /// 主題歌 (OP / ED) 2 曲」の順。<c>targetMaxChars</c>（140 字）を超えそうな段で打ち切り、
    /// 短く済むエピソードは尻切れにならずに自然に終わる設計とする。説明文は OG / Twitter Card にも
    /// 流用されるため、検索結果と SNS 共有プレビューの両方で読みやすい長さに収める。
    /// スタッフ抽出は <see cref="BuildStaffRowsAsync"/> の結果をそのまま再利用する（重複クエリを避けるため）。
    /// 主題歌行は OP / ED のみ採用し、挿入歌は字数節約のため description には含めない。
    /// </summary>
    private static string BuildMetaDescription(
        Episode ep,
        IReadOnlyList<StaffRow> staffRows,
        string siteName)
    {
        // meta description / og:description / twitter:description は概ね 120〜160 字程度で
        // 切り詰められるため、保守的に 140 字を目標値に置く（厳密上限ではなく、超えそうな段で
        // 追加を打ち切るためのガード値）。日本語 1 文字 = 1 char カウントで運用。
        const int targetMaxChars = 140;

        // 末尾にサイト名を必ず添える（カードにブランドを出す）。その分の文字数を先に確保し、
        // 本文（OA日付・通算・スタッフ）はサイト名を除いた予算内で打ち切る。各項目は "/" 区切り。
        var siteSuffix = string.IsNullOrEmpty(siteName) ? "" : $" — {siteName}";
        int budget = targetMaxChars - siteSuffix.Length;

        // og:title が『シリーズ』第N話「サブタイトル」を持つため、説明文ではそれを繰り返さず、
        // 放送日（OA:yyyy.M.d）・通算（全プリキュアTV通算の累計値）・主要スタッフでページ固有の情報を出す。
        var segments = new List<string>
        {
            "OA:" + ep.OnAirAt.ToString("yyyy.M.d"),
        };
        if (ep.TotalEpNo is int tep) segments.Add($"通算{tep}話");
        if (ep.TotalOaNo is int toa) segments.Add($"放送{toa}回");

        // 主要スタッフ（最大 3 役職：脚本→絵コンテ・演出系→作画監督…の順。予算内で打ち切る）。
        // 主題歌はシリーズ単位で全話共通＝そのエピソード固有の情報ではないため載せない。
        int staffAdded = 0;
        foreach (var staff in staffRows)
        {
            if (staffAdded >= 3) break;
            if (string.IsNullOrWhiteSpace(staff.NamesLine)) continue;
            // staff.NamesLine は <a href="..."> でラップされた HTML 断片を含むため、プレーンテキスト化する。
            var seg = $"{staff.RoleLabel}:{StripHtmlTags(staff.NamesLine)}";
            // 既存の "/" 連結長 ＋ "/" ＋ seg が予算超過なら採用しない（直前項目で打ち切り）。
            if (string.Join("/", segments).Length + 1 + seg.Length > budget) break;
            segments.Add(seg);
            staffAdded++;
        }

        return string.Join("/", segments) + siteSuffix;
    }

    /// <summary>スタッフ行群から「演出」役職の人物名一覧を取り出す。</summary>
    private static List<string> ExtractDirectorPersons(IReadOnlyList<StaffRow> staffRows)
    {
        foreach (var row in staffRows)
        {
            if (row.RoleLabel == "演出" || row.RoleLabel == "絵コンテ・演出")
            {
                return SplitNamesLine(row.NamesLine);
            }
        }
        return new List<string>();
    }

    /// <summary>スタッフ行群から「脚本」役職の人物名一覧を取り出す。</summary>
    private static List<string> ExtractScreenplayPersons(IReadOnlyList<StaffRow> staffRows)
    {
        foreach (var row in staffRows)
        {
            if (row.RoleLabel == "脚本")
            {
                return SplitNamesLine(row.NamesLine);
            }
        }
        return new List<string>();
    }

    /// <summary>
    /// <see cref="StaffRow.NamesLine"/> のように「、」連結された日本語人名文字列を、個別の人名リストに割る
    ///。空白要素は除外、各人名の前後空白も除去する。
    /// 注意：<see cref="StaffRow.NamesLine"/> は実体としては <c>&lt;a href="..."&gt;人名&lt;/a&gt;</c> でラップされた
    /// HTML 断片の連結である。本メソッドの結果は JSON-LD の Person.name 値として使われるため、
    /// 各エントリから HTML タグを除去してプレーンテキスト化する。
    /// </summary>
    private static List<string> SplitNamesLine(string namesLine)
    {
        if (string.IsNullOrEmpty(namesLine)) return new List<string>();
        return namesLine.Split('、', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => StripHtmlTags(n).Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    /// <summary>
    /// 入力文字列から HTML タグ（<c>&lt;...&gt;</c>）を素朴に取り除いてプレーンテキスト化する。
    /// HTML エンティティ（<c>&amp;amp;</c> 等）のデコードは行わない。スタッフ名・楽曲名のような
    /// 短い人手入力フィールドが想定で、<c>&lt;</c> のような特殊文字を含まない前提とする。
    /// 正規表現は素朴な「<c>&lt;</c> から <c>&gt;</c> までを 1 タグとみなす」非貪欲マッチで、
    /// 属性値内に <c>&gt;</c> を含むタグ（HTML としては不正だが理論上可能）は対象外。
    /// </summary>
    private static string StripHtmlTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");
    }

    /// <summary>主要スタッフ（脚本／絵コンテ／演出／作画監督／美術監督）の表示行を構築する。</summary>
    private IReadOnlyList<StaffRow> BuildStaffRows(IReadOnlyList<Credit> credits)
    {
        // 役職マスタは BuildContext で事前展開済み（role_code → Role）。
        _roleMap ??= _ctx.RoleByCode;

        // スタッフセクションは脚本／絵コンテ／演出／作画監督／美術 の 5 役職を
        // 別々のラインで出すが、絵コンテと演出が同じ人物（同じ集合）になった場合だけ
        // 「絵コンテ・演出」の 1 ラインに統合する。そのため一旦は役職コード単位で
        // (重複判定キー → 表示用 HTML) のペアリストとして集めておき、最後に行 DTO を組み立てる。
        // 抽出対象役職と、役職コード／表示名の候補。
        // role_code がリポジトリ内の役職マスタに実在する値を直接指定する。
        // 加えて name_ja 側でもマッチを許容するので、マスタが今後追加・改名されても拾いやすい。
        var staffSpecs = new[]
        {
            new StaffSpec("脚本",     new[] { "SCREENPLAY" },         new[] { "脚本" }),
            new StaffSpec("絵コンテ", new[] { "STORYBOARD" },         new[] { "絵コンテ" }),
            new StaffSpec("演出",     new[] { "EPISODE_DIRECTOR" },   new[] { "演出" }),
            new StaffSpec("作画監督", new[] { "ANIMATION_DIRECTOR" }, new[] { "作画監督" }),
            new StaffSpec("美術",     new[] { "ART_DIRECTOR" },       new[] { "美術" })
        };

        // 役職コード → スタッフ仕様の逆引きを 1 度だけ作る。
        // role_code がコード候補に一致、または当該 role の name_ja が表示名候補のいずれかに一致するとき採用。
        var roleCodeToSpec = new Dictionary<string, StaffSpec>(StringComparer.Ordinal);
        foreach (var (code, role) in _roleMap)
        {
            foreach (var spec in staffSpecs)
            {
                if (spec.RoleCodeCandidates.Contains(code, StringComparer.Ordinal)
                    || (role.NameJa is { } nm && spec.RoleNameCandidates.Contains(nm, StringComparer.Ordinal)))
                {
                    if (!roleCodeToSpec.ContainsKey(code))
                        roleCodeToSpec[code] = spec;
                }
            }
        }

        // 仕様ラベル → 集めた人物名（HTML 断片）のリスト。
        // 重複判定キーは PERSON エントリなら "P:{alias_id}"、TEXT エントリなら "T:{raw_text}" とし、
        // リンク化の有無に関わらず同一エントリを 1 度だけ表示するようにする。
        // 絵コンテと演出の同一性判定にも使うため、キー集合（HashSet<string>）も保持しておく。
        var collected = staffSpecs.ToDictionary(s => s.Label, _ => new List<string>(), StringComparer.Ordinal);
        var seen = staffSpecs.ToDictionary(s => s.Label, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        // クレジット → カード → tier → group → cardRole の順で走査して、
        // ヒット役職配下の PERSON エントリの名義を引く。
        // 階層 6 段は SiteDataLoader が事前展開済みの BuildContext.CreditTree スナップショットを辿る
        // （旧実装はページごとに階層別 GetBy*Async を発火しており、エピソード数 × 階層分の
        // DB 往復が走っていた）。スナップショット各層は per-id 取得時と同一の並び順を保持しているが、
        // 旧経路と同じ明示ソートを保険として残す。
        foreach (var credit in credits)
        {
            if (!_ctx.CreditTree.CardsByCreditId.TryGetValue(credit.CreditId, out var cardSnapshots)) continue;
            foreach (var cardSnap in cardSnapshots.OrderBy(c => c.Card.CardSeq))
            {
                foreach (var tierSnap in cardSnap.Tiers.OrderBy(t => t.Tier.TierNo))
                {
                    foreach (var grpSnap in tierSnap.Groups.OrderBy(g => g.Group.GroupNo))
                    {
                        foreach (var crSnap in grpSnap.Roles.OrderBy(r => r.Role.OrderInGroup))
                        {
                            var cr = crSnap.Role;
                            if (cr.RoleCode is null) continue;
                            if (!roleCodeToSpec.TryGetValue(cr.RoleCode, out var spec)) continue;

                            // 配下のブロックとエントリを引いて、PERSON エントリの名義のみを集める。
                            foreach (var blockSnap in crSnap.Blocks.OrderBy(b => b.Block.BlockSeq))
                            {
                                var entries = blockSnap.Entries
                                    .Where(e => !e.IsBroadcastOnly)
                                    .OrderBy(e => e.EntrySeq);
                                foreach (var e in entries)
                                {
                                    var (key, html) = ResolveStaffEntry(e);
                                    if (string.IsNullOrEmpty(html)) continue;
                                    if (seen[spec.Label].Add(key))
                                        collected[spec.Label].Add(html);
                                }
                            }
                        }
                    }
                }
            }
        }

        // 絵コンテ・演出が同一集合（同じ重複キー集合）の場合、1 ラインに統合する。
        // 例：絵コンテ＝伊藤 尚往、演出＝伊藤 尚往 → 「絵コンテ・演出 伊藤 尚往」
        // 異なる場合は 2 行に分ける（絵コンテ A、演出 B）。
        // 統合判定はキー集合の集合比較で行う（HTML 表現の文字列比較だと alias の表示揺れに弱いため）。
        bool storyboardDirectorMerged =
            seen["絵コンテ"].Count > 0
            && seen["演出"].Count > 0
            && seen["絵コンテ"].SetEquals(seen["演出"]);

        // 役職コード解決：表示順は仕様の配列順を踏襲。各行の RoleUrl は系譜代表 role_code の役職統計ページ。
        // 統合行 (絵コンテ・演出) のときは特例的に「絵コンテ用 URL」と「演出用 URL」の両方を保持し、
        // テンプレ側で「絵コンテ」「演出」それぞれを別リンクとして描画できるようにする。
        string? RoleUrl(string label) => label switch
        {
            "脚本" => CreatorsRoleUrlFor("SCREENPLAY", "脚本"),
            "絵コンテ" => CreatorsRoleUrlFor("STORYBOARD", "絵コンテ"),
            "演出" => CreatorsRoleUrlFor("EPISODE_DIRECTOR", "演出"),
            "作画監督" => CreatorsRoleUrlFor("ANIMATION_DIRECTOR", "作画監督"),
            "美術" => CreatorsRoleUrlFor("ART_DIRECTOR_TV", "美術"),
            _ => null
        };

        // 仕様の並び順で、エントリのある役職だけ DTO 化。
        // HTML 断片を「、」で連結した文字列を NamesLine に詰める。テンプレ側では html.escape を
        // かけずにそのまま出力する（PERSON エントリは既に <a> タグでラップ済み、TEXT は escape 済み）。
        var rows = new List<StaffRow>();
        foreach (var spec in staffSpecs)
        {
            var names = collected[spec.Label];
            if (names.Count == 0) continue;

            // 統合モード中は絵コンテ単体／演出単体ではなく、絵コンテ位置で 1 行だけ出す（演出はスキップ）。
            if (storyboardDirectorMerged)
            {
                if (spec.Label == "演出") continue;
                if (spec.Label == "絵コンテ")
                {
                    rows.Add(new StaffRow
                    {
                        // 表示ラベル文字列は「絵コンテ・演出」。テンプレ側でリンク分割するため、
                        // 構成役職それぞれの URL を SubRoleLinks にも詰める。
                        RoleLabel = "絵コンテ・演出",
                        // 統合行では RoleCode は空文字（テンプレ側は SubRoleLinks の各 Code を使う）。
                        RoleCode = "",
                        RoleUrl = "",
                        SubRoleLinks = new List<StaffRoleLink>
                        {
                            new StaffRoleLink { Code = "STORYBOARD",        Label = "絵コンテ", Url = RoleUrl("絵コンテ") ?? "" },
                            new StaffRoleLink { Code = "EPISODE_DIRECTOR",  Label = "演出",     Url = RoleUrl("演出")     ?? "" }
                        },
                        NamesLine = string.Join("、", names)
                    });
                    continue;
                }
            }

            // 通常モード：1 役職 1 行で素直に出す。
            rows.Add(new StaffRow
            {
                RoleLabel = spec.Label,
                // RoleCode はバッジの data-role-code に使う。staffSpecs では RoleCodeCandidates の
                // 先頭が代表コード（"SCREENPLAY" / "STORYBOARD" / 等）になる。
                RoleCode = spec.RoleCodeCandidates.Length > 0 ? spec.RoleCodeCandidates[0] : "",
                RoleUrl = RoleUrl(spec.Label) ?? "",
                SubRoleLinks = Array.Empty<StaffRoleLink>(),
                NamesLine = string.Join("、", names)
            });
        }
        return rows;
    }

    /// <summary>
    /// 指定役職コード（or 表示名フォールバック）から、役職統計詳細ページ <c>/creators/roles/{rep}/</c> の
    /// URL を組み立てる。
    /// 1) コード候補そのままが <see cref="RoleSuccessorResolver"/> のクラスタに含まれていればそれを採用。
    /// 2) 含まれていなければ表示名候補（"脚本" 等）でマスタを走査し、ヒットしたコードのクラスタ代表を採用。
    /// 3) どちらでも引けないときは <c>null</c>（テンプレ側でリンク化を抑止）。
    /// </summary>
    private string? CreatorsRoleUrlFor(string preferredRoleCode, string fallbackNameJa)
    {
        // 役職マスタが未ロードならリンク化スキップ（直前段で必ずロードしているはずだが念のため）。
        if (_roleMap is null) return null;

        // 1) 推奨コードが存在すれば、その系譜代表 → URL。
        if (_roleMap.ContainsKey(preferredRoleCode))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(preferredRoleCode);
            if (!string.IsNullOrEmpty(rep)) return PathUtil.CreatorsRoleUrl(rep);
        }

        // 2) name_ja フォールバック検索：表示名が一致する役職コードを 1 件採用。
        foreach (var (code, role) in _roleMap)
        {
            if (string.Equals(role.NameJa, fallbackNameJa, StringComparison.Ordinal))
            {
                string rep = _roleSuccessorResolver.GetRepresentative(code);
                if (!string.IsNullOrEmpty(rep)) return PathUtil.CreatorsRoleUrl(rep);
            }
        }

        return null;
    }

    /// <summary>
    /// スタッフ役職配下のエントリ 1 件から (重複判定キー, 表示用 HTML 文字列) を取り出す。
    /// PERSON エントリは <see cref="StaffNameLinkResolver"/> 経由で人物詳細ページへの &lt;a&gt; リンク HTML に
    /// 変換し、TEXT エントリは HTML エスケープのみ施したプレーンテキストにする。
    /// 重複判定キーは PERSON なら <c>"P:{alias_id}"</c>、TEXT なら <c>"T:{raw_text}"</c>。
    /// それ以外（CHARACTER_VOICE / COMPANY / LOGO）は空文字 + 空 HTML を返して呼び出し元で除外する。
    /// 所属（屋号）は表示しない（スタッフ一覧は素朴に「役職 — 名前、名前、名前」で出す方針）。
    /// 名義の解決は BuildContext.PersonAliasById（削除済み込みの全件辞書）への同期 lookup で行う
    /// （per-id GetByIdAsync の DB 往復を撲滅。旧経路の GetByIdAsync も削除済み名義を返す仕様だったため
    /// 解決結果は同一）。
    /// </summary>
    private (string Key, string Html) ResolveStaffEntry(CreditBlockEntry e)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    var pa = _ctx.PersonAliasById.TryGetValue(pid, out var alias) ? alias : null;
                    string? displayText = pa?.DisplayTextOverride ?? pa?.Name;
                    if (string.IsNullOrEmpty(displayText)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(pid, displayText);
                    return ($"P:{pid}", html);
                }
                return ("", "");
            case "TEXT":
                {
                    string raw = e.RawText ?? "";
                    if (string.IsNullOrEmpty(raw)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(null, raw);
                    return ($"T:{raw}", html);
                }
            default:
                return ("", "");
        }
    }

    /// <summary>スタッフ役職配下のエントリ 1 件から表示用の人物名を取り出す（プレーンテキスト版）。 別文脈での利用を想定したユーティリティで、本ファイル内からは参照しない。 PERSON / TEXT のときだけ採用し、CHARACTER_VOICE / COMPANY / LOGO は null を返す。 所属（屋号）は表示しない（スタッフ一覧は素朴に「役職 — 名前、名前、名前」で出す方針）。 名義解決は <see cref="ResolveStaffEntry"/> と同じく BuildContext.PersonAliasById への同期 lookup。</summary>
    private string? ResolveStaffEntryName(CreditBlockEntry e)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    var pa = _ctx.PersonAliasById.TryGetValue(pid, out var alias) ? alias : null;
                    return pa?.DisplayTextOverride ?? pa?.Name;
                }
                return null;
            case "TEXT":
                return e.RawText;
            default:
                return null;
        }
    }

    /// <summary>スタッフ役職の判定スペック。</summary>
    private sealed record StaffSpec(string Label, string[] RoleCodeCandidates, string[] RoleNameCandidates);

    /// <summary>YouTube URL から動画 ID を抽出する。失敗時は空文字を返す。 埋め込み iframe を生成するため。</summary>
    private static string ExtractYoutubeId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        // 典型的な 4 パターンを直接見る:
        //   https://www.youtube.com/watch?v=XXXX
        //   https://youtu.be/XXXX
        //   https://www.youtube.com/embed/XXXX
        //   https://m.youtube.com/watch?v=XXXX
        // 11 文字の英数字 + アンダースコア + ハイフンが ID。
        var m = System.Text.RegularExpressions.Regex.Match(
            url,
            @"(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/embed/)([A-Za-z0-9_\-]{11})");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>放送日時を「2004年2月1日 8:30〜9:00」または「2004年2月1日 8:30」フォーマットで返す。 <paramref name="durationMinutes"/> が NULL（尺未登録）の場合は終了時刻を表示しない。 尺登録済みの場合は分単位で加算した終了時刻も併記する。 時刻部分は <c>H:mm</c>（先頭ゼロなし、分は 2 桁）。</summary>
    private static string FormatJpDateTimeWithDuration(DateTime dt, byte? durationMinutes)
    {
        string head = $"{dt.Year}年{dt.Month}月{dt.Day}日 {dt.Hour}:{dt.Minute:D2}";
        if (!durationMinutes.HasValue || durationMinutes.Value == 0) return head;

        var endDt = dt.AddMinutes(durationMinutes.Value);
        return $"{head}〜{endDt.Hour}:{endDt.Minute:D2}";
    }

    /// <summary>同シリーズ全話の中から「現在話の前後 ±2 件 + 先頭 + 末尾」のページネーション項目を組み立てる。</summary>
    /// <param name="siblings">同シリーズ全話（順序不問）。</param>
    /// <param name="current">現在話。</param>
    /// <param name="seriesSlug">URL 組み立て用のシリーズ slug。</param>
    /// <param name="window">現在話の前後に表示する話数（既定 2 = 計 5 話を中央に表示）。</param>
    private static IReadOnlyList<PaginationItem> BuildPagination(
        IReadOnlyList<Episode> siblings,
        Episode current,
        string seriesSlug,
        int window = 2)
    {
        // 並びを SeriesEpNo 昇順に固定。
        var ordered = siblings.OrderBy(x => x.SeriesEpNo).ToList();
        if (ordered.Count == 0) return Array.Empty<PaginationItem>();

        int currentIdx = ordered.FindIndex(x => x.EpisodeId == current.EpisodeId);
        if (currentIdx < 0) currentIdx = 0;

        // 表示すべきインデックス（昇順、重複除去）を集める。
        // - 先頭（0）
        // - 末尾（Count-1）
        // - 現在の前後 ±window
        var indices = new SortedSet<int>();
        indices.Add(0);
        indices.Add(ordered.Count - 1);
        for (int i = currentIdx - window; i <= currentIdx + window; i++)
        {
            if (i >= 0 && i < ordered.Count) indices.Add(i);
        }

        // 隣接しない（差が 2 以上）箇所に省略記号を挟みつつアイテム化。
        var result = new List<PaginationItem>();
        int? prev = null;
        foreach (var idx in indices)
        {
            if (prev.HasValue && idx - prev.Value >= 2)
            {
                result.Add(new PaginationItem { IsEllipsis = true });
            }
            var ep = ordered[idx];
            result.Add(new PaginationItem
            {
                SeriesEpNo = ep.SeriesEpNo,
                Url = PathUtil.EpisodeUrl(seriesSlug, ep.SeriesEpNo),
                IsCurrent = ep.EpisodeId == current.EpisodeId
            });
            prev = idx;
        }
        return result;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class EpisodeContentModel
    {
        public SeriesRefView Series { get; set; } = new();
        public EpisodeView Episode { get; set; } = new();
        public FormatTableModel FormatTable { get; set; } = new();
        public string TitleCharInfoHtml { get; set; } = "";
        public IReadOnlyList<PartLengthStatRow> PartLengthStats { get; set; } = Array.Empty<PartLengthStatRow>();
        /// <summary>パート尺統計表のヘッダで使う「『○○プリキュア』」（引用符込み）。 テンプレ側で 2 段ヘッダの上段ラベルとして展開する。</summary>
        public string SeriesTitleShortQuoted { get; set; } = "";
        public IReadOnlyList<ThemeSongRow> ThemeSongs { get; set; } = Array.Empty<ThemeSongRow>();
        public IReadOnlyList<CreditBlockView> CreditBlocks { get; set; } = Array.Empty<CreditBlockView>();
        /// <summary>主要スタッフ情報（脚本／絵コンテ／演出／作画監督／美術）。クレジット階層から抽出した抜粋。</summary>
        public IReadOnlyList<StaffRow> Staff { get; set; } = Array.Empty<StaffRow>();
        /// <summary>使用音声セクション。episode_uses をパート別にグルーピングしたもの。 0 件のエピソードでは空配列で、テンプレ側でセクション自体を非表示にする。</summary>
        public IReadOnlyList<EpisodeUseSection> EpisodeUseSections { get; set; } = Array.Empty<EpisodeUseSection>();
        /// <summary>通算情報の項目列（シリーズ内話数 + 全シリーズ通算 + ニチアサ通算 等）。テンプレ側で放送日時と並ぶファクトタイルとして描画。</summary>
        public IReadOnlyList<TotalsItem> Totals { get; set; } = Array.Empty<TotalsItem>();
        /// <summary>ビルド時刻時点の参照点キャプション（例：「2026年5月3日現在 『キミとアイドルプリキュア♪』第14話時点」）。 毎週変動するセクションの右下注記に出す。</summary>
        public string BuildPointCaption { get; set; } = "";
        /// <summary>サブタイトル分析専用の参照点（サブタイトル登録済みの最終話基準。放送済基準の BuildPointCaption とは別物）。</summary>
        public string SubtitleBuildPointCaption { get; set; } = "";
        /// <summary>クレジット横断のサイト全体カバレッジラベル。</summary>
        public string CoverageLabel { get; set; } = "";
        public string PrevUrl { get; set; } = "";
        public string PrevLabel { get; set; } = "";
        public string NextUrl { get; set; } = "";
        public string NextLabel { get; set; } = "";
        /// <summary>ページネーション端ボタンに表示する前話ラベル（例：「#3 〇〇〇」）。前話が無いときは空文字。</summary>
        public string PrevPagerLabel { get; set; } = "";
        /// <summary>ページネーション端ボタンに表示する次話ラベル（例：「#5 〇〇〇」）。次話が無いときは空文字。</summary>
        public string NextPagerLabel { get; set; } = "";
        /// <summary>同シリーズ全話分の話数ページネーション。テンプレ側で上下 2 か所に展開する。</summary>
        public IReadOnlyList<PaginationItem> Pagination { get; set; } = Array.Empty<PaginationItem>();
    }

    /// <summary>使用音声セクションのパート別グループ 1 件分。</summary>
    private sealed class EpisodeUseSection
    {
        /// <summary>パート種別の表示ラベル（例: "アバン"、"Aパート"）。</summary>
        public string PartLabel { get; set; } = "";
        /// <summary>このパート内の使用音声行（use_order, sub_order 昇順）。</summary>
        public IReadOnlyList<EpisodeUseRow> Uses { get; set; } = Array.Empty<EpisodeUseRow>();
    }

    /// <summary>使用音声 1 行分。SONG なら歌詳細リンク付き、BGM なら M 番号 + メニュータイトル、その他はテキスト。</summary>
    private sealed class EpisodeUseRow
    {
        public byte UseOrder { get; set; }
        public byte SubOrder { get; set; }
        /// <summary>内容種別の表示ラベル（"歌"、"劇伴"、"ドラマ" 等）。</summary>
        public string ContentKindLabel { get; set; } = "";
        /// <summary>主表示テキスト（歌のタイトル、劇伴のメニュータイトル、テキスト系の override 文字列など）。</summary>
        public string Title { get; set; } = "";
        /// <summary>補助情報（歌唱者・サイズ・パート・M 番号・作曲など）。</summary>
        public string SubTitle { get; set; } = "";
        /// <summary>シーン説明（任意）。</summary>
        public string SceneLabel { get; set; } = "";
        /// <summary>使用尺ラベル（"m:ss" 形式）。秒数情報がなければ空文字。</summary>
        public string DurationLabel { get; set; } = "";
        /// <summary>SONG 行のときの楽曲詳細ページへのリンク（それ以外は空）。</summary>
        public string SongLink { get; set; } = "";
        /// <summary>本放送限定フラグ（「本放送のみ」を末尾に表示）。</summary>
        public bool IsBroadcastOnly { get; set; }
    }

    /// <summary>主要スタッフ 1 行（役職名 + 人物名のリスト）。</summary>
    private sealed class StaffRow
    {
        /// <summary>表示用役職名（"脚本" / "絵コンテ" / "演出" / "作画監督" / "美術" / "絵コンテ・演出"）。</summary>
        public string RoleLabel { get; set; } = "";

        /// <summary>役職代表コード。</summary>
        public string RoleCode { get; set; } = "";

        /// <summary>役職統計詳細ページの URL。<c>"/creators/roles/{rep_role_code}/"</c> 形式。 空文字のときはテンプレ側でリンク化せずプレーンテキスト表示。 「絵コンテ・演出」統合行ではこの値ではなく <see cref="SubRoleLinks"/> を使う。</summary>
        public string RoleUrl { get; set; } = "";

        /// <summary>統合ラベル時の構成役職リンク群。</summary>
        public IReadOnlyList<StaffRoleLink> SubRoleLinks { get; set; } = Array.Empty<StaffRoleLink>();

        /// <summary>人物名（複数なら「、」で連結された文字列）。</summary>
        public string NamesLine { get; set; } = "";
    }

    /// <summary>統合ラベル「絵コンテ・演出」を分割描画するためのリンク 1 件分。 テンプレ側では <c>&lt;a href="{Url}"&gt;{Label}&lt;/a&gt;</c> として埋め込む。</summary>
    private sealed class StaffRoleLink
    {
        /// <summary>役職代表コード。</summary>
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>通算情報 1 項目（ラベル + 値 + 任意の説明）。テンプレ側で「小ラベル＋値」の縦 2 段ファクトタイル 1 枚として描画する。</summary>
    private sealed class TotalsItem
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        /// <summary>数え方の定義などの説明文。非空ならタイル全体がツールチップのトリガになる（fact-has-help）。</summary>
        public string Help { get; set; } = "";
    }

    /// <summary>話数ページネーションの 1 項目。</summary>
    private sealed class PaginationItem
    {
        public int SeriesEpNo { get; set; }
        public string Url { get; set; } = "";
        public bool IsCurrent { get; set; }
        /// <summary>省略記号（…）を出すための仮想項目。SeriesEpNo / Url は無効。</summary>
        public bool IsEllipsis { get; set; }
    }

    private sealed class SeriesRefView
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleShort { get; set; } = "";
    }

    private sealed class EpisodeView
    {
        public int SeriesEpNo { get; set; }
        public string TotalEpNo { get; set; } = "";
        public string TotalOaNo { get; set; } = "";
        public string NitiasaOaNo { get; set; } = "";
        public string TitleText { get; set; } = "";
        public string TitleRichHtml { get; set; } = "";
        public string TitleKana { get; set; } = "";
        /// <summary>放送日時を「2004年2月1日 8:30〜9:00」形式で。尺未登録時は終了時刻なし。</summary>
        public string OnAirDateTime { get; set; } = "";
        public string ToeiAnimSummaryUrl { get; set; } = "";
        public string ToeiAnimLineupUrl { get; set; } = "";
        public string YoutubeTrailerUrl { get; set; } = "";
        public string YoutubeId { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class PartLengthStatRow
    {
        public string PartName { get; set; } = "";
        /// <summary>フォーマット帯グラフと共用の色パレット CSS クラス（fmt-p-*）。カードのチップとゲージマーカーに使う。</summary>
        public string PartCss { get; set; } = "";
        /// <summary>この回の OA 実尺（mm:ss）。OA 尺未登録なら空でカード側非表示。</summary>
        public string DurationLabel { get; set; } = "";
        public int SeriesRank { get; set; }
        public int SeriesTotal { get; set; }
        public string SeriesHensachi { get; set; } = "";
        /// <summary>偏差値ゲージ（25〜75 スケール）上のマーカー位置（左から %）。style 属性用。</summary>
        public string SeriesGaugePct { get; set; } = "";
        /// <summary>シリーズ内分布のヒストグラム（ゲージ背景用の棒高さ % × 25 ビン）。</summary>
        public IReadOnlyList<int> SeriesHist { get; set; } = Array.Empty<int>();
        public int GlobalRank { get; set; }
        public int GlobalTotal { get; set; }
        public string GlobalHensachi { get; set; } = "";
        /// <summary>歴代側ゲージのマーカー位置（左から %）。style 属性用。</summary>
        public string GlobalGaugePct { get; set; } = "";
        /// <summary>歴代全体分布のヒストグラム（ゲージ背景用の棒高さ % × 25 ビン）。</summary>
        public IReadOnlyList<int> GlobalHist { get; set; } = Array.Empty<int>();
    }

    /// <summary>偏差値を 25〜75 スケールのゲージ位置（0〜100%）へ変換する。範囲外はゲージ端に丸める。</summary>
    private static string HensachiGaugePercent(double hensachi)
    {
        var pct = Math.Clamp((hensachi - 25.0) / 50.0 * 100.0, 0.0, 100.0);
        return pct.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>主題歌 1 行（テーブルではなく縦リスト 1 行表現）の DTO。</summary>
    private sealed class ThemeSongRow
    {
        /// <summary>区分ラベル（"OP" / "ED" / "挿入歌"）。</summary>
        public string KindLabel { get; set; } = "";
        /// <summary>楽曲タイトル。テンプレ側で <c>「タイトル」</c> のようにカギ括弧で括る。
        /// VariantLabel 非空ならそれを採用、空なら親 song.title（SongsGenerator displayTitle 慣例）。</summary>
        public string Title { get; set; } = "";
        /// <summary>親 song のタイトル（VariantLabel に依存しない常に <c>songs.title</c> の値）。
        /// テンプレ側で「収録は recording だが、クレジット文脈では親 song のタイトルで表示」したいときに使う。</summary>
        public string SongTitle { get; set; } = "";
        /// <summary>楽曲詳細ページへのリンク URL（song_id が引けたときだけセット）。</summary>
        public string SongLink { get; set; } = "";
        /// <summary>録音バージョン表記（例: "TV size"）。空文字なら表示しない。</summary>
        public string VariantLabel { get; set; } = "";
        /// <summary>歌唱者のフリーテキスト（<c>song_recordings.singer_name</c>、フォールバック用）。 <see cref="VocalistsHtml"/> の構造化表示が 優先される（Generator 内でフォールバック処理済み、テンプレは VocalistsHtml だけを見ればよい）。</summary>
        public string SingerName { get; set; } = "";
        /// <summary>備考（任意）。</summary>
        public string Notes { get; set; } = "";
        /// <summary>本放送限定フラグ（「（本放送のみ）」を末尾に併記する）。</summary>
        public bool IsBroadcastOnly { get; set; }

        // ── 構造化クレジット由来の HTML 群 ──
        /// <summary>作詞の表示用 HTML。</summary>
        public string LyricsHtml { get; set; } = "";
        /// <summary>「作詞」役職ラベル HTML（/creators/roles/{rep}/ リンク化済み、未登録時は平文）。</summary>
        public string LyricsRoleLabelHtml { get; set; } = "";
        /// <summary>作曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string CompositionHtml { get; set; } = "";
        /// <summary>「作曲」役職ラベル HTML。</summary>
        public string CompositionRoleLabelHtml { get; set; } = "";
        /// <summary>編曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string ArrangementHtml { get; set; } = "";
        /// <summary>「編曲」役職ラベル HTML。</summary>
        public string ArrangementRoleLabelHtml { get; set; } = "";
        /// <summary>歌唱者の表示用 HTML。</summary>
        public string VocalistsHtml { get; set; } = "";
        /// <summary>「歌」役職ラベル HTML。 他の作詞・作曲・編曲ラベルと同様に <c>/creators/roles/VOCALS/</c> へのリンク付き HTML。 未登録時はフォールバック固定文字列「歌」が入る。</summary>
        public string VocalistsRoleLabelHtml { get; set; } = "";
        /// <summary>コーラス（BACKING_VOCALS 役）連名の表示用 HTML。 該当録音にコーラス行が無ければ空文字列（テンプレ側で行ごと出さない）。</summary>
        public string ChorusHtml { get; set; } = "";
        /// <summary>「コーラス」役職ラベル HTML。 <see cref="ChorusHtml"/> が非空のときだけセットされる（<c>/creators/roles/BACKING_VOCALS/</c> へのリンク化済み HTML、未登録時はフォールバック固定文字列「コーラス」）。</summary>
        public string ChorusRoleLabelHtml { get; set; } = "";
    }

    private sealed class CreditBlockView
    {
        public string CreditKindLabel { get; set; } = "";
        public string Html { get; set; } = "";
    }
}