using PrecureDataStars.Data.Models;

namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// 声の出演の表で、同じ役名・同じ声優が続く行（ひと続き）の升の出し方を解決する。
/// ひと続きは <see cref="CreditBlockEntry.SameCharacterLayout"/> / <see cref="CreditBlockEntry.SamePersonLayout"/> の値だけで決める：
/// 行 i は、行 i と行 i-1 がともに CHARACTER_VOICE で、行 i の列に値があるとき行 i-1 の続き。
/// 出し方はひと続きの 2 行目の値（REPEAT / TOP / MIDDLE）で決まる。
/// SiteBuilder のクレジット描画と Catalog のプレビューで同じ解決を共有する。
/// </summary>
public static class VoiceCastRunLayout
{
    /// <summary>升を空のまま出す（TOP のひと続きの 2 行目以降）。</summary>
    public const int BlankCell = -1;

    /// <summary>升を出さない（MIDDLE のひと続きの 2 行目以降。先頭の行の升が縦に結合して受け持つ）。</summary>
    public const int OmittedCell = 0;

    /// <summary>ふつうに名前を書く（ひと続きに入らない行と、REPEAT のひと続きの全行）。</summary>
    public const int NormalCell = 1;

    /// <summary>
    /// 行ごとの升の出し方を返す。値は <see cref="NormalCell"/> / <see cref="BlankCell"/> / <see cref="OmittedCell"/>、
    /// または 2 以上（MIDDLE のひと続きの先頭の行で、升を縦にその行数だけ結合して名前を書く）。
    /// </summary>
    /// <param name="rows">ブロックの中で行になるエントリ（併記を畳んだ後の描く順）。</param>
    /// <param name="layoutOf">エントリから見る列（役名なら SameCharacterLayout、声優なら SamePersonLayout）。</param>
    public static int[] Resolve(IReadOnlyList<CreditBlockEntry> rows, Func<CreditBlockEntry, string?> layoutOf)
    {
        int n = rows.Count;
        var cells = new int[n];
        for (int i = 0; i < n; i++) cells[i] = NormalCell;

        for (int i = 0; i < n; )
        {
            if (rows[i].EntryKind != "CHARACTER_VOICE")
            {
                i++;
                continue;
            }
            // 行 i を先頭に、列に値のある CHARACTER_VOICE の行が続くところまでをひと続きとする。
            int j = i + 1;
            while (j < n && rows[j].EntryKind == "CHARACTER_VOICE" && layoutOf(rows[j]) is not null)
            {
                j++;
            }
            int span = j - i;
            if (span >= 2)
            {
                switch (layoutOf(rows[i + 1]))
                {
                    case CreditEntrySameLayouts.Middle:
                        cells[i] = span;
                        for (int k = i + 1; k < j; k++) cells[k] = OmittedCell;
                        break;
                    case CreditEntrySameLayouts.Top:
                        for (int k = i + 1; k < j; k++) cells[k] = BlankCell;
                        break;
                    // REPEAT（と知らない値）は毎行ふつうに書く。
                }
            }
            i = j;
        }
        return cells;
    }
}
