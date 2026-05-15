using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// クレジット階層から拾った <c>person_alias_id</c> を、人物詳細ページへの HTML リンクに解決するための共通ヘルパ。
/// <para>
/// 同名（同 alias を共有）人物の概念をサイト上で可視化するため、<c>person_alias_persons</c> 中間テーブルを
/// 起動時 1 回だけ全件ロードして <c>alias_id → list&lt;person_id&gt;</c> の逆引き辞書を構築し、
/// 1 alias で 1 person の通常ケースは単一リンク、複数 person で共有されているケースは
/// 「山田太郎[1] 山田太郎[2]」のように添字付きリンクを並べる出力を返す。
/// </para>
/// <para>
/// 添字の付与順（誰が [1] になるか）は <c>person_seq</c> 昇順 → <c>person_id</c> 昇順で安定化。
/// 並び順自体は中間表の <c>person_seq</c> 既定値（共同名義における表示順）に従う。
/// </para>
/// <para>
/// 表示テキスト（<paramref name="displayText"/>）には <see cref="PersonAlias.DisplayTextOverride"/>
/// または <see cref="PersonAlias.Name"/> をそのまま渡す（呼び出し元で解決済みの状態）。
/// 当ヘルパはこの文字列を HTML エスケープして &lt;a&gt; タグでラップする。
/// </para>
/// </summary>
public sealed class StaffNameLinkResolver
{
    /// <summary>alias_id → 紐付く全 person_id（person_seq 昇順 → person_id 昇順で安定化済み）。</summary>
    private readonly IReadOnlyDictionary<int, IReadOnlyList<int>> _personIdsByAliasId;

    private StaffNameLinkResolver(IReadOnlyDictionary<int, IReadOnlyList<int>> personIdsByAliasId)
    {
        _personIdsByAliasId = personIdsByAliasId;
    }

    /// <summary>
    /// 起動時に 1 度だけ呼び出すファクトリ。<c>person_alias_persons</c> を全件ロードして辞書化する。
    /// </summary>
    public static async Task<StaffNameLinkResolver> CreateAsync(IConnectionFactory factory, CancellationToken ct = default)
    {
        var repo = new PersonAliasPersonsRepository(factory);
        var rows = await repo.GetAllAsync(ct).ConfigureAwait(false);

        // person_seq 昇順 → person_id 昇順で安定化したリストにグルーピング。
        var dict = rows
            .GroupBy(r => r.AliasId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.OrderBy(r => r.PersonSeq).ThenBy(r => r.PersonId).Select(r => r.PersonId).ToList());

        return new StaffNameLinkResolver(dict);
    }

    /// <summary>
    /// 指定 alias_id と表示文字列を、人物詳細ページへの HTML リンク（または平文）に変換する。
    /// </summary>
    /// <param name="personAliasId">PERSON エントリの person_alias_id。NULL の場合はリンク化せず平文を返す。</param>
    /// <param name="displayText">表示用文字列（DisplayTextOverride または Name）。null/空のときは空文字を返す。</param>
    /// <returns>
    /// HTML として直接埋め込み可能な文字列。
    /// <list type="bullet">
    ///   <item><description>person_alias_id が NULL またはマッチする人物が無い: <c>{displayText}</c>（HTML エスケープのみ）</description></item>
    ///   <item><description>1 人物のみ: <c>&lt;a href="/persons/{id}/"&gt;{displayText}&lt;/a&gt;</c></description></item>
    ///   <item><description>複数人物（共有 alias）: <c>&lt;a href="..."&gt;{displayText}[1]&lt;/a&gt; &lt;a href="..."&gt;{displayText}[2]&lt;/a&gt;</c>（半角スペース区切り）</description></item>
    /// </list>
    /// </returns>
    public string ResolveAsHtml(int? personAliasId, string? displayText)
    {
        if (string.IsNullOrEmpty(displayText)) return "";

        // alias_id が無い、もしくは中間表に対応行が無い → リンク化せず HTML エスケープのみ。
        if (!personAliasId.HasValue
            || !_personIdsByAliasId.TryGetValue(personAliasId.Value, out var personIds)
            || personIds.Count == 0)
        {
            return Escape(displayText);
        }

        // 1 人物のみ → 単純な単一リンク。
        if (personIds.Count == 1)
        {
            return $"<a href=\"/persons/{personIds[0]}/\">{Escape(displayText)}</a>";
        }

        // 複数人物 → 「{displayText}[1] {displayText}[2] ...」の添字付き複数リンク。
        // 添字は 1 始まり。区切り文字は半角スペース（CSS の white-space で折り返し制御可能）。
        var parts = new List<string>(personIds.Count);
        for (int i = 0; i < personIds.Count; i++)
        {
            parts.Add($"<a href=\"/persons/{personIds[i]}/\">{Escape(displayText)}[{i + 1}]</a>");
        }
        return string.Join(" ", parts);
    }

    /// <summary>HTML 5 における &amp;・&lt;・&gt;・&quot;・&#39; の最小限のエスケープ。</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
}