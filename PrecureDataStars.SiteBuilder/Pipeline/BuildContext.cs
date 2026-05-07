using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Configuration;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 全 Generator から読まれる共有コンテキスト。
/// <para>
/// パイプライン開始時に <see cref="Data.SiteDataLoader"/> がデータを一括ロードし、
/// 結果をこのオブジェクトに詰めて Generator に渡す。
/// 1 回のビルドで同じマスタを何度もクエリしないための簡易キャッシュ的な役割。
/// </para>
/// <para>
/// タスク 1〜3 の段階ではシリーズ・エピソードに必要な情報のみを保持。
/// プリキュア／キャラ／人物などのページが追加されるタスク 4 以降で順次拡張する。
/// </para>
/// </summary>
public sealed class BuildContext
{
    /// <summary>実行時設定。</summary>
    public required BuildConfig Config { get; init; }

    /// <summary>ロガー。</summary>
    public required BuildLogger Logger { get; init; }

    /// <summary>サマリ集計（ページ数等）。</summary>
    public required BuildSummary Summary { get; init; }

    /// <summary>論理削除を除く全シリーズ（start_date, series_id 昇順）。</summary>
    public required IReadOnlyList<Series> Series { get; init; }

    /// <summary>シリーズごとのエピソード一覧（series_id → series_ep_no 昇順のリスト）。</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<Episode>> EpisodesBySeries { get; init; }

    /// <summary>パート種別マスタ（part_type → モデル）。</summary>
    public required IReadOnlyDictionary<string, PartType> PartTypeByCode { get; init; }

    /// <summary>シリーズ種別マスタ（kind_code → モデル）。タイトル表示用。</summary>
    public required IReadOnlyDictionary<string, SeriesKind> SeriesKindByCode { get; init; }

    /// <summary>
    /// シリーズ slug → シリーズ ID の逆引き索引。Generator がリンク先を組み立てるときに使う。
    /// </summary>
    public required IReadOnlyDictionary<string, int> SeriesIdBySlug { get; init; }

    /// <summary>
    /// シリーズ ID から所属シリーズを取得するための索引。
    /// （Series リストを毎回検索しないようにするための補助。）
    /// </summary>
    public required IReadOnlyDictionary<int, Series> SeriesById { get; init; }
}
