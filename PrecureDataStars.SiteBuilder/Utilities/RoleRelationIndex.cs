using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 役職どうしの関連（<see cref="RoleRelation"/>：段階・並列）を、系譜の代表 role_code 単位に引けるようにした索引。
/// <list type="bullet">
///   <item><description>関係の両端は <see cref="RoleSuccessorResolver.GetRepresentative"/> で系譜の代表へ寄せる
///     （撮影監督 → デジタル撮影監督 のように系譜でまとめた役職は、代表 1 つとして扱う）。</description></item>
///   <item><description>並列でつながった代表どうし（連結成分）を 1 つの「段」にまとめる。段は並列の相手がいない代表なら自分 1 つだけ。</description></item>
///   <item><description>段階は段どうしの有向辺として持つ（同じ段の中の段階は捨てる）。</description></item>
/// </list>
/// 用途は役職詳細の年表に重ねる関連する役職（前段階・並列・後段階）の判定。
/// 構築後は読み取り専用で、並列のページ生成から同時に引いてよい。
/// </summary>
public sealed class RoleRelationIndex
{
    /// <summary>代表 role_code → 段の ID（段の中で display_order 最小の代表）。</summary>
    private readonly Dictionary<string, string> _stageOf = new(StringComparer.Ordinal);

    /// <summary>段の ID → 後段階の段の ID。</summary>
    private readonly Dictionary<string, HashSet<string>> _nextStages = new(StringComparer.Ordinal);

    /// <summary>段の ID → 前段階の段の ID。</summary>
    private readonly Dictionary<string, HashSet<string>> _prevStages = new(StringComparer.Ordinal);

    private readonly RoleSuccessorResolver _resolver;

    /// <summary>関連の全件と役職マスタ・系譜から索引を組み立てる。</summary>
    public RoleRelationIndex(IEnumerable<Role> roles, IEnumerable<RoleRelation> relations, RoleSuccessorResolver resolver)
    {
        _resolver = resolver;
        var roleMap = roles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        var rels = relations
            .Where(r => roleMap.ContainsKey(r.FromRoleCode) && roleMap.ContainsKey(r.ToRoleCode))
            .Select(r => (From: resolver.GetRepresentative(r.FromRoleCode), To: resolver.GetRepresentative(r.ToRoleCode), r.RelationKind))
            .Where(r => !string.Equals(r.From, r.To, StringComparison.Ordinal))
            .ToList();

        // 並列の連結成分を Union-Find で求める（関係に出てくる代表だけを節点にする）。
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string x)
        {
            if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
            while (!string.Equals(p, x, StringComparison.Ordinal))
            {
                var gp = parent[p];
                parent[x] = gp;
                x = p;
                p = gp;
            }
            return x;
        }
        foreach (var (from, to, _) in rels)
        {
            Find(from);
            Find(to);
        }
        foreach (var (from, to, kind) in rels)
        {
            if (!string.Equals(kind, RoleRelationKinds.Parallel, StringComparison.Ordinal)) continue;
            string a = Find(from), b = Find(to);
            if (!string.Equals(a, b, StringComparison.Ordinal)) parent[a] = b;
        }

        // 段の ID は段の中で display_order 最小の代表（同点は role_code 昇順）にそろえる。
        int Order(string code) => roleMap.TryGetValue(code, out var r) ? r.DisplayOrder ?? ushort.MaxValue : ushort.MaxValue;
        foreach (var group in parent.Keys.ToList().GroupBy(Find, StringComparer.Ordinal))
        {
            var members = group
                .OrderBy(Order)
                .ThenBy(c => c, StringComparer.Ordinal)
                .ToList();
            string stageId = members[0];
            foreach (var m in members) _stageOf[m] = stageId;
        }

        foreach (var (from, to, kind) in rels)
        {
            if (!string.Equals(kind, RoleRelationKinds.StepUp, StringComparison.Ordinal)) continue;
            string sf = _stageOf[from], st = _stageOf[to];
            if (string.Equals(sf, st, StringComparison.Ordinal)) continue;
            Add(_nextStages, sf, st);
            Add(_prevStages, st, sf);
        }

        static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }
            set.Add(value);
        }
    }

    /// <summary>関連が 1 件も登録されていないか。</summary>
    public bool IsEmpty => _stageOf.Count == 0;

    /// <summary>
    /// 役職詳細のページの役職 <paramref name="pageRoleCode"/> から見た、役職 <paramref name="roleCode"/> の関連の種類。
    /// 同じ段の別の役職なら <see cref="RoleRelationLane.Parallel"/>、前段階の段の役職なら <see cref="RoleRelationLane.Before"/>、
    /// 後段階の段の役職なら <see cref="RoleRelationLane.After"/>。系譜で同じ役職（代表が同じ）や、関連の無い役職は null。
    /// </summary>
    public RoleRelationLane? LaneOf(string pageRoleCode, string roleCode)
    {
        string pageRep = _resolver.GetRepresentative(pageRoleCode);
        string rep = _resolver.GetRepresentative(roleCode);
        if (string.Equals(pageRep, rep, StringComparison.Ordinal)) return null;
        if (!_stageOf.TryGetValue(pageRep, out var pageStage) || !_stageOf.TryGetValue(rep, out var stage)) return null;
        if (string.Equals(pageStage, stage, StringComparison.Ordinal)) return RoleRelationLane.Parallel;
        if (_prevStages.TryGetValue(pageStage, out var prev) && prev.Contains(stage)) return RoleRelationLane.Before;
        if (_nextStages.TryGetValue(pageStage, out var next) && next.Contains(stage)) return RoleRelationLane.After;
        return null;
    }
}

/// <summary>役職詳細の年表で、ページの役職から見た関連する役職の種類（帯の色を分ける）。</summary>
public enum RoleRelationLane
{
    /// <summary>前段階（ページの役職の前に担うことの多い役職）。</summary>
    Before,
    /// <summary>並列（ページの役職と並んで担う役職）。</summary>
    Parallel,
    /// <summary>後段階（ページの役職の後に担うことの多い役職）。</summary>
    After
}
