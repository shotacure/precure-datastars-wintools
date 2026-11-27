namespace PrecureDataStars.Data.Models;

/// <summary>
/// petitcure_video_staff テーブルに対応するエンティティモデル（PK: staff_id）。
/// ぷちきゅあの動画の概要欄の「今回のスタッフ」の 1 名。役職・名前・所属は概要欄に書かれたままの文字で持ち、
/// 名前は人物か企業の名義に、所属は企業の名義につなぐ（まだ名義の無い名前はつながず、文字だけで出す）。
/// 役職は役職マスタにつながず、書かれたままの文字で出す（ぷちきゅあは役職の一覧・集計の対象外）。
/// </summary>
public sealed class PetitcureVideoStaff
{
    /// <summary>行の ID（PK）。</summary>
    public int StaffId { get; set; }

    /// <summary>YouTube の動画 ID（→ petitcure_videos.video_id）。</summary>
    public string VideoId { get; set; } = "";

    /// <summary>概要欄の中の並び順（1 から）。</summary>
    public ushort DisplayOrder { get; set; }

    /// <summary>概要欄の役職名（そのまま。「コンテ・演出」など）。</summary>
    public string RoleLabelText { get; set; } = "";

    /// <summary>概要欄の名前（そのまま）。</summary>
    public string NameText { get; set; } = "";

    /// <summary>人物の名義（→ person_aliases.alias_id）。企業か、まだつないでいなければ null。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>企業/団体の名義（→ company_aliases.alias_id）。人物か、まだつないでいなければ null。</summary>
    public int? CompanyAliasId { get; set; }

    /// <summary>名前に添えた所属（そのまま。括弧は除く）。無ければ null。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>所属の企業/団体の名義（→ company_aliases.alias_id）。無ければ null。</summary>
    public int? AffiliationCompanyAliasId { get; set; }
}
