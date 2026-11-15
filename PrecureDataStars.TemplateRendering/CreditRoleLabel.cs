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

    /// <summary>
    /// 役職名だけが画面にはクレジットされていない行（<c>credit_card_roles.role_label_not_on_screen</c>）の役職名 HTML を、
    /// 全角の括弧で包んだ <c>span.role-label-not-on-screen</c> にする（「（料理作監）」の形。マウスを載せると title で理由が出る）。
    /// 色は CSS で、行ごと画面に無い行（not-on-screen）と同じ薄い色を役職名にだけ当て、名前・中身はふつうに出す。
    /// <paramref name="roleNameHtml"/> が空なら空のまま返す（役職名を出さない行に括弧だけを残さない）。
    /// SiteBuilder のクレジット描画・Catalog のプレビュー・テンプレの <c>{ROLE_NAME}</c> で共有する。
    /// </summary>
    public static string WrapNotOnScreenHtml(string roleNameHtml)
        => string.IsNullOrEmpty(roleNameHtml)
            ? roleNameHtml
            : $"<span class=\"role-label-not-on-screen\" title=\"画面にはクレジットされていない役職名\">（{roleNameHtml}）</span>";
}
