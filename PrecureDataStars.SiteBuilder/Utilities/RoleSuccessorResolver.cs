using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 役職マスタ（<see cref="Role"/>）と役職系譜（<see cref="RoleSuccession"/>）から
/// クラスタ（同一役職の歴代の名前のすべて）を構築するヘルパー
/// （多対多対応に書き換え）。
/// <para>
/// 構造の前提：
/// <list type="bullet">
///   <item><see cref="RoleSuccession"/> は from_role_code → to_role_code の有向辺だが、
///     クラスタを求めるときは「無向辺」とみなして連結成分をたどる</item>
///   <item>同じ from から複数の to を持てるので分裂が表現可能（A → B かつ A → C）</item>
///   <item>複数の from から同じ to を持てるので併合が表現可能（B → A かつ C → A）</item>
///   <item>連結成分（クラスタ）の代表は、メンバー中で <see cref="Role.DisplayOrder"/> 最小の役職
///     （同点は role_code 昇順）。代表は末端である必要は無い（多対多では「末端」の概念が無いため）</item>
/// </list>
/// </para>
/// <para>
/// 用途：
/// <list type="bullet">
///   <item>クレジット話数ランキング集計時に同一クラスタを 1 単位として束ねる</item>
///   <item>役職別ランキング詳細ページの URL に系譜代表 role_code を採用する</item>
///   <item>表示名は系譜代表の <see cref="Role.NameJa"/> を使う</item>
///   <item>クラスタ内の歴代の名前は詳細ページで「歴代名」セクションとして並べる</item>
/// </list>
/// </para>
/// </summary>
public sealed class RoleSuccessorResolver
{
    private readonly IReadOnlyDictionary<string, Role> _roleMap;

    /// <summary>
    /// 任意の role_code → そのクラスタの代表 role_code の事前計算結果。
    /// </summary>
    private readonly Dictionary<string, string> _representativeOf;

    /// <summary>
    /// 代表 role_code → 同クラスタに属する全 role_code 一覧の事前計算結果。
    /// </summary>
    private readonly Dictionary<string, List<string>> _membersByRep;

    /// <summary>
    /// 役職マスタと系譜情報から系譜情報を構築する。
    /// 計算量は O(N + E)（N: 役職数, E: 系譜辺数）。
    /// </summary>
    /// <param name="roles">全役職一覧。</param>
    /// <param name="successions">系譜の関係エンティティ全件。</param>
    public RoleSuccessorResolver(IEnumerable<Role> roles, IEnumerable<RoleSuccession> successions)
    {
        _roleMap = roles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);

        // Union-Find で連結成分を構築する。
        // 系譜辺 from↔to を「無向辺」として扱うため、両方向に union する。
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in _roleMap.Keys) parent[code] = code;

        string Find(string x)
        {
            // 経路圧縮付き find。
            var path = new List<string>();
            while (!string.Equals(parent[x], x, StringComparison.Ordinal))
            {
                path.Add(x);
                x = parent[x];
            }
            foreach (var p in path) parent[p] = x;
            return x;
        }

        void Union(string a, string b)
        {
            // 末端と末端を 1 本に揃える。代表選びは後段で行うのでここでは順序気にしない。
            string ra = Find(a), rb = Find(b);
            if (!string.Equals(ra, rb, StringComparison.Ordinal))
            {
                parent[ra] = rb;
            }
        }

        // 系譜辺で union。from / to のいずれかが roles に存在しない場合は
        // FK 制約で本来発生しないが、防御的に弾く。
        foreach (var s in successions)
        {
            if (!parent.ContainsKey(s.FromRoleCode)) continue;
            if (!parent.ContainsKey(s.ToRoleCode)) continue;
            Union(s.FromRoleCode, s.ToRoleCode);
        }

        // クラスタごとにメンバーを集約。
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var code in _roleMap.Keys)
        {
            string root = Find(code);
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<string>();
                groups[root] = list;
            }
            list.Add(code);
        }

        // 各クラスタの代表を決定：display_order 最小（同点は role_code 昇順）。
        // 代表は連結成分の中で「最も上位に並ぶ役職」とみなすので、運用者が display_order を
        // 並び替えれば代表も切り替わる。
        _representativeOf = new Dictionary<string, string>(StringComparer.Ordinal);
        _membersByRep = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, members) in groups)
        {
            var sortedMembers = members
                .Select(c => _roleMap[c])
                .OrderBy(r => r.DisplayOrder ?? ushort.MaxValue)
                .ThenBy(r => r.RoleCode, StringComparer.Ordinal)
                .Select(r => r.RoleCode)
                .ToList();
            string representative = sortedMembers[0];
            _membersByRep[representative] = sortedMembers;
            foreach (var code in sortedMembers)
            {
                _representativeOf[code] = representative;
            }
        }
    }

    /// <summary>
    /// 指定 role_code が属するクラスタの代表 role_code を返す。
    /// 系譜情報が無い役職は自分自身を代表として返す。
    /// 未登録の role_code は引数そのものを返す（フォールバック）。
    /// </summary>
    public string GetRepresentative(string? roleCode)
    {
        if (string.IsNullOrEmpty(roleCode)) return string.Empty;
        return _representativeOf.TryGetValue(roleCode!, out var rep) ? rep : roleCode!;
    }

    /// <summary>
    /// クラスタに属する全メンバーの role_code 列を返す（display_order 順、同点 role_code 昇順）。
    /// 渡す role_code はクラスタ内のどれでもよい（代表でも非代表でも自動的に解決）。
    /// 未登録の role_code は引数 1 件のみを含む列を返す（フォールバック）。
    /// </summary>
    public IReadOnlyList<string> GetClusterMembers(string roleCode)
    {
        string rep = GetRepresentative(roleCode);
        if (string.IsNullOrEmpty(rep)) return Array.Empty<string>();
        return _membersByRep.TryGetValue(rep, out var members)
            ? members
            : new[] { roleCode };
    }

    /// <summary>
    /// すべてのクラスタの (代表 role_code, メンバー全件) 一覧を返す。
    /// 役職別ランキング索引ページで「クラスタ単位で 1 行表示」する用途。
    /// 並び順は代表の display_order 昇順（同点は role_code 昇順）。
    /// </summary>
    public IEnumerable<(string Representative, IReadOnlyList<string> Members)> EnumerateClusters()
    {
        return _membersByRep
            .Select(kv => (Rep: kv.Key, Members: kv.Value, Role: _roleMap[kv.Key]))
            .OrderBy(x => x.Role.DisplayOrder ?? ushort.MaxValue)
            .ThenBy(x => x.Rep, StringComparer.Ordinal)
            .Select(x => (x.Rep, (IReadOnlyList<string>)x.Members));
    }
}