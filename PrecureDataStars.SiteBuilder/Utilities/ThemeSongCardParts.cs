using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 主題歌・挿入歌カード（エピソード詳細・シリーズ詳細の <c>#theme-songs</c>）の見出しに並べる区分バッジ 1 つ分。
/// 同じ録音を OP・ED・挿入歌として使ったときはカードを 1 枚にまとめ、区分バッジを使った順に並べる。
/// </summary>
public sealed class ThemeSongKindEntry
{
    /// <summary>区分ラベル（song_music_classes.name_ja。「オープニング主題歌」など）。</summary>
    public string Label { get; init; } = "";

    /// <summary>
    /// その区分で使った話数の範囲ラベル（「#1～49 (全話)」など）。シリーズ詳細のエピソードを持つ系列だけが持ち、
    /// ほかは空文字。区分が 1 つのカードでは曲名の後ろに、2 つ以上のカードでは各区分バッジの直後に出す。
    /// </summary>
    public string RangeLabel { get; init; } = "";
}

/// <summary>
/// 主題歌・挿入歌カードの再生ボタン 1 つ分。カードの中で使ったサイズごとに、使った順に並ぶ
/// （鳴らす音源が同じならボタンは 1 つ）。
/// </summary>
public sealed class ThemeSongPlay
{
    /// <summary>鳴らす YouTube 動画 ID。</summary>
    public string ArtTrackId { get; init; } = "";
    /// <summary>配信音源が YouTube Music Premium 会員限定か。</summary>
    public bool PremiumOnly { get; init; }
    /// <summary>音源が入っている盤の表記（プレイヤーの補足に出す）。</summary>
    public string SourceAlbum { get; init; } = "";
    /// <summary>会員限定のときの代わり（誰でも再生できる同じサイズの音源）。無ければ空文字。</summary>
    public string AltArtTrackId { get; init; } = "";
    /// <summary>代わりの音源が入っている盤の表記。</summary>
    public string AltSourceAlbum { get; init; } = "";
    /// <summary>
    /// 鳴らす音源のサイズの名前（「TVサイズ」など）。そのサイズの音源が見つかったときだけ入れ、
    /// サイズの指定が無い・そのサイズの音源が無く録音の既定の音源で鳴らすときは空文字（ボタンの説明にサイズを書かない）。
    /// </summary>
    public string SizeLabel { get; init; } = "";

    /// <summary>
    /// 実際に鳴る音源のトラックのサイズの名前（「フルサイズ」など）。録音の既定の音源へ落ちたときも入る。トラックのサイズが未設定なら空文字。
    /// 札の付かないボタンの左に、どのサイズが鳴るかを示す札として出す。
    /// </summary>
    public string SourceSizeLabel { get; init; } = "";

    /// <summary>ボタンの説明に書くサイズの名前（指定どおりのサイズで鳴るならそのサイズ、そうでなければ実際に鳴る音源のサイズ）。</summary>
    public string DisplaySizeLabel => SizeLabel.Length > 0 ? SizeLabel : SourceSizeLabel;
}

/// <summary>
/// サイズの札を縦に並べるときの 1 行（デパプリの ED の Type.I〜IV のように、1 枚のカードで違うサイズを 2 つ以上使ったとき）。
/// 札の右に、そのサイズの音源の再生ボタンを置く。
/// </summary>
public sealed class ThemeSongSizeRow
{
    /// <summary>サイズの名前（「TVサイズ Type.I」など）。</summary>
    public string Label { get; init; } = "";

    /// <summary>そのサイズの音源の再生ボタン。そのサイズの音源が無ければ null（札だけ出す）。</summary>
    public ThemeSongPlay? Play { get; init; }

    /// <summary>
    /// そのサイズが流れた話数（「#1～3, 10, 13, 16」など）。札の左に出す。札が 2 つ以上のカード
    /// （サイズが話によって分かれるとき）で、シリーズ詳細のエピソードを持つ系列だけが持ち、ほかは空文字。
    /// </summary>
    public string RangeLabel { get; init; } = "";
}

/// <summary>主題歌カードを録音ごとに 1 枚へまとめるときの、区分・サイズ・再生ボタン・備考の足し込み。</summary>
public static class ThemeSongCardParts
{
    /// <summary>
    /// サイズの札を縦に並べる行。札ごとに、そのサイズの名前の付いた再生ボタンを添える（そのサイズの音源が無ければ札だけ）。
    /// 札が 2 つ以上のカードと、札が 1 つでそのサイズの音源で鳴るカード（札をボタンの左へ移す）で作る。
    /// 札が無いカードと、札が 1 つでもそのサイズの音源が無いカードは空（札は曲名の後ろ）。
    /// 札が 2 つ以上で <paramref name="episodeNosByLabel"/> があれば、札ごとにそのサイズが流れた話数を添える
    /// （札が 1 つなら曲の使用話数と同じなので添えない）。
    /// </summary>
    public static List<ThemeSongSizeRow> SizeRows(
        IReadOnlyList<string> sizeLabels,
        IReadOnlyList<ThemeSongPlay> plays,
        IReadOnlyDictionary<string, HashSet<int>>? episodeNosByLabel = null)
    {
        if (sizeLabels.Count == 0) return [];
        if (sizeLabels.Count == 1 && !plays.Any(p => string.Equals(p.SizeLabel, sizeLabels[0], StringComparison.Ordinal))) return [];
        bool withRange = sizeLabels.Count > 1 && episodeNosByLabel is not null;
        return sizeLabels.Select(l => new ThemeSongSizeRow
        {
            Label = l,
            Play = plays.FirstOrDefault(p => string.Equals(p.SizeLabel, l, StringComparison.Ordinal)),
            RangeLabel = withRange && episodeNosByLabel!.TryGetValue(l, out var nos)
                ? EpisodeRangeCompressor.Compress(nos)
                : ""
        }).ToList();
    }

