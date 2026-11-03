namespace PrecureDataStars.Data.Models;

/// <summary>
/// credits テーブルに対応するエンティティモデル（PK: credit_id）。
/// クレジット 1 件 = 1 行。シリーズ単位 or エピソード単位で、OP/ED 各 1 件を版（<see cref="MediaVersion"/>）ごとに持つ。
/// 版は NULL（共通）が通常で、本放送と Blu-ray・配信でクレジットのフォーマットが丸ごと違う作品（オトナシリーズ）だけ
/// 同じ回・同じ種類のクレジットを "BROADCAST"（本放送）と "HOME_VIDEO"（Blu-ray・配信）の 2 本で並べて持つ。
/// 一部の差し替え（ロゴバージョン違い等）は個々のエントリ単位（<c>credit_block_entries.is_broadcast_only</c>）で扱う。
/// <see cref="ScopeKind"/> = "SERIES"  なら <see cref="SeriesId"/> が必須・<see cref="EpisodeId"/> は NULL。<br/>
/// <see cref="ScopeKind"/> = "EPISODE" なら <see cref="EpisodeId"/> が必須・<see cref="SeriesId"/> は NULL。
/// この排他は DB 側のトリガー <c>trg_credits_b{i,u}_scope_consistency</c> でも担保される。
/// <see cref="PartType"/> が NULL のクレジットは「規定位置（part_types.default_credit_kind が
/// credit_kind と一致するパート）で流れる」と解釈される。OP/ED が他パートで流れる
/// 例外的ケース（CM 跨ぎ後の B パートで OP が流れる回 等）でのみ値を入れる。
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

    /// <summary>クレジット種別（"OP"/"ED"）。</summary>
    public string CreditKind { get; set; } = "OP";

    /// <summary>
    /// 版（"BROADCAST" = 本放送、"HOME_VIDEO" = Blu-ray・配信、NULL = 共通）。値は <see cref="CreditMediaVersions"/>。
    /// 同じスコープ・同じ <see cref="CreditKind"/> の中で一意（NULL どうしも 1 本まで。DB 側は生成列 media_version_key の UNIQUE で担保）。
    /// SiteBuilder の集計は共通と Blu-ray・配信版だけで数え、本放送版は表示だけに使う。
    /// </summary>
    public string? MediaVersion { get; set; }

    /// <summary>同一スコープ（同一エピソード／同一シリーズ）内でのクレジット表示順（1 始まり）。 クレジット階層下位（card_seq 等）と同じく明示順序で、運用者が編集画面で 並べ替える。従来の credit_kind 暗黙順（OP→ED）依存を解消する。</summary>
    public ushort CreditSeq { get; set; } = 1;

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

/// <summary>クレジットの版（<see cref="Credit.MediaVersion"/>）の値と表示名。</summary>
public static class CreditMediaVersions
{
    /// <summary>本放送。集計には数えず、表示だけに使う。</summary>
    public const string Broadcast = "BROADCAST";

    /// <summary>Blu-ray・配信。</summary>
    public const string HomeVideo = "HOME_VIDEO";

    /// <summary>版の表示名。NULL（共通）は空文字。</summary>
    public static string LabelOf(string? mediaVersion) => mediaVersion switch
    {
        Broadcast => "本放送",
        HomeVideo => "Blu-ray・配信",
        _ => ""
    };

    /// <summary>同じ種別のクレジットを並べる順（共通 → 本放送 → Blu-ray・配信）。</summary>
    public static int SortOrder(string? mediaVersion) => mediaVersion switch
    {
        null => 0,
        Broadcast => 1,
        HomeVideo => 2,
        _ => 9
    };

    /// <summary>集計（関わった話数・役職の何回目など）に数える版か。本放送版だけ数えない。</summary>
    public static bool CountsForStats(string? mediaVersion)
        => !string.Equals(mediaVersion, Broadcast, StringComparison.Ordinal);
}
