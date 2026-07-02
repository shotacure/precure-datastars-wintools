namespace PrecureDataStars.SiteBuilder.Generators;

// 人物・企業詳細ページの「クレジット履歴」「声の出演」セクションで共有する表示用 DTO 群。
// PersonsGenerator / CompaniesGenerator の両方から参照されるため、単一ジェネレータの
// ファイル内ではなく本ファイルに置く（テンプレは person-detail.sbn / company-detail.sbn）。

/// <summary>役職別の関与グループ（行構造はシリーズ単位 + 話数圧縮）。</summary>
internal sealed class InvolvementGroup
{
    public string RoleCode { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    /// <summary>役職統計ページ（/creators/roles/{code}/）への URL。役職コードが空（カテゴリプレフィックス + 役職なし）のときは空文字。</summary>
    public string RoleUrl { get; set; } = "";
    /// <summary>シリーズ単位の集約行群。各行はそのシリーズ内での話数集合を圧縮表記で持つ。</summary>
    public IReadOnlyList<InvolvementSeriesRow> SeriesRows { get; set; } = Array.Empty<InvolvementSeriesRow>();
    /// <summary>TV 系シリーズ（series_kinds.credit_attach_to='EPISODE'）での担当エピソード合計数。</summary>
    public int EpisodeCount { get; set; }
    /// <summary>映画系シリーズ（series_kinds.credit_attach_to='SERIES'、MOVIE / MOVIE_SHORT / SPRING / EVENT）での担当本数（1 シリーズ = 1 本）。</summary>
    public int MovieCount { get; set; }
    /// <summary>担当の総量（<see cref="EpisodeCount"/> + <see cref="MovieCount"/>）。降順ソートのキーとして使う。</summary>
    public int Count => EpisodeCount + MovieCount;
    /// <summary>"担当 N 話・M 本" などの動詞つき単位表記。両方ゼロなら空文字。
    /// 声優役グループ（<see cref="HasCharacterColumn"/>）は「出演」、それ以外は「担当」を冠して、
    /// エピソードの話数（#N・第N話）と数量の「N 話」を読み分けられるようにする。</summary>
    public string CountLabel
    {
        get
        {
            return (EpisodeCount, MovieCount) switch
            {
                ( > 0, > 0) => $"{CountVerb} {EpisodeCount} 話・{MovieCount} 本",
                ( > 0, 0)   => $"{CountVerb} {EpisodeCount} 話",
                (0,   > 0) => $"{CountVerb} {MovieCount} 本",
                _           => ""
            };
        }
    }
    /// <summary>担当数バッジ（📺話・🎥本のピル）の前に冠する動詞。 声優役グループ（<see cref="HasCharacterColumn"/>）は「出演」、それ以外は「担当」。</summary>
    public string CountVerb => HasCharacterColumn ? "出演" : "担当";
    /// <summary>このグループ内に CharacterNames が設定された行が 1 件以上あるか（声優役判定）。</summary>
    public bool HasCharacterColumn { get; set; }

    /// <summary>声の出演で複数の役（キャラ）を演じている場合の「役」大くくりサブセクション。
    /// 空のときはテンプレ側が従来の <see cref="SeriesRows"/> 表示にフォールバックする。</summary>
    public IReadOnlyList<CharacterRoleSection> CharacterSections { get; set; } = Array.Empty<CharacterRoleSection>();
}

/// <summary>声の出演の「役（キャラクター）」大くくりサブセクション 1 件。</summary>
internal sealed class CharacterRoleSection
{
    /// <summary>役の表示名（最初にクレジットされた名義）。</summary>
    public string CharacterLabel { get; set; } = "";

    /// <summary>キャラクター詳細ページの URL。</summary>
    public string CharacterUrl { get; set; } = "";

    /// <summary>この役で出演したシリーズ行（放送開始日順、話数の圧縮表記つき）。</summary>
    public IReadOnlyList<InvolvementSeriesRow> SeriesRows { get; set; } = Array.Empty<InvolvementSeriesRow>();
}

/// <summary>シリーズ単位の関与 1 行。 行はシリーズ単位 + 話数圧縮で構成する（エピソードごと 1 行にはしない）。</summary>
internal sealed class InvolvementSeriesRow
{
    public string SeriesSlug { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。 クレジット履歴・声の出演履歴の各シリーズ行の表記で、シリーズ名直後に 薄色括弧で添える表現に使う（略称（series.title_short）は一切使わない）。</summary>
    public string SeriesStartYearLabel { get; set; } = "";
    /// <summary>話数圧縮表記。例：「#1〜4, 8」。全話担当なら空文字（テンプレ側で「(全話)」マークを別途出す）。 シリーズ全体スコープのときは「（シリーズ全体）」のような任意ラベルを入れる。</summary>
    public string RangeLabel { get; set; } = "";
    /// <summary>シリーズ内の全話を担当しているフラグ。テンプレで「(全話)」マークを出すかの判定に使う。</summary>
    public bool IsAllEpisodes { get; set; }
    /// <summary>声優関与のとき演じたキャラ名（シリーズ内連名、「、」連結）。それ以外は空。</summary>
    public string CharacterNames { get; set; } = "";
    /// <summary>当該シリーズで当該人物がクレジットされた所属屋号の表示ラベル。</summary>
    public string AffiliationsLabel { get; set; } = "";
}