    /// <summary>サイズの名前ごとの話数の集合に、話数を足す（同じ録音を OP・ED で使ったカードをまとめるときも和を取る）。</summary>
    public static void AddEpisodeNos(Dictionary<string, HashSet<int>> episodeNosByLabel, string sizeLabel, IEnumerable<int> episodeNos)
    {
        if (!episodeNosByLabel.TryGetValue(sizeLabel, out var set))
        {
            set = new HashSet<int>();
            episodeNosByLabel[sizeLabel] = set;
        }
        set.UnionWith(episodeNos);
    }

    /// <summary>
    /// 札の付かない再生ボタン。札を縦に並べるカードでは、どの札にも当たらないもの
    /// （サイズの指定が無い話の、録音の既定の音源など）だけ。札を縦に並べないカードではすべて。
    /// </summary>
    public static List<ThemeSongPlay> LoosePlays(IReadOnlyList<string> sizeLabels, IReadOnlyList<ThemeSongPlay> plays)
        => SizeRows(sizeLabels, plays).Count == 0
            ? plays.ToList()
            : plays.Where(p => !sizeLabels.Contains(p.SizeLabel, StringComparer.Ordinal)).ToList();

    /// <summary>
    /// 録音 × サイズの再生ボタンを選ぶ。サイズが指定されていてそのサイズの音源があればそれ（サイズの名前つき）、
    /// 無ければ（サイズ未指定も）録音ごとに選んだ音源（サイズの名前なし）。どちらも無ければ null。
    /// </summary>
    public static ThemeSongPlay? ResolvePlay(
        ThemeArtTrackIndex index,
        int songRecordingId,
        string? sizeVariantCode,
        IReadOnlyDictionary<string, string> sizeLabelByCode)
    {
        var artTrack = index.Resolve(songRecordingId, sizeVariantCode);
        if (artTrack is null) return null;
        // そのサイズの音源で鳴らすときだけサイズの名前を添える（録音の既定の音源へ落ちたときは添えない）。
        bool isSized = !string.IsNullOrEmpty(sizeVariantCode)
            && index.ByRecordingAndSize.ContainsKey((songRecordingId, sizeVariantCode));
        string sizeLabel = isSized && sizeLabelByCode.TryGetValue(sizeVariantCode!, out var label) ? label : "";
        string sourceSizeLabel = sizeLabelByCode.TryGetValue(artTrack.SizeVariantCode, out var sourceLabel) ? sourceLabel : "";
        return FromArtTrack(artTrack, sizeLabel, sourceSizeLabel);
    }

    /// <summary>
    /// 再生ボタンを足す。同じ音源（動画 ID）のボタンが既にあれば足さない。ただし既にあるボタンにサイズの名前が無く、
    /// 足そうとしたボタンにはあるときは、位置はそのままでサイズの名前つきのものに差し替える。
    /// </summary>
    public static void AddPlay(List<ThemeSongPlay> plays, ThemeSongPlay? play)
    {
        if (play is null || play.ArtTrackId.Length == 0) return;
        int at = plays.FindIndex(p => string.Equals(p.ArtTrackId, play.ArtTrackId, StringComparison.Ordinal));
        if (at < 0)
        {
            plays.Add(play);
            return;
        }
        if (plays[at].SizeLabel.Length == 0 && play.SizeLabel.Length > 0) plays[at] = play;
    }

    /// <summary>区分バッジを足す。ラベルと範囲がどちらも同じものが既にあれば足さない。</summary>
    public static void AddKind(List<ThemeSongKindEntry> kinds, ThemeSongKindEntry kind)
    {
        if (kinds.Any(k => string.Equals(k.Label, kind.Label, StringComparison.Ordinal)
                           && string.Equals(k.RangeLabel, kind.RangeLabel, StringComparison.Ordinal))) return;
        kinds.Add(kind);
    }

    /// <summary>空でない文字列を、まだ無ければ足す（サイズの名前の並び）。</summary>
    public static void AddDistinct(List<string> list, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (list.Contains(value, StringComparer.Ordinal)) return;
        list.Add(value);
    }

    /// <summary>備考をつなぐ。空なら今のまま、同じ備考が既に含まれていればつながない。</summary>
    public static string AppendNote(string current, string? note)
    {
        if (string.IsNullOrEmpty(note)) return current;
        if (current.Length == 0) return note;
        return current.Split(" / ").Contains(note, StringComparer.Ordinal) ? current : current + " / " + note;
    }

    private static ThemeSongPlay FromArtTrack(ThemeArtTrack at, string sizeLabel, string sourceSizeLabel) => new()
    {
        ArtTrackId = at.ArtTrackId,
        PremiumOnly = at.PremiumOnly,
        SourceAlbum = at.SourceAlbum,
        AltArtTrackId = at.AltArtTrackId,
        AltSourceAlbum = at.AltSourceAlbum,
        SizeLabel = sizeLabel,
        SourceSizeLabel = sourceSizeLabel
    };
}
