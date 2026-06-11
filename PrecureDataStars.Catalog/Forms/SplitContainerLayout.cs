namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// SplitContainer の分割位置をフォーム初回表示後に設計値へ再適用するための共通ヘルパ。
/// <para>
/// WinForms の既知ピットフォール（CLAUDE.md 記載）として、Designer の InitializeComponent 内で設定した
/// <see cref="SplitContainer.SplitterDistance"/> はフォーム既定サイズ（300×300 相当）に対して評価されるため、
/// その後 ClientSize が設計サイズへ拡張されても意図した分割位置にならない。
/// 対策として、実描画後（<see cref="Form.Shown"/>）に本ヘルパで設計値を再適用する。
/// </para>
/// </summary>
internal static class SplitContainerLayout
{
    /// <summary>
    /// SplitterDistance を設計値へ再適用する。代入可能範囲
    /// （<see cref="SplitContainer.Panel1MinSize"/> 〜 全長 − <see cref="SplitContainer.Panel2MinSize"/> −
    /// スプリッタ幅）にクランプし、領域が極端に小さい場合（最小化途中など）は何もしない。
    /// </summary>
    /// <param name="split">対象の SplitContainer。</param>
    /// <param name="designDistance">Designer で意図していた分割位置（px）。</param>
    public static void Apply(SplitContainer split, int designDistance)
    {
        // 分割方向に応じた全長。Horizontal は上下分割（高さ基準）、Vertical は左右分割（幅基準）。
        int total = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
        int max = total - split.Panel2MinSize - split.SplitterWidth;
        if (max <= split.Panel1MinSize) return;

        split.SplitterDistance = Math.Clamp(designDistance, split.Panel1MinSize, max);
    }
}
