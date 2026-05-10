
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

    /// <summary>
    /// ビルド時刻時点で「直近に放送された TV シリーズエピソード」。
    /// <para>
    /// <see cref="Series.KindCode"/> = "TV" のシリーズ配下のエピソードのうち、ビルド実行時刻
    /// （<see cref="DateTime.Now"/>）以前で <see cref="Episode.OnAirAt"/> が最大のもの。
    /// 該当が無いとき（クリーン DB 等）は <c>null</c>。
    /// </para>
    /// <para>
    /// 用途：エピソード詳細ページで毎週変動するセクション（サブタイトル文字情報、パート尺統計情報）に
    /// 「yyyy年m月d日現在、『○○プリキュア』第n話時点」というキャプションを付ける際の参照点。
    /// 同じ意味の「最新エピソード」を Home ジェネレータも別途計算しているが、参照点を本フィールドに集約することで
    /// サイト全体で「いま」の基準が揃うようにする。
    /// </para>
    /// </summary>
    public required (Series Series, Episode Episode)? LatestAiredTvEpisode { get; init; }

    /// <summary>
    /// クレジット横断のカバレッジラベル文字列
    /// （v1.3.0 ブラッシュアップ続編で追加）。
    /// <para>
    /// 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記で、
    /// クレジットが 1 件でも登録されている TV シリーズエピソードの中から最新（OnAirAt 最大）の 1 話を基準にする。
    /// 該当が無いときは空文字（テンプレ側で「ラベル無し」として空表示）。
    /// </para>
    /// <para>
    /// プリキュア・キャラクター・人物・企業・団体・シリーズ・エピソードの各詳細・索引ページに
    /// 「サイト全体としてどこまで反映されているか」を明記するために、全ページで横断的にこのラベルを使う。
    /// 算出タイミングは <see cref="CreditInvolvementIndex"/> 構築直後（Pipeline 側で 1 回算出して詰める）。
    /// 各エンティティ単独の鮮度ではなくサイト全体の鮮度を示す方針なので、ページごとに値は変わらない。
    /// </para>
    /// </summary>
    public string CreditCoverageLabel { get; set; } = string.Empty;
}
