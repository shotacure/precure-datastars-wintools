namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// エピソード詳細のフォーマット表テンプレに渡すモデル。
/// <para>
/// 本放送列は放送開始時刻（<c>episode.on_air_at</c> の時刻部分）基準で「開始時刻〜終了時刻」を表示する。<br/>
/// 配信列は <c>series.vod_intro</c>（VOD 用先頭イントロ秒数）を起点とした累積開始秒数を表示する。<br/>
/// 円盤列は累積時刻を持たず、尺のみを表示する。
/// </para>
/// </summary>
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