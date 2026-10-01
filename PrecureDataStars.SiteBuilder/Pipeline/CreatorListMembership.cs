using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クリエイターの各一覧（スタッフ・声の出演・歌唱・音楽制作）に、どの人物・企業/団体が載ったかの記録。
/// <see cref="Generators.CreatorsGenerator"/> が一覧を作るときに積み、人物・企業詳細のパンくずが
/// 「本人が載っている一覧」を経由するために引く（載っていない一覧を経由すると、戻った先に本人が居ない）。
/// CreatorsGenerator は人物・企業詳細より前に走らせ、以後は読み取り専用（並列レンダリングから引いても安全）。
/// </summary>
public sealed class CreatorListMembership
{
    /// <summary>未構築時（ピンポイントビルドで一覧を作らない場合など）用の空の記録。パンくずは中間の一覧を経由しない。</summary>
    public static CreatorListMembership Empty { get; } = new();

    public HashSet<int> StaffPersons { get; } = new();
    public HashSet<int> StaffCompanies { get; } = new();
    public HashSet<int> VoiceCastPersons { get; } = new();
    public HashSet<int> SingerPersons { get; } = new();
    public HashSet<int> MusicProductionPersons { get; } = new();
    public HashSet<int> MusicProductionCompanies { get; } = new();

    /// <summary>
    /// 人物詳細のパンくずで経由する一覧（ラベルと URL）。本人が載っている一覧を
    /// スタッフ → 声の出演 → 歌唱 → 音楽制作 の順に探し、どれにも載っていなければ null（中間の段を置かない）。
    /// </summary>
    public (string Label, string Url)? ListForPerson(int personId)
    {
        if (StaffPersons.Contains(personId)) return ("歴代プリキュアスタッフ", PathUtil.CreatorsStaffUrl());
        if (VoiceCastPersons.Contains(personId)) return ("歴代プリキュア声優", PathUtil.CreatorsVoiceCastUrl());
        if (SingerPersons.Contains(personId)) return ("歴代プリキュア歌唱", PathUtil.CreatorsSingersUrl());
        if (MusicProductionPersons.Contains(personId)) return ("歴代プリキュア音楽制作", PathUtil.CreatorsMusicProductionUrl());
        return null;
    }

    /// <summary>
    /// 人物・企業/団体詳細のパンくず「ホーム › 歴代クリエイター › {本人が載っている一覧} › 名前」。
    /// <paramref name="list"/> が null（どの一覧にも載っていない）なら中間の段を置かない。
    /// </summary>
    public static BreadcrumbItem[] DetailBreadcrumbs((string Label, string Url)? list, string displayName)
    {
        var items = new List<BreadcrumbItem>
        {
            new() { Label = "ホーム", Url = "/" },
            new() { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() }
        };
        if (list is { } l) items.Add(new BreadcrumbItem { Label = l.Label, Url = l.Url });
        items.Add(new BreadcrumbItem { Label = displayName, Url = "" });
        return items.ToArray();
    }

    /// <summary>企業/団体詳細のパンくずで経由する一覧。スタッフ → 音楽制作 の順に探し、どれにも載っていなければ null。</summary>
    public (string Label, string Url)? ListForCompany(int companyId)
    {
        if (StaffCompanies.Contains(companyId)) return ("歴代プリキュアスタッフ", PathUtil.CreatorsStaffUrl());
        if (MusicProductionCompanies.Contains(companyId)) return ("歴代プリキュア音楽制作", PathUtil.CreatorsMusicProductionUrl());
        return null;
    }
}
