using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 役職マスタ（<see cref="Role"/>）の系譜（<c>successor_role_code</c> による有向リンク）を解決し、
/// 同一クラスタに属する役職をひとまとめに扱うためのヘルパー。
/// <para>
/// 構造の前提：
/// <list type="bullet">
///   <item>各役職は最大 1 つの「後継役職」（<see cref="Role.SuccessorRoleCode"/>）を指す</item>
///   <item>後継リンクを辿って到達する役職集合 ＝ クラスタ</item>
///   <item>クラスタの「代表」役職は、後継リンクの末端（successor が NULL）のうち
///     <see cref="Role.DisplayOrder"/> 最小の役職とする</item>
///   <item>サイクル（A→B→A）は構造上不正だが、安全のため検出時はそこで打ち切る</item>
/// </list>
/// </para>
/// <para>
/// 用途：
/// <list type="bullet">
///   <item>クレジット話数ランキング集計時に同一クラスタを 1 単位として束ねる</item>
///   <item>役職別ランキング詳細ページの URL に系譜代表 role_code を採用する</item>
///   <item>表示名は系譜代表の <see cref="Role.NameJa"/> を使う</item>
/// </list>
/// </para>
/// </summary>
public sealed class RoleSuccessorResolver
{
    private readonly IReadOnlyDictionary<string, Role> _roleMap;

    /// <summary>
    /// クラスタ ID（任意の代表 role_code）→ 同クラスタに属する全 role_code 一覧の事前計算結果。
    /// </summary>
    private readonly Dictionary<string, List<string>> _clusterMembers;

    /// <summary>
    /// 任意の role_code → そのクラスタの代表 role_code の事前計算結果。
    /// </summary>
    private readonly Dictionary<string, string> _representativeOf;

    /// <summary>
    /// 役職一覧から系譜情報を構築する。コンストラクタの計算量は O(N + N log N)。
    /// </summary>
    /// <param name="roles">全役職一覧。</param>
    public RoleSuccessorResolver(IEnumerable<Role> roles)
    {
        _roleMap = roles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);

        // 1) 各 role_code から後継チェーンの末端まで辿ってクラスタを発見する。
        //    Union-Find 風に「同一クラスタ」を束ねる。
        var clusterIdOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var r in _roleMap.Values)
        {
            if (clusterIdOf.ContainsKey(r.RoleCode)) continue;

            // 後継チェーンを末端（successor が NULL もしくは未定義）まで辿る
            var chain = new List<string> { r.RoleCode };
            var visited = new HashSet<string>(StringComparer.Ordinal) { r.RoleCode };
            string current = r.RoleCode;
            while (true)
            {
                if (!_roleMap.TryGetValue(current, out var cur)) break;
                if (string.IsNullOrEmpty(cur.SuccessorRoleCode)) break;
                if (!visited.Add(cur.SuccessorRoleCode)) break; // サイクル防止
                chain.Add(cur.SuccessorRoleCode);
                current = cur.SuccessorRoleCode;
            }

            // チェーン上の終端 role_code を一旦のクラスタ ID に。後で末端集合の代表に置き換える。
            string tail = chain[^1];
            foreach (var code in chain)
            {
                clusterIdOf[code] = tail;
            }
        }

        // 2) クラスタ ID（暫定 = 末端 role_code）でグループ化して、各クラスタ内の
        //    すべてのメンバーを列挙する（逆方向 = 後継として指される側も含める）。
        //    例：B→A, C→A の場合、tail を辿るだけだと A しか拾えないが、
        //    実際には B / C も A クラスタの一員。逆引きを使って吸収する。
        var inboundMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var r in _roleMap.Values)
        {
            if (string.IsNullOrEmpty(r.SuccessorRoleCode)) continue;
            if (!inboundMap.TryGetValue(r.SuccessorRoleCode, out var list))
            {
                list = new List<string>();
                inboundMap[r.SuccessorRoleCode] = list;
            }
            list.Add(r.RoleCode);
        }

        // 3) 末端 role_code から逆方向に BFS で全メンバーを収集
        //    （末端は successor を持たない役職、すなわち後継チェーンの終端）。
        var clusterTails = new HashSet<string>(_roleMap.Values
            .Where(r => string.IsNullOrEmpty(r.SuccessorRoleCode))
            .Select(r => r.RoleCode), StringComparer.Ordinal);

        _clusterMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tail in clusterTails)
        {
            var members = new List<string>();
            var stack = new Stack<string>();
            stack.Push(tail);
            while (stack.Count > 0)
            {
                string code = stack.Pop();
                if (!assigned.Add(code)) continue;
                members.Add(code);
                if (inboundMap.TryGetValue(code, out var inbound))
                {
                    foreach (var src in inbound) stack.Push(src);
                }
            }
            _clusterMembers[tail] = members;
        }

        // 4) クラスタごとに代表を決定。末端のうち display_order 最小、同値なら role_code 昇順。
        //    後継を持たない（=末端の）役職が複数あるクラスタはあり得ないが（Union-Find により
        //    必ず 1 つにまとまる）、念のためクラスタの全メンバーから「末端」を抽出して代表を決める。
        _representativeOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (tail, members) in _clusterMembers)
        {
            // 末端候補：successor が NULL/空の役職
            var tails = members
                .Where(c => _roleMap.TryGetValue(c, out var role)
                            && string.IsNullOrEmpty(role.SuccessorRoleCode))
                .Select(c => _roleMap[c])
                .OrderBy(r => r.DisplayOrder ?? ushort.MaxValue)
                .ThenBy(r => r.RoleCode, StringComparer.Ordinal)
                .ToList();
            string representative = tails.Count > 0 ? tails[0].RoleCode : tail;
            foreach (var code in members)
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
    /// 代表 role_code を渡すと、そのクラスタに属する全メンバーの role_code 列を返す。
    /// 自身を含む。代表でない役職を渡すと、まずその代表を求めてからクラスタを返す。
    /// 該当なしのときは引数 1 件のみを含む列を返す。
    /// </summary>
    public IReadOnlyList<string> GetClusterMembers(string roleCode)
    {
        string rep = GetRepresentative(roleCode);
        if (string.IsNullOrEmpty(rep)) return Array.Empty<string>();
        // _clusterMembers は「末端 role_code をキー」にしているが、代表は末端の中でも
        // display_order 最小なので必ずしも tail と一致しない。tail から逆引きする。
        // → すべてのクラスタメンバーを総ざらいしてヒットしたエントリを返す
        foreach (var (_, members) in _clusterMembers)
        {
            if (members.Any(m => string.Equals(m, rep, StringComparison.Ordinal)))
            {
                return members;
            }
        }
        return new[] { roleCode };
    }

    /// <summary>
    /// すべてのクラスタの (代表 role_code, メンバー全件) 一覧を返す。
    /// 役職別ランキング索引ページで「クラスタ単位で 1 行表示」する用途。
    /// </summary>
    public IEnumerable<(string Representative, IReadOnlyList<string> Members)> EnumerateClusters()
    {
        // 代表 role_code でグルーピング
        var byRep = _representativeOf
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(g => (Representative: g.Key, Members: (IReadOnlyList<string>)g.Select(kv => kv.Key).ToList()));
        return byRep;
    }
}
