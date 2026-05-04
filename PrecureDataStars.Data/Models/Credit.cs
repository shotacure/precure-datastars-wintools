namespace PrecureDataStars.Data.Models;

/// <summary>
/// credits テーブルに対応するエンティティモデル（PK: credit_id）。
/// <para>
/// クレジット 1 件 = 1 行。シリーズ単位 or エピソード単位で、本放送共通／本放送限定の
/// 2 段階で OP/ED 各 1 件まで保持できる（is_broadcast_only=0 が Blu-ray・配信を含む
/// 全媒体共通行、is_broadcast_only=1 が本放送限定の例外行）。
/// </para>
/// <para>
/// <see cref="ScopeKind"/> = "SERIES"  なら <see cref="SeriesId"/> が必須・<see cref="EpisodeId"/> は NULL。<br/>
/// <see cref="ScopeKind"/> = "EPISODE" なら <see cref="EpisodeId"/> が必須・<see cref="SeriesId"/> は NULL。
/// この排他は DB 側のトリガー <c>trg_credits_b{i,u}_scope_consistency</c> でも担保される。
/// </para>
/// <para>
/// <see cref="PartType"/> が NULL のクレジットは「規定位置（part_types.default_credit_kind が
/// credit_kind と一致するパート）で流れる」と解釈される。OP/ED が他パートで流れる
/// 例外的ケース（CM 跨ぎ後の B パートで OP が流れる回 等）でのみ値を入れる。
/// </para>
/// <para>
/// v1.2.0 工程 B' で <see cref="IsBroadcastOnly"/> を導入。
/// クレジットも本放送と Blu-ray・配信で同じ内容なのが大半なので、既定 0 行の
/// 1 件で全媒体共通のクレジットを表現し、本放送だけ異なる場合のみ
/// is_broadcast_only=1 の追加行を別途立てる運用とする。UNIQUE は
/// (series_id, is_broadcast_only, credit_kind) と (episode_id, is_broadcast_only, credit_kind) の 2 本。
/// </para>
/// </summary>
public sealed class Credit
{
    /// <summary>クレジットの主キー（AUTO_INCREMENT）。</summary>
    public int CreditId { get; set; }

    /// <summary>スコープ区分（"SERIES"/"EPISODE"）。</summary>
    public string ScopeKind { get; set; } = "EPISODE";

    /// <summary>シリーズ ID（→ series.series_id、scope=SERIES のとき必須）。</summary>
    public int? SeriesId { get; set; }

    /// <summary>エピソード ID（→ episodes.episode_id、scope=EPISODE のとき必須）。</summary>
    public int? EpisodeId { get; set; }

    /// <summary>
    /// 本放送限定フラグ（v1.2.0 工程 B' 追加）。
    /// false (0) = 本放送・Blu-ray・配信ともに共通（既定）。
    /// true (1) = 本放送限定の例外行。
    /// </summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>クレジット種別（"OP"/"ED"）。</summary>
    public string CreditKind { get; set; } = "OP";

    /// <summary>規定位置と異なるパートで流れる場合の上書きパート（→ part_types.part_type、任意）。</summary>
    public string? PartType { get; set; }

    /// <summary>提示形式（"CARDS" = 複数カード、"ROLL" = 巻物）。既定 "CARDS"。</summary>
    public string Presentation { get; set; } = "CARDS";

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
