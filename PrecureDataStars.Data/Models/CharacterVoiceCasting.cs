namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_voice_castings テーブルに対応するエンティティモデル（PK: casting_id）。
/// <para>
/// キャラクター ⇄ 声優のキャスティング情報。同一 <c>character_id</c> × <c>person_id</c> に対し
/// 期間や種別を変えて複数行を持てる（声優交代の節目を <see cref="ValidFrom"/> で記録）。
/// </para>
/// <para>
/// <see cref="CastingKind"/> の意味:
/// "REGULAR"    ... 標準のレギュラー担当 / 
/// "SUBSTITUTE" ... 代役（病気・スケジュール都合等の期間限定） / 
/// "TEMPORARY"  ... 引き継ぎ・交代後の暫定担当 / 
/// "MOB"        ... 1 話限りのモブ等への当て込み。
/// </para>
/// </summary>
public sealed class CharacterVoiceCasting
{
    /// <summary>キャスティングの主キー（AUTO_INCREMENT）。</summary>
    public int CastingId { get; set; }

    /// <summary>対象キャラクター ID（→ characters.character_id）。</summary>
    public int CharacterId { get; set; }

    /// <summary>担当人物 ID（→ persons.person_id）。</summary>
    public int PersonId { get; set; }

    /// <summary>キャスティング区分（"REGULAR"/"SUBSTITUTE"/"TEMPORARY"/"MOB"）。既定 "REGULAR"。</summary>
    public string CastingKind { get; set; } = "REGULAR";

    /// <summary>担当開始日（任意）。</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>担当終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

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
