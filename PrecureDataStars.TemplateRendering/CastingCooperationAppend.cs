using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 声の出演（VOICE_CAST）の表の末尾に「協力」行として足す、同じカードの CASTING_COOPERATION 役職の中身。
/// SiteBuilder のクレジット描画と Catalog のプレビューで共有する。
/// </summary>
/// <param name="Entries">CASTING_COOPERATION 役職配下のエントリ（表示順）。</param>
/// <param name="LabelText">「協力」行の役職の表記（CASTING_COOPERATION 役職の <c>role_label_text</c>）。
/// 無ければ null で、役職マスタの名前で出す。</param>
public sealed record CastingCooperationAppend(IReadOnlyList<CreditBlockEntry> Entries, string? LabelText);
