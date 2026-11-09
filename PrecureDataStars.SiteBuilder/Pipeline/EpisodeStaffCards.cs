using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 各話のスタッフの札（作品のページのエピソード一覧・トップページのエピソード欄・エピソード詳細のスタッフ欄）を組むときの、
/// 読むカードの選び方と、作品ごとの札の決まり。
/// <para>
/// 本編の作品は、脚本・絵コンテ・演出・作画監督・美術（区分は <see cref="EpisodeChiefRoles"/>）の 5 つの札。
/// オトナシリーズの作品は、作画監督が毎回大勢並ぶので作画監督の札を出さず、作画監督と同じカードにある作画監督以外の役職
/// （総作画監督・料理パート作画監督・アクション作画監督・サブキャラクターデザインなど。区分 1〜5 の役職は除く）を
/// クレジットの見出しの文字で 1 役職 1 札にして、クレジットの順に作画監督と同じ緑の札で並べる。
/// 美術の札は、OP の美術監督（作品のメインスタッフ）ではなく、各話の ED の美術デザイン（<see cref="ArtDesignCode"/>）を出す。
/// </para>
/// </summary>
public static class EpisodeStaffCards
{
    /// <summary>美術デザインの役職コード。オトナシリーズの作品の美術の札に出す。</summary>
    public const string ArtDesignCode = "ART_DESIGN";

    /// <summary>オトナシリーズの作品の美術の札の文字。</summary>
    public const string OtonaArtLabel = "美術デザイン";

    /// <summary>オトナシリーズの作品で、作画監督と同じカードの役職の札の色分けに使う役職コード（作画監督と同じ緑）。</summary>
    public const string AnimationCardBadgeRoleCode = "ANIMATION_DIRECTOR";

    /// <summary>
    /// 札を組むときに読むカードを、カードの順に返す。本放送と Blu-ray・配信の両方の版のカードを持つクレジットは、
    /// Blu-ray・配信の版（と共通）のカードだけを読み、本放送の版のカードは読まない。
    /// </summary>
    public static IEnumerable<CreditCardSnapshot> CardsForStaff(IReadOnlyList<CreditCardSnapshot> cards)
    {
        bool hasHomeVideo = cards.Any(c => c.Card.MediaVersion == CreditMediaVersions.HomeVideo);
        return cards
            .Where(c => !(hasHomeVideo && c.Card.MediaVersion == CreditMediaVersions.Broadcast))
            .OrderBy(c => c.Card.CardSeq);
    }

    /// <summary>カードの役職を、段 → グループ → グループ内の順（クレジットの順）に並べて返す。</summary>
    public static List<CreditCardRoleSnapshot> OrderedRoles(CreditCardSnapshot card)
        => card.Tiers.OrderBy(t => t.Tier.TierNo)
            .SelectMany(t => t.Groups.OrderBy(g => g.Group.GroupNo))
            .SelectMany(g => g.Roles.OrderBy(r => r.Role.OrderInGroup))
            .ToList();

    /// <summary>役職を区分（1=脚本、2=絵コンテ、3=演出、4=作画監督、5=美術）に振り分ける。役職マスタに無いコードは null。</summary>
    public static int? Classify(CreditCardRole cr, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (cr.RoleCode is null) return null;
        if (!roleMap.TryGetValue(cr.RoleCode, out var role)) return null;
        return EpisodeChiefRoles.Classify(cr.RoleCode, role.NameJa);
    }

    /// <summary>
    /// 作品に合わせて札の区分を決める。本編の作品は <see cref="Classify"/> のまま。
    /// オトナシリーズの作品は、作画監督（区分 4）と美術監督（区分 5）を札にせず、美術デザインを区分 5 に入れる。
    /// </summary>
    public static int? ClassifyForWork(CreditCardRole cr, IReadOnlyDictionary<string, Role> roleMap, bool isOtonaWork)
    {
        if (!isOtonaWork) return Classify(cr, roleMap);
        if (string.Equals(cr.RoleCode, ArtDesignCode, StringComparison.Ordinal)) return EpisodeChiefRoles.ArtDirector;
        int? b = Classify(cr, roleMap);
        return b is EpisodeChiefRoles.AnimationDirector or EpisodeChiefRoles.ArtDirector ? null : b;
    }

    /// <summary>カードに作画監督（区分 4）の役職があるか。</summary>
    public static bool HasAnimationDirector(IEnumerable<CreditCardRoleSnapshot> roles, IReadOnlyDictionary<string, Role> roleMap)
        => roles.Any(r => Classify(r.Role, roleMap) == EpisodeChiefRoles.AnimationDirector);

    /// <summary>
    /// オトナシリーズの作品の、作画監督と同じカードの役職の札の文字。クレジットの見出しの文字（role_label_text）があればそれ、
    /// 無ければ役職マスタの名前。札にしない役職（区分 1〜5、役職マスタに無いもの）は null。
    /// </summary>
    public static string? AnimationCardBadgeLabel(CreditCardRole cr, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (cr.RoleCode is null) return null;
        if (!roleMap.TryGetValue(cr.RoleCode, out var role)) return null;
        if (EpisodeChiefRoles.Classify(cr.RoleCode, role.NameJa) is not null) return null;
        return string.IsNullOrWhiteSpace(cr.RoleLabelText) ? role.NameJa : cr.RoleLabelText!;
    }
}
