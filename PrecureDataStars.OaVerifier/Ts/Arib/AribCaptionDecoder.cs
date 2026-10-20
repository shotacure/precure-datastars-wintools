using System.Text;

namespace PrecureDataStars.OaVerifier.Ts.Arib;

/// <summary>
/// ARIB STD-B24 字幕（フルセグ・日本語・8 単位符号）の PES ペイロードを解読し、画面単位の字幕
/// （<see cref="CaptionCue"/>）に組み立てる。制御コード・文字集合の扱いは ARIB TR-B14 の運用と
/// libaribcaption の実装に合わせている。
///
/// <para>画面のモデル：文字は表示位置（ドット座標）の行ごとに積み、画面消去（CS）で 1 件の字幕として確定する。
/// 時刻は PES の表示時刻（PTS をメディア時刻に直したもの）に、文中の時間待ち（TIME）を足して進める。
/// 次の字幕文がそのまま書き足す場合は同じ画面として扱う（消去が来るまでが 1 件）。</para>
///
/// <para>本文に残さないもの：振り仮名（小型 SSZ、または SSM で 18×18 ドットの文字）、モザイク、
/// 図形の装飾。局独自の外字（DRCS）と Unicode に無い追加記号は 〓 に置き換え、<see cref="CaptionCue.HasGaiji"/> を立てる。</para>
///
/// <para>スレッド方針：1 インスタンスを 1 スレッドから順に使う（PES は放送順に与える）。</para>
/// </summary>
internal sealed class AribCaptionDecoder
{
    private const int Geta = 0x3013;            // 〓（文字に直せない外字の置き換え）
    private const int IdeographicSpace = 0x3000;
    private const int LanguageId = 1;           // 第 1 言語（日本語）の字幕文だけを読む
    private const int MaxMacroDepth = 4;        // マクロ展開の入れ子上限（壊れたデータでの無限再帰よけ）

    /// <summary>文字集合（G0〜G3 に指示されるもの）。</summary>
    private enum CharSet : byte
    {
        Kanji, Alphanumeric, Hiragana, Katakana, Mosaic,
        ProportionalAlphanumeric, ProportionalHiragana, ProportionalKatakana,
        JisX0201Katakana, JisX0213Plane1, JisX0213Plane2, AdditionalSymbols,
        Drcs, Macro, Unsupported
    }

    /// <summary>G0〜G3 の指示内容（集合とバイト数）。</summary>
    private readonly record struct Designation(CharSet Set, int Bytes);

    private static readonly Designation KanjiSet = new(CharSet.Kanji, 2);
    private static readonly Designation AlnumSet = new(CharSet.Alphanumeric, 1);
    private static readonly Designation HiraganaSet = new(CharSet.Hiragana, 1);
    private static readonly Designation MacroSet = new(CharSet.Macro, 1);

    /// <summary>既定マクロ（0x60〜0x6F）。G3 に指示されたマクロ集合の文字で呼ばれ、符号の指示をまとめて切り替える。</summary>
    private static readonly byte[][] DefaultMacros =
    {
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x4A, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x31, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x20, 0x41, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x32, 0x1B, 0x29, 0x34, 0x1B, 0x2A, 0x35, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x32, 0x1B, 0x29, 0x33, 0x1B, 0x2A, 0x35, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x32, 0x1B, 0x29, 0x20, 0x41, 0x1B, 0x2A, 0x35, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x20, 0x41, 0x1B, 0x29, 0x20, 0x42, 0x1B, 0x2A, 0x20, 0x43, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x20, 0x44, 0x1B, 0x29, 0x20, 0x45, 0x1B, 0x2A, 0x20, 0x46, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x20, 0x47, 0x1B, 0x29, 0x20, 0x48, 0x1B, 0x2A, 0x20, 0x49, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x20, 0x4A, 0x1B, 0x29, 0x20, 0x4B, 0x1B, 0x2A, 0x20, 0x4C, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x20, 0x4D, 0x1B, 0x29, 0x20, 0x4E, 0x1B, 0x2A, 0x20, 0x4F, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x20, 0x42, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x20, 0x43, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x24, 0x42, 0x1B, 0x29, 0x20, 0x44, 0x1B, 0x2A, 0x30, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x31, 0x1B, 0x29, 0x30, 0x1B, 0x2A, 0x4A, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
        new byte[] { 0x1B, 0x28, 0x4A, 0x1B, 0x29, 0x32, 0x1B, 0x2A, 0x20, 0x41, 0x1B, 0x2B, 0x20, 0x70, 0x0F, 0x1B, 0x7D },
    };

