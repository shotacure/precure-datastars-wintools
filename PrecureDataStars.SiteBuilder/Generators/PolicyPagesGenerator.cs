using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 法律・運営情報系の補助ページを生成するジェネレータ（v1.3.1 追加）。
/// <para>
/// 生成対象：
/// </para>
/// <list type="bullet">
///   <item><description><c>/privacy/</c> — プライバシーポリシー。Cookie の使用、
///     Google Analytics 4 によるアクセス解析、Google AdSense による広告配信、
///     第三者配信事業者からの DoubleClick Cookie の利用、ユーザによる無効化方法等を明示する。</description></item>
///   <item><description><c>/disclaimer/</c> — 免責事項。情報の正確性に関する免責、
///     外部リンクに対する責任範囲、引用素材の権利者帰属を明示する。</description></item>
///   <item><description><c>/contact/</c> — お問い合わせページ。X / メールでの連絡先と、
///     情報の修正報告に関する案内を提示する（既存フッタの contact セクションを正式ページ化）。</description></item>
/// </list>
/// <para>
/// 本ジェネレータは <see cref="AboutGenerator"/> と同じ立ち位置（独立した静的ページ群の生成）に
/// 配置し、コンテンツモデルは渡すサイト名と AdSense クライアント ID の存否のみ。
/// AdSense クライアント ID が設定されているときは <c>/privacy/</c> 内の関連節を出力に含める判断を
/// テンプレ側で行う運用とする。
/// </para>
/// </summary>
public sealed class PolicyPagesGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;
    private readonly PageRenderer _page;

    public PolicyPagesGenerator(BuildContext ctx, BuildConfig config, PageRenderer page)
    {
        _ctx = ctx;
        _config = config;
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

    /// <summary>
    /// <c>/privacy/</c> — プライバシーポリシー。
    /// テンプレでは「アクセス解析」「広告配信」「Cookie」「第三者からの提供」「無効化方法」「お問い合わせ」の
    /// 6 セクションで構成し、AdSense / GA4 が設定されているかをモデルの bool で判定して
    /// 該当節の出し分けを行う。
    /// </summary>
    private void GeneratePrivacy()
    {
        var content = new PrivacyContentModel
        {
            SiteName = _ctx.Config.SiteName,
            HasAdSense = !string.IsNullOrEmpty(_config.GoogleAdSenseClientId),
            HasGa4 = !string.IsNullOrEmpty(_config.Ga4MeasurementId),
        };
        var layout = new LayoutModel
        {
            PageTitle = "プライバシーポリシー",
            MetaDescription = $"{_ctx.Config.SiteName} のプライバシーポリシー。Cookie、アクセス解析、広告配信に関する情報。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プライバシーポリシー", Url = "" }
            }
        };
        _page.RenderAndWrite("/privacy/", "policy", "privacy.sbn", content, layout);
    }

    /// <summary>
    /// <c>/disclaimer/</c> — 免責事項。
    /// </summary>
    private void GenerateDisclaimer()
    {
        var content = new DisclaimerContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "免責事項",
            MetaDescription = $"{_ctx.Config.SiteName} の免責事項。情報の正確性・外部リンク・著作権に関する取り扱い。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "免責事項", Url = "" }
            }
        };
        _page.RenderAndWrite("/disclaimer/", "policy", "disclaimer.sbn", content, layout);
    }

    /// <summary>
    /// <c>/contact/</c> — お問い合わせページ。
    /// </summary>
    private void GenerateContact()
    {
        var content = new ContactContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "お問い合わせ",
            MetaDescription = $"{_ctx.Config.SiteName} へのお問い合わせ。誤情報のご指摘、ご意見、利用に関するご質問はこちらから。",
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

        /// <summary>AdSense クライアント ID が設定されているか。広告配信節の出し分けに使う。</summary>
        public bool HasAdSense { get; set; }

        /// <summary>GA4 メジャメント ID が設定されているか。アクセス解析節の出し分けに使う。</summary>
        public bool HasGa4 { get; set; }
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
