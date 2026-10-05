using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// クレジットに出す役職 1 つの表記を決める。画面の役職の表記（<c>credit_card_roles.role_label_text</c>）が
/// あればそれを、無ければ役職マスタの名前（<c>roles.name_ja</c>）を使う。
/// SiteBuilder のクレジット描画と Catalog のプレビューで共有する。
/// </summary>
public static class CreditRoleLabel
{
    /// <summary>
    /// 役職 1 つの表記を返す。<paramref name="roleLabelText"/> が空でなければそれ、
    /// 無ければ役職マスタの名前、マスタに無ければ役職コード、役職コードも無ければ空文字。
    /// </summary>
    public static string Resolve(string? roleLabelText, string? roleCode, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (!string.IsNullOrEmpty(roleLabelText)) return roleLabelText!;
        if (string.IsNullOrEmpty(roleCode)) return "";
        return roleMap.TryGetValue(roleCode!, out var r) && !string.IsNullOrEmpty(r.NameJa) ? r.NameJa : roleCode!;
    }
}
