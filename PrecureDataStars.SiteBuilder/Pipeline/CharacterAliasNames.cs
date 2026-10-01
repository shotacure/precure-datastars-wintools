using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// キャラクターを 1 つの名前で指すときの名義の表示名。苗字の無い名義（同じキャラクターの別の名義の末尾と一致し、
/// それより短いもの。例：「志穂」と「久保田 志穂」）は、苗字のあるフルネームの名義に置き換える。
/// 置き換えた先（または名義そのもの）がキャラクターの正式名と空白の有無だけ違うときは、正式名の表記（「久保田志穂」）で出す。
/// 声の出演一覧の役名、歌唱一覧のキャラクターの行、人物詳細の役名のように「そのキャラクターを指す名前」に使い、
/// 各話のクレジットや盤の表記、キャラクター詳細の名義の一覧のように、クレジットされた表記そのものを出す場面では使わない。
/// データ読み込み直後に Pipeline が 1 度だけ作り、<see cref="BuildContext.CharacterAliasNames"/> に置く（並列のページ生成から読むだけ）。
/// </summary>
public sealed class CharacterAliasNames
{
    /// <summary>置き換える名義の alias_id → 出す表記（フルネームの名義、または正式名）。置き換えの無い名義は載せない。</summary>
    private readonly IReadOnlyDictionary<int, string> _fullNameByAliasId;

    private CharacterAliasNames(IReadOnlyDictionary<int, string> fullNameByAliasId)
    {
        _fullNameByAliasId = fullNameByAliasId;
    }

    /// <summary>未構築時（Catalog 側プレビュー等）用。置き換えをせず名義の表記をそのまま返す。</summary>
    public static CharacterAliasNames Identity { get; } = new(new Dictionary<int, string>());

    /// <summary>名義の表示名。苗字の無い名義ならフルネームの名義の表記、そうでなければ名義の表記そのもの。</summary>
    public string DisplayName(CharacterAlias alias)
        => _fullNameByAliasId.TryGetValue(alias.AliasId, out var full) ? full : alias.Name;

    /// <summary>
    /// キャラクターごとに名義を見比べ、苗字の無い名義からフルネームの名義への置き換え表を作る。
    /// 比べるときは空白を除く。末尾が一致する長い名義が 2 つ以上あれば、短い方（同じ長さなら alias_id の小さい方）を採る。
    /// 出す表記が正式名（<paramref name="characterNameById"/>）と空白の有無だけ違うときは、正式名の表記にそろえる。
    /// </summary>
    public static CharacterAliasNames Build(
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, string> characterNameById)
    {
        var map = new Dictionary<int, string>();
        foreach (var group in aliasById.Values.GroupBy(a => a.CharacterId))
        {
            characterNameById.TryGetValue(group.Key, out var formalName);
            var aliases = group.Select(a => (Alias: a, Key: Compact(a.Name))).ToList();
            foreach (var (alias, key) in aliases)
            {
                if (key.Length == 0) continue;
                var full = aliases
                    .Where(x => x.Alias.AliasId != alias.AliasId
                                && x.Key.Length > key.Length
                                && x.Key.EndsWith(key, StringComparison.Ordinal))
                    .OrderBy(x => x.Key.Length)
                    .ThenBy(x => x.Alias.AliasId)
                    .Select(x => x.Alias)
                    .FirstOrDefault();
                string display = full?.Name ?? alias.Name;
                if (!string.IsNullOrEmpty(formalName)
                    && string.Equals(Compact(display), Compact(formalName), StringComparison.Ordinal))
                {
                    display = formalName;
                }
                if (!string.Equals(display, alias.Name, StringComparison.Ordinal)) map[alias.AliasId] = display;
            }
        }
        return new CharacterAliasNames(map);
    }

    private static string Compact(string? s) => (s ?? "").Replace(" ", "").Replace("\u3000", "");
}
