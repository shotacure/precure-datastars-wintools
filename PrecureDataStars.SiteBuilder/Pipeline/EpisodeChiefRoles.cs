namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 各話のチーフ（脚本・絵コンテ・演出・作画監督・美術）の役職の判定。
/// 記念日カレンダーに誕生日を出す人物の判定（<see cref="BirthdayCalendarEligibility"/>）・歴代記録・
/// 各話のチーフの顔ぶれの索引（<see cref="EpisodeChiefStaffIndex"/>。エピソード詳細の「くわしく」のカードと札）で同じ決まりを使う。
/// 各話のスタッフの札（エピソード一覧・エピソード詳細のスタッフ欄）はこの区分を使わず、作品ごとの表で役職を決める（<see cref="EpisodeStaffCards"/>）。
/// </summary>
public static class EpisodeChiefRoles
{
    /// <summary>区分 1：脚本。</summary>
    public const int Screenplay = 1;
    /// <summary>区分 2：絵コンテ。</summary>
    public const int Storyboard = 2;
    /// <summary>区分 3：演出。</summary>
    public const int EpisodeDirector = 3;
    /// <summary>区分 4：作画監督。</summary>
    public const int AnimationDirector = 4;
    /// <summary>区分 5：美術。</summary>
    public const int ArtDirector = 5;

    /// <summary>区分の表示名（1=脚本、2=絵コンテ、3=演出、4=作画監督、5=美術）。区分外は空文字。</summary>
    public static string Label(int chiefRole) => chiefRole switch
    {
        Screenplay => "脚本",
        Storyboard => "絵コンテ",
        EpisodeDirector => "演出",
        AnimationDirector => "作画監督",
        ArtDirector => "美術",
        _ => ""
    };

    /// <summary>
    /// 役職を各話のチーフの区分に振り分ける。1=脚本、2=絵コンテ、3=演出、4=作画監督、5=美術。該当しなければ null。
    /// 役職コードか、役職マスタの日本語名で判定する（同じ名前で別コードの役職も拾う）。
    /// </summary>
    public static int? Classify(string? roleCode, string? roleNameJa)
    {
        if (roleCode is null) return null;
        string nm = roleNameJa ?? "";
        if (roleCode == "SCREENPLAY"         || nm == "脚本")     return 1;
        if (roleCode == "STORYBOARD"         || nm == "絵コンテ") return 2;
        if (roleCode == "EPISODE_DIRECTOR"   || nm == "演出")     return 3;
        if (roleCode == "ANIMATION_DIRECTOR" || nm == "作画監督") return 4;
        if (roleCode == "ART_DIRECTOR"       || nm == "美術")     return 5;
        return null;
    }
}
