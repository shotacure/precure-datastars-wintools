namespace PrecureDataStars.OaVerifier.Ts.Arib;

/// <summary>
/// 字幕 1 件（画面に出てから消去されるまでの 1 画面ぶん）。時刻はメディア時刻（ms、ファイル先頭=0）。
/// </summary>
/// <param name="StartMs">表示開始のメディア時刻（ms）。最初の文字が出た時刻。</param>
/// <param name="EndMs">消去（画面消去 CS、または次の字幕による置き換え）のメディア時刻（ms）。</param>
/// <param name="Lines">画面上の行（上から順）。振り仮名（小型文字）は含まない。</param>
/// <param name="HasGaiji">文字に直せない外字（局独自の DRCS や対応の無い追加記号）を 〓 に置き換えた箇所を含むか。</param>
internal sealed record CaptionCue(long StartMs, long EndMs, IReadOnlyList<CaptionLine> Lines, bool HasGaiji)
{
    /// <summary>行を「 / 」で連結した本文（色の印なし）。</summary>
    public string PlainText => string.Join(" / ", Lines.Select(l => l.PlainText));
}

/// <summary>字幕の 1 行。色の変わり目ごとに区切った断片の並び。</summary>
internal sealed record CaptionLine(IReadOnlyList<CaptionSpan> Spans)
{
    /// <summary>行の本文（色の印なし）。</summary>
    public string PlainText => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>同じ文字色が続く断片。</summary>
/// <param name="ColorIndex">文字色（ARIB の 0 番パレット。0 黒 / 1 赤 / 2 緑 / 3 黄 / 4 青 / 5 マゼンタ / 6 シアン / 7 白）。</param>
/// <param name="Text">本文。</param>
internal sealed record CaptionSpan(int ColorIndex, string Text)
{
    /// <summary>既定の文字色（白）。字幕の色分けは話者の区別に使われるので、白以外だけ印を付ける。</summary>
    public const int DefaultColor = 7;

    /// <summary>色の日本語名（印に使う）。8 以上（中間輝度・透明）は 0〜7 の同系色として扱う。</summary>
    public static string ColorName(int index) => (index & 7) switch
    {
        0 => "黒",
        1 => "赤",
        2 => "緑",
        3 => "黄",
        4 => "青",
        5 => "紫",
        6 => "水",
        _ => "白"
    };
}
