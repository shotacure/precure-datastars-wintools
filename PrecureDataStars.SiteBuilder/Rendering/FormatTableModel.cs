namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>エピソード詳細のフォーマット表テンプレに渡すモデル。</summary>
public sealed class FormatTableModel
{
    /// <summary>表に表示する行の並び（episode_seq 昇順）。</summary>
    public IReadOnlyList<FormatTableRow> Rows { get; set; } = Array.Empty<FormatTableRow>();

    /// <summary>OA（本放送）の総尺秒。表のフッタ用。</summary>
    public string OaTotal { get; set; } = "";
    /// <summary>配信の総尺秒（vod_intro を含む）。</summary>
    public string VodTotal { get; set; } = "";
    /// <summary>円盤の総尺秒。</summary>
    public string DiscTotal { get; set; } = "";

    /// <summary>本放送フォーマットが調査中かどうか。いずれかのパートの備考（episode_parts.notes）に
    /// 「【本放送未確認】」が含まれるとき true になり、テンプレ側でフォーマットセクション直下に
    /// 「（本放送フォーマットは現在調査中です）」の赤字注記を出す。</summary>
    public bool OaUnderInvestigation { get; set; }

    /// <summary>帯グラフ表現のメディア別バー（本放送 / 配信 / BD・DVD の順。データの無いメディアは含まれない）。</summary>
    public IReadOnlyList<FormatBar> Bars { get; set; } = Array.Empty<FormatBar>();

    /// <summary>帯グラフの色凡例（バー内での初出順）。</summary>
    public IReadOnlyList<FormatLegendItem> Legend { get; set; } = Array.Empty<FormatLegendItem>();
}

/// <summary>1 パート分の表示用 DTO。</summary>
public sealed class FormatTableRow
{
    /// <summary>パート種別の日本語名（例: "Aパート"）。</summary>
    public string PartName { get; set; } = "";

    /// <summary>OA 開始時刻（HH:mm:ss）。NULL のときは空。</summary>
    public string OaStart { get; set; } = "";
    /// <summary>OA 終了時刻（HH:mm:ss）。</summary>
    public string OaEnd { get; set; } = "";
    /// <summary>OA 尺（mm:ss）。</summary>
    public string OaDuration { get; set; } = "";

    /// <summary>配信版の累積開始秒（mm:ss）。<c>series.vod_intro</c> を起点とする。</summary>
    public string VodStart { get; set; } = "";
    /// <summary>配信版の尺（mm:ss）。</summary>
    public string VodDuration { get; set; } = "";

    /// <summary>円盤版の尺（mm:ss）。時刻列は出さない。</summary>
    public string DiscDuration { get; set; } = "";

    /// <summary>備考（episode_parts.notes）。</summary>
    public string Notes { get; set; } = "";
}

/// <summary>帯グラフの 1 メディア分のバー。</summary>
public sealed class FormatBar
{
    /// <summary>メディアの短縮ラベル（"本放送" / "配信" / "円盤"）。</summary>
    public string MediumLabel { get; set; } = "";

    /// <summary>このメディアの総尺ラベル（mm:ss）。バー右端に添える。</summary>
    public string TotalLabel { get; set; } = "";

    /// <summary>バー幅のパーセント値（最長メディアを 100 とする同一スケール）。style 属性用の不変文化圏文字列。</summary>
    public string WidthPercent { get; set; } = "100";

    /// <summary>バーを構成するセグメント列（パート順）。</summary>
    public IReadOnlyList<FormatBarSegment> Segments { get; set; } = Array.Empty<FormatBarSegment>();
}

/// <summary>帯グラフバー内の 1 セグメント（1 パート）。</summary>
public sealed class FormatBarSegment
{
    /// <summary>色パレットの CSS クラス名（fmt-p-*）。</summary>
    public string PaletteCss { get; set; } = "";

    /// <summary>セグメント幅の比重（＝パート尺秒。テンプレ側で flex-grow に渡す）。</summary>
    public int Seconds { get; set; }

    /// <summary>セグメント内に出す短縮ラベル（"OP" / "Aパート" など。出さないときも値自体は持つ）。</summary>
    public string ShortLabel { get; set; } = "";

    /// <summary>ラベルがセグメント幅に収まる見込みのときだけ true（収まらない幅では色と title 属性に委ねる）。</summary>
    public bool ShowLabel { get; set; }

    /// <summary>幅が狭めのセグメント（スマホ幅ではラベルを隠す対象）。</summary>
    public bool SmallLabel { get; set; }

    /// <summary>title 属性用のフルラベル（"アバンタイトル 1:23" 形式）。</summary>
    public string Title { get; set; } = "";
}

/// <summary>帯グラフの色凡例 1 項目。</summary>
public sealed class FormatLegendItem
{
    /// <summary>色パレットの CSS クラス名（fmt-p-*）。</summary>
    public string PaletteCss { get; set; } = "";

    /// <summary>凡例ラベル（"Aパート" / "提供クレジット" など）。</summary>
    public string Label { get; set; } = "";
}
