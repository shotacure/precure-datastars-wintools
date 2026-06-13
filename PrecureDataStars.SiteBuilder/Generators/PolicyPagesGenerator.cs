using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>法律・運営情報系の補助ページを生成するジェネレータ。
/// プライバシーポリシーの文面は常に本番運用状態（GA4 + AdSense 有効）を前提とした固定記述で、
/// ローカルテスト時の ID 未設定（タグ出力オフ）には文面を追従させない。</summary>
public sealed class PolicyPagesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public PolicyPagesGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating policy pages");

        GeneratePrivacy();
        GenerateDisclaimer();
        GenerateContact();

        _ctx.Logger.Success("/privacy/, /disclaimer/, /contact/");
    }

    /// <summary>/privacy/ — プライバシーポリシー。</summary>
    private void GeneratePrivacy()
    {
        var content = new PrivacyContentModel
        {
            SiteName = _ctx.Config.SiteName,
        };
        var layout = new LayoutModel
        {
            PageTitle = "プライバシーポリシー",
            MetaDescription = $"{_ctx.Config.SiteName} のプライバシーポリシーです。Cookie・アクセス解析・広告配信の取り扱いを説明しています。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プライバシーポリシー", Url = "" }
            }
        };
        _page.RenderAndWrite("/privacy/", "policy", "privacy.sbn", content, layout);
    }

    /// <summary><c>/disclaimer/</c> — 免責事項。</summary>
    private void GenerateDisclaimer()
    {
        var content = new DisclaimerContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "免責事項",
            MetaDescription = $"{_ctx.Config.SiteName} の免責事項です。情報の正確性・外部リンク・著作権の取り扱いを説明しています。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "免責事項", Url = "" }
            }
        };
        _page.RenderAndWrite("/disclaimer/", "policy", "disclaimer.sbn", content, layout);
    }

    /// <summary><c>/contact/</c> — お問い合わせページ。</summary>
    private void GenerateContact()
    {
        var content = new ContactContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "お問い合わせ",
            MetaDescription = $"{_ctx.Config.SiteName} へのお問い合わせページです。誤情報のご指摘、ご意見、利用に関するご質問はこちらからお寄せください。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "お問い合わせ", Url = "" }
            }
        };
        _page.RenderAndWrite("/contact/", "policy", "contact.sbn", content, layout);
    }

    /// <summary>プライバシーポリシーページに渡すコンテンツモデル。</summary>
    private sealed class PrivacyContentModel
    {
        /// <summary>サイト名（テンプレ本文中に複数回出現するため毎ページ渡す）。</summary>
        public string SiteName { get; set; } = "";
    }

    /// <summary>免責事項ページに渡すコンテンツモデル。</summary>
    private sealed class DisclaimerContentModel
    {
        public string SiteName { get; set; } = "";
    }

    /// <summary>お問い合わせページに渡すコンテンツモデル。</summary>
    private sealed class ContactContentModel
    {
        public string SiteName { get; set; } = "";
    }
}
