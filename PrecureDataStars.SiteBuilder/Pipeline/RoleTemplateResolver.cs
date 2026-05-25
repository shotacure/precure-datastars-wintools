using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 役職テンプレ（<c>role_templates</c>）の (role_code, series_id) → <see cref="RoleTemplate"/> 解決を
/// メモリ辞書で同期実行するヘルパ。SiteDataLoader が起動時に
/// <see cref="Data.Repositories.RoleTemplatesRepository.GetAllAsync"/> を 1 度呼んで全件取得した結果を
/// 渡して構築する。
/// <para>
/// 旧 <see cref="Data.Repositories.RoleTemplatesRepository.ResolveAsync"/> は (role_code, series_id) →
/// (role_code, NULL) のフォールバックを 1 SQL（UNION ALL + priority ソート）でやっていたが、
/// CreditTreeRenderer がクレジット内の役職ごとにこれを発火するため、クレジット入りエピソードでは
/// per-credit 数十回オーダーの DB 往復になっていた。本クラスはその挙動を C# 側の辞書 lookup に置き換える。
/// </para>
/// </summary>
public sealed class RoleTemplateResolver
{
    private readonly IReadOnlyDictionary<(string RoleCode, int SeriesId), RoleTemplate> _bySeries;
    private readonly IReadOnlyDictionary<string, RoleTemplate> _byDefault;

    /// <summary>
    /// 全 role_templates 行を受け取り、(role_code, series_id) 別と既定 (series_id=NULL) 別の 2 段辞書を構築する。
    /// 同一キーで重複がある場合は最初の 1 件を採用する（マスタ UNIQUE で実質発生しない想定）。
    /// </summary>
    public RoleTemplateResolver(IEnumerable<RoleTemplate> allTemplates)
    {
        var bySeries = new Dictionary<(string, int), RoleTemplate>();
        var byDefault = new Dictionary<string, RoleTemplate>(StringComparer.Ordinal);
        foreach (var t in allTemplates)
        {
            if (string.IsNullOrEmpty(t.RoleCode)) continue;
            if (t.SeriesId is int sid)
            {
                var key = (t.RoleCode, sid);
                if (!bySeries.ContainsKey(key)) bySeries[key] = t;
            }
            else
            {
                if (!byDefault.ContainsKey(t.RoleCode)) byDefault[t.RoleCode] = t;
            }
        }
        _bySeries = bySeries;
        _byDefault = byDefault;
    }

    /// <summary>
    /// (role_code, series_id) で見つかればそれを返し、無ければ (role_code, NULL) の既定テンプレを返す。
    /// 既定も無ければ null。<see cref="Data.Repositories.RoleTemplatesRepository.ResolveAsync"/> と同じ
    /// 優先順位ルール（シリーズ別オーバーライド優先 → 既定フォールバック）。
    /// </summary>
    public RoleTemplate? Resolve(string roleCode, int? seriesId)
    {
        if (string.IsNullOrEmpty(roleCode)) return null;
        if (seriesId is int sid && _bySeries.TryGetValue((roleCode, sid), out var hit)) return hit;
        return _byDefault.TryGetValue(roleCode, out var def) ? def : null;
    }
}
