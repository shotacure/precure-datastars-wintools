namespace PrecureDataStars.Data.Models;

/// <summary>
/// 配信音源（YouTube アートトラック）の再生可否（<c>tracks.youtube_playability</c>）の値と、
/// サイトに再生ボタンを出してよいかの決まり。
/// <para>
/// 値の出どころは 2 つに分かれる。
/// <list type="bullet">
///   <item><see cref="Unplayable"/>：YouTube Data API の地域制限（<c>contentDetails.regionRestriction</c>）で
///         日本では再生できないと分かった音源。取り込みと定期の再確認で API が決める。</item>
///   <item><see cref="PremiumOnly"/> / <see cref="Ok"/>：YouTube Music Premium 会員限定かどうか。
///         Data API のどの項目にも現れないため、取り込みの確認ダイアログで人が盤ごとに決める。</item>
/// </list>
/// NULL は未確認（会員限定かどうかを人がまだ決めていない）で、サイトでは <see cref="Ok"/> と同じに扱う。
/// 会員限定の音源を誤って未確認のまま出しても、再生できなかったことはプレイヤーがその場で検知する。
/// </para>
/// </summary>
public static class ArtTrackPlayability
{
    /// <summary>誰でも再生できる（人が会員限定ではないと確かめた）。</summary>
    public const string Ok = "OK";

    /// <summary>YouTube Music Premium 会員のみ再生できる（人が確かめた）。</summary>
    public const string PremiumOnly = "PREMIUM_ONLY";

    /// <summary>地域制限で日本では再生できない（API で確かめた）。</summary>
    public const string Unplayable = "UNPLAYABLE";

    /// <summary>
    /// API から取った埋め込み可否・地域制限を確かめ直す間隔（日数）。
    /// YouTube API Services Developer Policies III.E.4 は、保存した API データを 30 日以内に
    /// 取り直すか消すかを求めている。確認からこの日数を過ぎた音源はサイトに出さない。
    /// </summary>
    public const int RecheckDays = 30;

    /// <summary>
    /// サイトに再生ボタンを出してよい音源か。
    /// 埋め込み可と確認済みで、日本で再生でき、確認から <see cref="RecheckDays"/> 日以内のものに限る。
    /// </summary>
    /// <param name="embeddable"><c>tracks.youtube_embeddable</c>。</param>
    /// <param name="playability"><c>tracks.youtube_playability</c>。</param>
    /// <param name="checkedAt"><c>tracks.youtube_checked_at</c>。</param>
    /// <param name="now">判定の基準時刻。</param>
    public static bool IsPublishable(bool? embeddable, string? playability, DateTime? checkedAt, DateTime now)
        => embeddable == true
        && playability != Unplayable
        && IsFresh(checkedAt, now);

    /// <summary>確認から <see cref="RecheckDays"/> 日以内か。未確認（NULL）は古いものとみなす。</summary>
    public static bool IsFresh(DateTime? checkedAt, DateTime now)
        => checkedAt is DateTime at && now - at <= TimeSpan.FromDays(RecheckDays);
}