    // ── 符号の状態 ──
    private readonly Designation[] _g = new Designation[4];
    private int _gl;                 // GL に呼び出している G 番号
    private int _gr;                 // GR に呼び出している G 番号
    private int _macroDepth;

    // ── 書式の状態（表示位置はドット座標）──
    private int _writingFormat = 7;  // SWF（字幕管理の Format−1 か CSI SWF で決まる。7 = 960×540 横書き）
    private int _charW, _charH, _hSpacing, _vSpacing;
    private int _areaX, _areaY, _areaW, _areaH;
    private float _hScale = 1f, _vScale = 1f;
    private int _posX, _posY;
    private bool _posInited;
    private int _color = CaptionSpan.DefaultColor;
    private int _repeat = -1;        // RPC による次の文字の繰り返し回数（-1 = 指定なし）

    // ── 画面（行は表示位置の y 座標ごと）──
    private readonly SortedDictionary<int, LineBuffer> _rows = new();
    private long _screenStartMs = -1;
    private bool _screenGaiji;
    private long _nowMs;
    private int _prevManagementGroup = -1;

    private readonly List<CaptionCue> _cues = new();

    /// <summary>確定した字幕（表示開始順）。</summary>
    public IReadOnlyList<CaptionCue> Cues => _cues;

    /// <summary>読んだ字幕文（第 1 言語の字幕文データグループ）の数。診断用。</summary>
    public int StatementCount { get; private set; }

    /// <summary>構造が壊れていて読み飛ばした PES の数。診断用。</summary>
    public int BrokenCount { get; private set; }

    public AribCaptionDecoder() => ResetInternalState();

    /// <summary>字幕 PES のペイロード（PES ヘッダの直後＝data_identifier から）を 1 つ解読する。
    /// <paramref name="mediaMs"/> はこの PES の表示時刻（メディア時刻 ms）。</summary>
    public void DecodePes(ReadOnlySpan<byte> data, long mediaMs)
    {
        if (data.Length < 3) { BrokenCount++; return; }
        byte dataIdentifier = data[0];
        byte privateStreamId = data[1];
        int headerLen = data[2] & 0x0F;
        if (dataIdentifier != 0x80 || privateStreamId != 0xFF) { BrokenCount++; return; } // 0x80 = 字幕（0x81 は文字スーパー）

        int dg = 3 + headerLen;
        if (dg + 5 > data.Length) { BrokenCount++; return; }
        int groupId = data[dg] >> 2;
        int groupSize = (data[dg + 3] << 8) | data[dg + 4];
        if (dg + 5 + groupSize > data.Length) { BrokenCount++; return; }
        if (groupSize == 0) return;

        var body = data.Slice(dg + 5, groupSize);
        int id = groupId & 0x0F;
        int group = (groupId >> 5) & 1;
        if (id == 0)
        {
            // 字幕管理データ。組（A/B）が前回と同じなら再送なので読まない（ARIB TR-B14 4.2.4）。
            if (group == _prevManagementGroup) return;
            if (ParseManagement(body)) _prevManagementGroup = group;
            else BrokenCount++;
        }
        else if (id == LanguageId)
        {
            _nowMs = mediaMs;
            StatementCount++;
            if (!ParseStatement(body)) BrokenCount++;
        }
    }

    /// <summary>読み終わりに、表示中の字幕を <paramref name="endMs"/> で確定する。</summary>
    public void Finish(long endMs)
    {
        _nowMs = Math.Max(_nowMs, endMs);
        FlushScreen();
    }

    // ── データグループ ──

    private bool ParseManagement(ReadOnlySpan<byte> d)
    {
        if (d.Length < 10) return false;
        int tmd = d[0] >> 6;
        int off = 1;
        if (tmd == 0b10) off += 5;                       // OTM
        int numLanguages = d[off++];
        if (numLanguages is 0 or > 2) return false;
        for (int i = 0; i < numLanguages; i++)
        {
            if (off + 6 > d.Length) return false;
            int languageTag = d[off] >> 5;
            int dmf = d[off] & 0x0F;
            off++;
            if (dmf is 0b1100 or 0b1101 or 0b1110) off++; // DC
            off += 3;                                      // ISO_639_language_code
            int format = d[off] >> 4;
            off++;
            if (languageTag + 1 == LanguageId)
            {
                _writingFormat = format - 1;
                ResetGraphicSets();
                ResetWritingFormat();
            }
        }
        if (off + 3 > d.Length) return false;
        int loopLen = (d[off] << 16) | (d[off + 1] << 8) | d[off + 2];
        off += 3;
        if (loopLen == 0) return true;
        if (off + loopLen > d.Length) return false;
        return ParseDataUnits(d.Slice(off, loopLen));
    }

