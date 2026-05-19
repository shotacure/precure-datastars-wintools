namespace PrecureDataStars.Data.Models;

/// <summary>
/// movie_bgm_cues テーブルに対応するエンティティモデル（代理 PK: movie_bgm_cue_id）。
/// 映画作品の BGM リスト「1 キュー = 1 行」を表す。<see cref="BgmCue"/>（TV シリーズの
/// セッション制・劇伴専用）とは概念が異なり、映画にはセッションやパートの概念が無い。
/// その映画固有の M ナンバー文字列（<see cref="MNo"/>）・順序（<see cref="Seq"/>）・
/// サブ順序（<see cref="SubSeq"/>）と、そのキュー自体が何か（<see cref="ContentKindCode"/>＝
/// tracks と共通の track_content_kinds 区分）のみを持つ。
/// 映画 BGM 特有の状態として、音源は存在するが本編で使われていない
/// <see cref="IsUnused"/>（未使用）と、そもそも制作されていない
/// <see cref="IsMissing"/>（欠番）を独立フラグで保持する。両者は両立しない
/// （DB 側で CHECK 制約により排他）。<see cref="MNo"/> は欠番では値が無いため
/// NULL を許容し、自然キーにはせず代理キー <see cref="MovieBgmCueId"/> を主キーとする。
/// <see cref="SeriesId"/> は映画系シリーズ（kind_code が MOVIE / MOVIE_SHORT /
/// SPRING / EVENT のいずれか）のみを許容する。この種別制約は DB 側のトリガーで
/// 担保される（MySQL の CHECK は他テーブルを参照できないため）。
/// </summary>
public sealed class MovieBgmCue
{
    /// <summary>代理主キー（movie_bgm_cue_id）。新規挿入前は 0。</summary>
    public int MovieBgmCueId { get; set; }

    /// <summary>所属する映画シリーズ ID（→ series）。映画系 kind のみ許容。</summary>
    public int SeriesId { get; set; }

    /// <summary>映画内での並び順。0 は新規追加直後の暫定値で、Catalog 側の編集画面で 確定値を振る運用（bgm_cues.SeqInSession と同じ思想）。</summary>
    public int Seq { get; set; }

    /// <summary>サブ順序（同一 <see cref="Seq"/> 内での枝番。組曲やバリエーション違いの 並び等に使う）。既定 0。</summary>
    public int SubSeq { get; set; }

    /// <summary>その映画固有の M ナンバー文字列（例: "M01", "M14B"）。 欠番（<see cref="IsMissing"/>）では値が無いため NULL を許容する。</summary>
    public string? MNo { get; set; }

    /// <summary>このキュー自体が何か。</summary>
    public string ContentKindCode { get; set; } = "BGM";

    /// <summary>曲名・メニュー表記等（任意）。</summary>
    public string? Title { get; set; }

    /// <summary>備考（任意）。</summary>
    public string? Notes { get; set; }

    /// <summary>未使用フラグ。音源は存在するが本編では使われていないキュー。 <see cref="IsMissing"/> とは両立しない（DB 側 CHECK 制約で排他）。</summary>
    public bool IsUnused { get; set; }

    /// <summary>欠番フラグ。番号としては存在するがそもそも制作されていない （音源が存在しない）。<see cref="IsUnused"/> とは両立しない。</summary>
    public bool IsMissing { get; set; }

    /// <summary>作成日時（DB 既定値 CURRENT_TIMESTAMP）。</summary>
    public System.DateTime? CreatedAt { get; set; }

    /// <summary>更新日時（DB 側で ON UPDATE CURRENT_TIMESTAMP）。</summary>
    public System.DateTime? UpdatedAt { get; set; }

    /// <summary>作成者。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新者。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
