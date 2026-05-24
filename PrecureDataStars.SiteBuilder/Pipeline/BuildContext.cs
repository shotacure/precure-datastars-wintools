using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 全 Generator から読まれる共有コンテキスト。
/// パイプライン開始時に <see cref="Data.SiteDataLoader"/> がデータを一括ロードし、
/// 結果をこのオブジェクトに詰めて Generator に渡す。
/// 1 回のビルドで同じマスタを何度もクエリしないための簡易キャッシュ的な役割。
/// タスク 1〜3 の段階ではシリーズ・エピソードに必要な情報のみを保持。
/// プリキュア／キャラ／人物などのページが追加されるタスク 4 以降で順次拡張する。
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

    /// <summary>シリーズ slug → シリーズ ID の逆引き索引。Generator がリンク先を組み立てるときに使う。</summary>
    public required IReadOnlyDictionary<string, int> SeriesIdBySlug { get; init; }

    /// <summary>シリーズ ID から所属シリーズを取得するための索引。 （Series リストを毎回検索しないようにするための補助。）</summary>
    public required IReadOnlyDictionary<int, Series> SeriesById { get; init; }

    /// <summary>
    /// 全 <c>tracks</c> 行を catalog_no 単位で事前グルーピングした辞書。
    /// 旧 SongsGenerator / ProductsGenerator はディスクごとに <c>TracksRepository.GetByCatalogNoAsync</c>
    /// を逐次呼んでいたが、いずれのジェネレータも結局メモリ上の全件辞書を最後に組み立てる構造だったため、
    /// SiteDataLoader で 1 度だけ <see cref="Data.Repositories.TracksRepository.GetAllAsync"/> を実行して
    /// 共有する。並び順は (catalog_no, track_no, sub_order) 昇順を維持する。
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<Track>> TracksByCatalogNo { get; init; }

    /// <summary>
    /// 全 <c>song_credits</c> 行を song_id 単位で事前グルーピングした辞書。
    /// SongsGenerator と ProductsGenerator の双方で「曲ごとに作詞・作曲・編曲の名義行を引く」処理が
    /// 走るため、両方が同じソースを参照できるよう SiteDataLoader でロードして共有する。
    /// 並びは LYRICS → COMPOSITION → ARRANGEMENT → その他 role_code 昇順、同役内は credit_seq 昇順。
    /// </summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<SongCredit>> SongCreditsBySong { get; init; }

    /// <summary>
    /// 全 <c>song_recording_singers</c> 行を song_recording_id 単位で事前グルーピングした辞書。
    /// SongsGenerator と ProductsGenerator の双方で「録音ごとに歌唱者連名を引く」処理が走るため、
    /// 両方が同じソースを参照できるよう SiteDataLoader でロードして共有する。
    /// 並びは VOCALS → CHORUS → その他 role_code 昇順、同役内は singer_seq 昇順。
    /// </summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<SongRecordingSinger>> SingersByRecording { get; init; }

    /// <summary>
    /// 論理削除を除く全 <c>bgm_cues</c> 行を series_id 単位で事前グルーピングした辞書。
    /// MusicGenerator と ProductsGenerator の双方で「シリーズごとの全 cue」が必要になるため、
    /// SiteDataLoader でロードして共有する。並び順は GetBySeriesAsync と同等（session_no,
    /// seq_in_session, m_no_detail 昇順）。
    /// </summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> BgmCuesBySeries { get; init; }

    /// <summary>
    /// 全 <c>bgm_cue_credits</c> 行を (series_id, m_no_detail) 単位で事前グルーピングした辞書。
    /// ProductsGenerator が「BGM トラックごとに作曲・編曲の名義行を引く」処理を撲滅するために使う。
    /// 並びは COMPOSITION → ARRANGEMENT → その他 role_code 昇順、同役内は credit_seq 昇順。
    /// </summary>
    public required IReadOnlyDictionary<(int SeriesId, string MNoDetail), IReadOnlyList<BgmCueCredit>>
        BgmCueCreditsByCue { get; init; }

    /// <summary>
    /// 全エピソードのサブタイトル文字統計を事前計算したインデックス。
    /// 文字キー → 出現エピソード一覧（TotalEpNo 昇順整列）と、
    /// エピソード ID → 使用文字キー集合の双方向辞書を保持する。
    /// <see cref="Rendering.TitleCharInfoRenderer"/> がページごとに「初出 / 唯一 / N年Mか月ぶり」を判定する際、
    /// per-page で JSON_CONTAINS_PATH の全表走査を文字数分繰り返さないよう、
    /// ビルド開始時に <see cref="TitleCharIndex.Build"/> で 1 度だけ構築して全 Generator で共有する
    /// （展開元の <see cref="Episode.TitleCharStats"/> JSON は <see cref="Data.SiteDataLoader"/> が
    /// 全話分ロード済みのため、本インデックス構築に追加の DB クエリは発生しない）。
    /// </summary>
    public required TitleCharIndex TitleCharIndex { get; init; }

    /// <summary>
    /// 全エピソード分のパート尺偏差値・順位を事前計算した辞書。
    /// AVANT / PART_A / PART_B の <see cref="EpisodePartsRepository.PartLengthStat"/> を
    /// episode_id 単位でリスト化して保持する。
    /// SiteDataLoader が <see cref="EpisodePartsRepository.GetAllPartLengthStatsAsync"/> を
    /// ビルド開始時に 1 度だけ呼び出して構築する。
    /// 対象パートを持たないエピソード（kind_code='MOVIE' 等で AVANT/PART_A/PART_B が無いもの）
    /// は辞書に含まれない。
    /// EpisodeGenerator はページごとに DB を叩かず、本辞書から episode_id で引いて
    /// パート尺統計表を組み立てる（1 ページごとの per-episode 全件 CTE 集計を回避するため）。
    /// </summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<EpisodePartsRepository.PartLengthStat>>
        PartLengthStatsByEpisode { get; init; }

    /// <summary>
    /// ビルド時刻時点で「直近に放送された TV シリーズエピソード」。
    /// <see cref="Series.KindCode"/> = "TV" のシリーズ配下のエピソードのうち、ビルド実行時刻
    /// （<see cref="DateTime.Now"/>）以前で <see cref="Episode.OnAirAt"/> が最大のもの。
    /// 該当が無いとき（クリーン DB 等）は <c>null</c>。
    /// 用途：エピソード詳細ページで毎週変動するセクション（サブタイトル文字情報、パート尺統計情報）に
    /// 「yyyy年m月d日現在、『○○プリキュア』第n話時点」というキャプションを付ける際の参照点。
    /// 同じ意味の「最新エピソード」を Home ジェネレータも別途計算しているが、参照点を本フィールドに集約することで
    /// サイト全体で「いま」の基準が揃うようにする。
    /// </summary>
    public required (Series Series, Episode Episode)? LatestAiredTvEpisode { get; init; }

    /// <summary>
    /// クレジット横断のカバレッジラベル文字列。
    /// 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記で、
    /// クレジットが 1 件でも登録されている TV シリーズエピソードの中から最新（OnAirAt 最大）の 1 話を基準にする。
    /// 該当が無いときは空文字（テンプレ側で「ラベル無し」として空表示）。
    /// プリキュア・キャラクター・人物・企業・団体・シリーズ・エピソードの各詳細・索引ページに
    /// 「サイト全体としてどこまで反映されているか」を明記するために、全ページで横断的にこのラベルを使う。
    /// 算出タイミングは <see cref="CreditInvolvementIndex"/> 構築直後（Pipeline 側で 1 回算出して詰める）。
    /// 各エンティティ単独の鮮度ではなくサイト全体の鮮度を示す方針なので、ページごとに値は変わらない。
    /// </summary>
    public string CreditCoverageLabel { get; set; } = string.Empty;
}
