namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_episode_staff_roles テーブルに対応するエンティティモデル（PK: series_id, role_code）。
/// 作品ごとの、各話のスタッフの札に出す役職と、その並び順・札の色を持つ。
/// サイトの作品のページのエピソード一覧・トップページのエピソード欄・/episodes/・エピソード詳細のスタッフ欄（と各話の OGP カード）は、
/// この行のある役職だけを <see cref="DisplayOrder"/> の順に <see cref="BadgeColor"/> の色で出す。
/// 行の無い作品には札を出さない。
/// </summary>
public sealed class SeriesEpisodeStaffRole
{
    /// <summary>作品 ID（PK 構成、→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>役職コード（PK 構成、→ roles.role_code）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>作品の中の並び順（小さいほど先）。</summary>
    public byte DisplayOrder { get; set; }

    /// <summary>札の色（<c>#rrggbb</c> の小文字）。札の地・枠・字の色はこの色から作る。</summary>
    public string BadgeColor { get; set; } = "";

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
