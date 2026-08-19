namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>
/// 「この日に何があったか」を表す記念日 1 件。エピソードの放送、映画の公開、
/// キャラクター・人物の誕生日という出自の異なる出来事を 1 つの型に束ねた union で、
/// <see cref="Kind"/> によって意味のあるプロパティが変わる。
///
/// <para>
/// ホームのカレンダー（クライアント側 JS へ渡す JSON）と、日付別の記念日ページの
/// 双方が同じ集合を参照するための共有モデル。どちらか一方だけが持つ概念を作らないことで、
/// 「ホームには出るが記念日ページには出ない」といった食い違いが生まれないようにする。
/// </para>
/// </summary>
public sealed record AnniversaryEntry
{
    /// <summary>出来事の種別。<c>ep</c>=エピソード放送 / <c>mv</c>=映画公開 / <c>cb</c>=キャラクター誕生日 / <c>pb</c>=人物誕生日。</summary>
    public required string Kind { get; init; }

    /// <summary>出来事の月（1–12）。</summary>
    public required int Month { get; init; }

    /// <summary>出来事の日（1–31）。</summary>
    public required int Day { get; init; }

    /// <summary>出来事の年。誕生日（<c>cb</c>）のように年を持たない種別では null。</summary>
    public int? Year { get; init; }

    // ── シリーズ（ep / mv / cb の所属作品） ──

    /// <summary>シリーズ正式タイトル。</summary>
    public string SeriesTitle { get; init; } = "";

    /// <summary>シリーズの slug。</summary>
    public string SeriesSlug { get; init; } = "";

    /// <summary>カレンダーのコンパクト表示用の略称（未設定なら正式タイトル）。</summary>
    public string SeriesTitleShort { get; init; } = "";

    /// <summary>シリーズ詳細ページの URL。</summary>
    public string SeriesUrl { get; init; } = "";

    /// <summary>シリーズの放送・公開開始年。</summary>
    public int SeriesStartYear { get; init; }

    // ── エピソード（ep） ──

    /// <summary>シリーズ内話数。</summary>
    public int EpisodeNo { get; init; }

    /// <summary>サブタイトル（未確定話はプレースホルダ表記）。</summary>
    public string EpisodeTitle { get; init; } = "";

    /// <summary>エピソード詳細ページの URL。</summary>
    public string EpisodeUrl { get; init; } = "";

    /// <summary>第 1 話かどうか。</summary>
    public bool IsFirstEpisode { get; init; }

    /// <summary>最終話かどうか（<c>series.episodes</c> のマスタ総話数に一致する回）。</summary>
    public bool IsLastEpisode { get; init; }

    /// <summary>
    /// サブタイトル解禁時刻（ISO 8601）。解禁時刻を算出できた話のみ非空。
    /// 解禁済みかどうかは示さない（直近に解禁された話も値を持つ）。
    /// </summary>
    public string RevealAtIso { get; init; } = "";

    /// <summary>サブタイトル解禁時刻。解禁済みかどうかの判定は参照側が現在時刻と比較して行う。</summary>
    public DateTimeOffset? RevealAt { get; init; }

    // ── キャラクター誕生日（cb） ──

    /// <summary>キャラクターの正式名称。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>表示用名義（プリキュアなら変身前名義、それ以外は正式名称）。</summary>
    public string CharacterDisplayName { get; init; } = "";

    /// <summary>キャラクター詳細ページの URL。</summary>
    public string CharacterUrl { get; init; } = "";

    /// <summary>イメージカラー由来のバッジ配色（背景 / 文字 / 枠線）。</summary>
    public string KeyColorBackground { get; init; } = "";
    public string KeyColorForeground { get; init; } = "";
    public string KeyColorBorder { get; init; } = "";

    // ── 人物誕生日（pb） ──

    /// <summary>人物の氏名。</summary>
    public string PersonName { get; init; } = "";

    /// <summary>人物詳細ページの URL。</summary>
    public string PersonUrl { get; init; } = "";

    /// <summary>生年。公開設定が <c>PUBLIC</c> かつ判明している場合のみ非 null。</summary>
    public int? BirthYear { get; init; }
}
