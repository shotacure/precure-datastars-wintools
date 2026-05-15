namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_precures テーブルに対応するエンティティモデル。
/// <para>
/// シリーズとプリキュアの多対多関連を表現する純粋な関連テーブルの 1 行 = 1 関連エントリ。
/// 1 プリキュアは複数シリーズに登場し得る（続編シリーズで引き続きレギュラー、
/// クロスオーバー作品で登場、変身前の姿で出てきて変身しない出演 等）ため、
/// <see cref="Precure"/> 側に series_id を持たせず、本テーブルで多対多を扱う設計。
/// </para>
/// <para>
/// 複合 PK は <c>(series_id, precure_id)</c>。同シリーズ内に同一プリキュアの重複登録は不可。
/// </para>
/// </summary>
public sealed class SeriesPrecure
{
    /// <summary>所属シリーズの ID（FK: <c>series.series_id</c>）。</summary>
    public int SeriesId { get; set; }

    /// <summary>プリキュアの ID（FK: <c>precures.precure_id</c>）。</summary>
    public int PrecureId { get; set; }

    /// <summary>
    /// 同シリーズ内のプリキュア並び順（0 始まり、昇順表示）。
    /// 「主役 → サブ」の表示順を明示的に制御するために使う。
    /// 同値時は <see cref="PrecureId"/> 昇順でタイブレーク。
    /// </summary>
    public byte DisplayOrder { get; set; }

    /// <summary>レコード作成時刻（DB 側の DEFAULT CURRENT_TIMESTAMP）。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>レコード更新時刻（DB 側の ON UPDATE CURRENT_TIMESTAMP）。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成者識別子（任意、ログイン運用がある場合に使用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新者識別子（任意）。</summary>
    public string? UpdatedBy { get; set; }
}