    private bool ParseStatement(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4) return false;
        int tmd = d[0] >> 6;
        int off = 1;
        if (tmd is 0b01 or 0b10) off += 5;               // STM
        if (off + 4 > d.Length) return false;
        int loopLen = (d[off] << 16) | (d[off + 1] << 8) | d[off + 2];
        off += 3;
        if (loopLen == 0) return true;
        if (off + loopLen > d.Length) return false;
        return ParseDataUnits(d.Slice(off, loopLen));
    }

    /// <summary>データユニットの並びを読む。本文（0x20）だけを解読し、DRCS の字形定義（0x30/0x31）などは読み飛ばす。</summary>
    private bool ParseDataUnits(ReadOnlySpan<byte> d)
    {
        int off = 0;
        while (off < d.Length)
        {
            if (off + 5 > d.Length) return false;
            if (d[off] != 0x1F) return false;            // unit_separator
            int parameter = d[off + 1];
            int size = (d[off + 2] << 16) | (d[off + 3] << 8) | d[off + 4];
            if (size == 0) return true;
            if (off + 5 + size > d.Length) return false;
            if (parameter == 0x20) ParseStatementBody(d.Slice(off + 5, size));
            off += 5 + size;
        }
        return true;
    }

    // ── 本文（8 単位符号）──

    /// <summary>本文を 1 バイトずつ解読する。解読できない符号に当たったら、その本文の残りは捨てる。</summary>
    private void ParseStatementBody(ReadOnlySpan<byte> d)
    {
        int off = 0;
        while (off < d.Length)
        {
            byte ch = d[off];
            int used;
            if (ch <= 0x20) used = HandleC0(d[off..]);
            else if (ch < 0x7F) used = HandleChar(d[off..], _g[_gl]);
            else if (ch <= 0xA0) used = HandleC1(d[off..]);
            else if (ch < 0xFF) used = HandleChar(d[off..], _g[_gr]);
            else used = 1;
            if (used <= 0) return;
            off += used;
        }
    }

    /// <summary>C0 制御符号。戻り値は消費したバイト数（0 = 解読不能）。</summary>
    private int HandleC0(ReadOnlySpan<byte> d)
    {
        switch (d[0])
        {
            case 0x08: MoveRelative(-1, 0); return 1;                   // APB
            case 0x09: MoveRelative(1, 0); return 1;                    // APF
            case 0x0A: MoveRelative(0, 1); return 1;                    // APD
            case 0x0B: MoveRelative(0, -1); return 1;                   // APU
            case 0x0C:                                                  // CS（画面消去）
                FlushScreen();
                ResetInternalState();
                return 1;
            case 0x0D: MoveToNewline(); return 1;                       // APR
            case 0x0E: _gl = 1; return 1;                               // LS1
            case 0x0F: _gl = 0; return 1;                               // LS0
            case 0x16:                                                  // PAPF
                if (d.Length < 2) return 0;
                MoveRelative(d[1] & 0x3F, 0);
                return 2;
            case 0x19:                                                  // SS2
                if (d.Length < 2) return 0;
                { int n = HandleChar(d[1..], _g[2]); return n <= 0 ? 0 : 1 + n; }
            case 0x1D:                                                  // SS3
                if (d.Length < 2) return 0;
                { int n = HandleChar(d[1..], _g[3]); return n <= 0 ? 0 : 1 + n; }
            case 0x1B:                                                  // ESC
                if (d.Length < 2) return 0;
                { int n = HandleEsc(d[1..]); return n <= 0 ? 0 : 1 + n; }
            case 0x1C:                                                  // APS
                if (d.Length < 3) return 0;
                SetAbsolutePos(d[2] & 0x3F, d[1] & 0x3F);
                return 3;
            case 0x20:                                                  // SP
                PushChar(IsMiddleSize ? ' ' : IdeographicSpace, false);
                return 1;
            default:                                                    // NUL / BEL / CAN / RS / US など
                return 1;
        }
    }

    /// <summary>ESC に続く符号の指示・呼び出し。戻り値は ESC の後ろで消費したバイト数。</summary>
    private int HandleEsc(ReadOnlySpan<byte> d)
    {
        switch (d[0])
        {
            case 0x6E: _gl = 2; return 1;   // LS2
            case 0x6F: _gl = 3; return 1;   // LS3
            case 0x7E: _gr = 1; return 1;   // LS1R
            case 0x7D: _gr = 2; return 1;   // LS2R
            case 0x7C: _gr = 3; return 1;   // LS3R
        }

        if (d[0] == 0x24)                    // 2 バイト集合（または 2 バイト DRCS）
        {
            if (d.Length < 2) return 0;
            if (d[1] is >= 0x28 and <= 0x2B)
            {
                if (d.Length < 3) return 0;
                int gi = d[1] - 0x28;
                if (d[2] == 0x20)
                {
                    if (d.Length < 4) return 0;
                    if (TryDrcsByF(d[3], out var drcs)) _g[gi] = drcs;
                    return 4;
                }
                if (TryGSetByF(d[2], out var set)) _g[gi] = set;
                return 3;
            }
            if (TryGSetByF(d[1], out var g0)) _g[0] = g0;
            return 2;
        }

        if (d[0] is >= 0x28 and <= 0x2B)     // 1 バイト集合（または 1 バイト DRCS・マクロ）
        {
            if (d.Length < 2) return 0;
            int gi = d[0] - 0x28;
            if (d[1] == 0x20)
            {
                if (d.Length < 3) return 0;
                if (TryDrcsByF(d[2], out var drcs)) _g[gi] = drcs;
                return 3;
            }
            if (TryGSetByF(d[1], out var set)) _g[gi] = set;
            return 2;
        }
        return 1;
    }

    /// <summary>C1 制御符号。戻り値は消費したバイト数（0 = 解読不能）。</summary>
    private int HandleC1(ReadOnlySpan<byte> d)
    {
        byte c = d[0];
        if (c is >= 0x80 and <= 0x87) { _color = c - 0x80; return 1; } // BKF〜WHF（文字色）
        switch (c)
        {
            case 0x7F: return 1;                                         // DEL
            case 0x88: _hScale = 0.5f; _vScale = 0.5f; return 1;          // SSZ（小型）
            case 0x89: _hScale = 0.5f; _vScale = 1f; return 1;            // MSZ（中型）
            case 0x8A: _hScale = 1f; _vScale = 1f; return 1;              // NSZ（標準）
            case 0x8B:                                                    // SZX
                if (d.Length < 2) return 0;
                if (d[1] == 0x41) _vScale = 2f;
                else if (d[1] == 0x44) _hScale = 2f;
                else if (d[1] == 0x45) { _hScale = 2f; _vScale = 2f; }
                return 2;
            case 0x90:                                                    // COL
                if (d.Length < 2) return 0;
                if (d[1] == 0x20) return d.Length < 3 ? 0 : 3;            // パレット指定
                if (d[1] is >= 0x48 and <= 0x7F)
                {
                    if ((d[1] & 0xF0) == 0x40) _color = d[1] & 0x0F;      // 前景色
                    return 2;
                }
                return 0;
            case 0x91: case 0x93: case 0x94: case 0x97:                   // FLC / POL / WMM / HLC
                return d.Length < 2 ? 0 : 2;
            case 0x92:                                                    // CDC
                if (d.Length < 2) return 0;
                return d[1] == 0x20 ? (d.Length < 3 ? 0 : 3) : 2;
            case 0x98:                                                    // RPC（次の文字を繰り返す。0 = 行末まで）
                if (d.Length < 2) return 0;
                _repeat = d[1] & 0x3F;
                return 2;
            case 0x99: case 0x9A: return 1;                               // SPL / STL（下線）
            case 0x9B: { int n = HandleCsi(d[1..]); return n <= 0 ? 0 : 1 + n; }
            case 0x9D:                                                    // TIME
                if (d.Length < 3) return 0;
                if (d[1] == 0x20) { _nowMs += (d[2] & 0x3F) * 100L; return 3; } // 時間待ち（0.1 秒単位）
                if (d[1] == 0x28) return 3;
                return 0;
            case 0x95:                                                    // MACRO（字幕では使われない）
                return 0;
            default:
                return 1;
        }
    }

    /// <summary>CSI（拡張制御）。パラメータを読み、書式に関わるもの（SWF / SDF / SDP / SSM / SHS / SVS / ACPS）だけ反映する。
    /// 戻り値は CSI の後ろで消費したバイト数（0 = 解読不能）。</summary>
    private int HandleCsi(ReadOnlySpan<byte> d)
    {
        int off = 0, p1 = 0, p2 = 0, count = 0;
        while (off < d.Length)
        {
            byte b = d[off];
            if (b is >= 0x30 and <= 0x39) { if (count <= 1) p2 = p2 * 10 + (b & 0x0F); }
            else if (b == 0x20) { if (count == 0) p1 = p2; count++; break; }
            else if (b == 0x3B) { if (count == 0) { p1 = p2; p2 = 0; } count++; }
            off++;
        }
        if (++off >= d.Length) return 0;                                 // 終端文字 F へ
        switch (d[off])
        {
            case 0x53: if (count == 1) _writingFormat = p1; ResetWritingFormat(); break; // SWF
            case 0x56: _areaW = p1; _areaH = p2; break;                                  // SDF
            case 0x5F: _areaX = p1; if (count >= 2) _areaY = p2;                          // SDP
                       if (!_posInited) SetAbsolutePos(0, 0); break;
            case 0x57: _charW = p1; _charH = p2; break;                                   // SSM
            case 0x58: _hSpacing = p1; break;                                            // SHS
            case 0x59: _vSpacing = p1; break;                                            // SVS
            case 0x61: _posX = p1; _posY = p2; _posInited = true; break;                 // ACPS
        }
        return off + 1;
    }

    /// <summary>GL/GR の図形文字 1 字を、指示されている集合 <paramref name="g"/> で読む。戻り値は消費バイト数。</summary>
    private int HandleChar(ReadOnlySpan<byte> d, Designation g)
    {
        int c1 = d[0] & 0x7F;
        if (c1 is < 0x21 or >= 0x7F) return 0;
        int c2 = 0;
        if (g.Bytes == 2)
        {
            if (d.Length < 2) return 0;
            c2 = d[1] & 0x7F;
            if (c2 is < 0x21 or >= 0x7F) return 0;
        }

        switch (g.Set)
        {
            case CharSet.Kanji:
            case CharSet.JisX0213Plane1:
            case CharSet.JisX0213Plane2:
            case CharSet.AdditionalSymbols:
                PushKanji(c1 - 0x21, c2 - 0x21);
                break;
            // 仮名と和文の記号（ー。「」、・）は中型でも全角のまま（半角カナ混じりは台詞として読みにくいため）。
            case CharSet.Hiragana:
            case CharSet.ProportionalHiragana:
                PushChar(AribCharTables.Hiragana[c1 - 0x21], false);
                break;
            case CharSet.Katakana:
            case CharSet.ProportionalKatakana:
                PushChar(AribCharTables.Katakana[c1 - 0x21], false);
                break;
            case CharSet.JisX0201Katakana:
                PushChar(AribCharTables.JisX0201Katakana[c1 - 0x21], false);
                break;
            case CharSet.Alphanumeric:
            case CharSet.ProportionalAlphanumeric:
                PushChar(IsMiddleSize ? AribCharTables.AlphanumericHalfwidth[c1 - 0x21] : AribCharTables.AlphanumericFullwidth[c1 - 0x21], false);
                break;
            case CharSet.Drcs:
                PushChar(Geta, true);                                     // 局独自の外字は文字に直せない
                break;
            case CharSet.Macro:
                if (c1 is >= 0x60 and <= 0x6F && _macroDepth < MaxMacroDepth)
                {
                    _macroDepth++;
                    try { ParseStatementBody(DefaultMacros[c1 & 0x0F]); }
                    finally { _macroDepth--; }
                }
                break;
            default:                                                      // モザイク等は本文に残さない
                MoveRelative(1, 0);
                break;
        }
        return g.Bytes;
    }

    /// <summary>漢字系集合の 1 字（区・点は 0 起点）。1〜84 区は JIS の表、85 区以降は追加漢字・追加記号の表で引く。</summary>
    private void PushKanji(int ku, int ten)
    {
        const int GaijiBeginKu = 84;
        if (ku < GaijiBeginKu)
        {
            int index = ku * 94 + ten;
            int cp = AribCharTables.Kanji[index];
            if (IsMiddleSize)
            {
                // 中型では ASCII に当たる記号（！？（）など）と全角英数・空白を半角で表記する（画面上の見た目に合わせる）。
                // 和文の句読点・括弧（。、「」など）は全角のまま残す。
                if (ku < 2 && AribCharTables.KanjiSymbolsHalfwidth[index] < 0x80) cp = AribCharTables.KanjiSymbolsHalfwidth[index];
                if (cp == IdeographicSpace || cp is >= 0xFF01 and <= 0xFF5E) cp = (cp & 0xFF) + 0x20;
            }
            if (cp is 0 or 0xFFFD) PushChar(Geta, true);
            else PushChar(cp, false);
            return;
        }

        int gi = (ku - GaijiBeginKu) * 94 + ten;
        int g = gi < AribCharTables.AdditionalSymbols.Length ? AribCharTables.AdditionalSymbols[gi] : 0xFFFD;
        // Unicode に無い追加記号（0xFFFD・私用領域）は 〓 にする。
        if (g == 0xFFFD || g is >= 0xE000 and <= 0xF8FF) PushChar(Geta, true);
        else PushChar(g, false);
    }

    // ── 画面への積み込み ──

    /// <summary>1 字を現在位置の行に積んで位置を進める。振り仮名（小型）は位置だけ進めて本文に残さない。
    /// RPC が指定されていれば繰り返す（0 = 行末まで）。</summary>
    private void PushChar(int codePoint, bool gaiji)
    {
        int times = 1;
        if (_repeat >= 0)
        {
            times = _repeat == 0 ? Math.Max(1, (_areaX + _areaW - _posX) / Math.Max(1, SectionWidth)) : _repeat;
            _repeat = -1;
        }
        for (int i = 0; i < times; i++)
        {
            if (!_posInited) SetAbsolutePos(0, 0);
            if (!IsRuby)
            {
                if (_screenStartMs < 0) _screenStartMs = _nowMs;
                if (!_rows.TryGetValue(_posY, out var line)) { line = new LineBuffer(); _rows[_posY] = line; }
                // 同じ行で前の文字から 1 字ぶん以上空いていれば空白を 1 つ挟む（APS/APF による字間の空き）。
                if (line.HasText && _posX > line.NextX + SectionWidth / 2)
                    line.Append(_color, IsMiddleSize ? ' ' : IdeographicSpace);
                line.Append(_color, codePoint);
                line.NextX = _posX + SectionWidth;
                if (gaiji) _screenGaiji = true;
            }
            MoveRelative(1, 0);
        }
    }

    /// <summary>表示中の画面を 1 件の字幕として確定し、画面を空にする。</summary>
    private void FlushScreen()
    {
        var lines = _rows.Values.Where(l => l.HasText).Select(l => l.Build()).Where(l => l.PlainText.Trim().Length > 0).ToList();
        if (lines.Count > 0 && _screenStartMs >= 0)
            _cues.Add(new CaptionCue(_screenStartMs, Math.Max(_screenStartMs, _nowMs), lines, _screenGaiji));
        _rows.Clear();
        _screenStartMs = -1;
        _screenGaiji = false;
    }

    // ── 状態の初期化 ──

    /// <summary>画面消去（CS）時の初期化。符号の指示・書式・位置・色を既定に戻す。</summary>
    private void ResetInternalState()
    {
        ResetGraphicSets();
        ResetWritingFormat();
        _areaX = 0; _areaY = 0;
        _posInited = false;
        _posX = 0; _posY = 0;
        _hScale = 1f; _vScale = 1f;
        _color = CaptionSpan.DefaultColor;
        _repeat = -1;
    }

    /// <summary>フルセグ（プロファイル A）の既定：G0 漢字 / G1 英数 / G2 平仮名 / G3 マクロ、GL=G0・GR=G2。</summary>
    private void ResetGraphicSets()
    {
        _g[0] = KanjiSet;
        _g[1] = AlnumSet;
        _g[2] = HiraganaSet;
        _g[3] = MacroSet;
        _gl = 0;
        _gr = 2;
    }

    /// <summary>書式（SWF）ごとの表示領域・文字の大きさ・字間・行間を既定に戻す。</summary>
    private void ResetWritingFormat()
    {
        (_areaW, _areaH, _hSpacing, _vSpacing) = _writingFormat switch
        {
            5 => (1920, 1080, 4, 24),   // 1920×1080 横書き
            8 => (960, 540, 12, 24),    // 960×540 縦書き
            9 => (720, 480, 4, 16),     // 720×480 横書き
            10 => (720, 480, 8, 24),    // 720×480 縦書き
            _ => (960, 540, 4, 24)      // 7: 960×540 横書き（既定）
        };
        _charW = 36;
        _charH = 36;
    }

    // ── 表示位置（ドット座標）──

    private bool IsMiddleSize => _hScale * 2 == _vScale;
    private bool IsRuby => (_hScale == 0.5f && _vScale == 0.5f) || (_charW == 18 && _charH == 18);
    private int SectionWidth => (int)Math.Floor((_charW + _hSpacing) * _hScale);
    private int SectionHeight => (int)Math.Floor((_charH + _vSpacing) * _vScale);

    private void SetAbsolutePos(int x, int y)
    {
        _posInited = true;
        _posX = _areaX + x * SectionWidth;
        _posY = _areaY + (y + 1) * SectionHeight;
    }

    private void MoveRelative(int x, int y)
    {
        if (!_posInited) SetAbsolutePos(0, 0);
        int sw = Math.Max(1, SectionWidth), sh = Math.Max(1, SectionHeight);
        for (; x < 0; x++)
        {
            _posX -= sw;
            if (_posX < _areaX) { _posX = _areaX + _areaW - sw; y--; }
        }
        for (; x > 0; x--)
        {
            _posX += sw;
            if (_posX >= _areaX + _areaW) { _posX = _areaX; y++; }
        }
        for (; y < 0; y++)
        {
            _posY -= sh;
            if (_posY < _areaY) _posY = _areaY + _areaH;
        }
        for (; y > 0; y--)
        {
            _posY += sh;
            if (_posY > _areaY + _areaH) _posY = _areaY + sh;
        }
    }

    private void MoveToNewline()
    {
        if (!_posInited) SetAbsolutePos(0, 0);
        _posX = _areaX;
        _posY += SectionHeight;
    }

    // ── 符号の指示の終端文字 F ──

    private static bool TryGSetByF(byte f, out Designation d)
    {
        Designation? found = f switch
        {
            0x42 => KanjiSet,
            0x4A => AlnumSet,
            0x30 => HiraganaSet,
            0x31 => new Designation(CharSet.Katakana, 1),
            0x32 or 0x33 or 0x34 or 0x35 => new Designation(CharSet.Mosaic, 1),
            0x36 => new Designation(CharSet.ProportionalAlphanumeric, 1),
            0x37 => new Designation(CharSet.ProportionalHiragana, 1),
            0x38 => new Designation(CharSet.ProportionalKatakana, 1),
            0x49 => new Designation(CharSet.JisX0201Katakana, 1),
            0x39 => new Designation(CharSet.JisX0213Plane1, 2),
            0x3A => new Designation(CharSet.JisX0213Plane2, 2),
            0x3B => new Designation(CharSet.AdditionalSymbols, 2),
            0x4B or 0x4C => new Designation(CharSet.Unsupported, 1),   // ラテン拡張（日本の字幕では使わない）
            _ => null
        };
        d = found.GetValueOrDefault();
        return found.HasValue;
    }

    private static bool TryDrcsByF(byte f, out Designation d)
    {
        if (f == 0x40) { d = new(CharSet.Drcs, 2); return true; }    // DRCS-0（2 バイト）
        if (f is >= 0x41 and <= 0x4F) { d = new(CharSet.Drcs, 1); return true; } // DRCS-1〜15
        if (f == 0x70) { d = MacroSet; return true; }                 // マクロ
        d = default;
        return false;
    }

    /// <summary>画面上の 1 行ぶんの文字（色の変わり目で断片に分ける）。</summary>
    private sealed class LineBuffer
    {
        private readonly List<(int color, StringBuilder text)> _spans = new();

        /// <summary>直前の文字の次の位置（ドット）。字間の空きの判定に使う。</summary>
        public int NextX { get; set; }

        public bool HasText => _spans.Count > 0;

        public void Append(int color, int codePoint)
        {
            if (_spans.Count == 0 || _spans[^1].color != color) _spans.Add((color, new StringBuilder()));
            _spans[^1].text.Append(char.ConvertFromUtf32(codePoint));
        }

        public CaptionLine Build() => new(_spans.Select(s => new CaptionSpan(s.color, s.text.ToString())).ToList());
    }
}
