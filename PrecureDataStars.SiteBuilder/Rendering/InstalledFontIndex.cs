using System.Text;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// この PC にインストールされた書体ファイルを、書体名から引くための索引。
/// <para>
/// Windows の書体一覧（DirectWrite）は、ユーザー別のフォルダに置かれた商用書体（モリサワの「MorisawaFonts」など）を
/// 返さないことがあり、SkiaSharp の <c>SKFontManager</c> でも「A-SK ミンカール Min2 H」のような名前は引けない。
/// そこで書体ファイルの name テーブルを直接読み、日本語名・英語名・「族名＋スタイル名」のどれでも引けるようにする。
/// </para>
/// <para>
/// 走査するのはシステムのフォントフォルダとユーザーのフォントフォルダ（配下を含む）。ファイルの先頭と name テーブルだけを
/// 読むので、数千ファイルでも数秒で済む。索引はビルド中に 1 度だけ作る。
/// </para>
/// </summary>
public sealed class InstalledFontIndex
{
    /// <summary>書体名（空白を詰めて小文字化したもの）→ ファイルパスと TTC 内の番号。</summary>
    private readonly Dictionary<string, (string Path, int Index)> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>走査したファイル数（ログ用）。</summary>
    public int FileCount { get; private set; }

    private InstalledFontIndex() { }

    /// <summary>既定のフォントフォルダ（システムとユーザー）を走査して索引を作る。</summary>
    public static InstalledFontIndex Build()
    {
        var index = new InstalledFontIndex();
        var dirs = new List<string>();
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windir.Length > 0) dirs.Add(Path.Combine(windir, "Fonts"));
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0) dirs.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                // 壊れたファイルや読めないファイルは飛ばす（EndOfStreamException は IOException に含まれる）。
                try { index.AddFile(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return index;
    }

    /// <summary>書体名からファイルを引く。見つからなければ null。</summary>
    public (string Path, int Index)? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(Normalize(name), out var hit) ? hit : null;
    }

    /// <summary>比較用に空白を詰め、全角英数を半角に寄せる。</summary>
    private static string Normalize(string s)
        => s.Normalize(NormalizationForm.FormKC).Replace(" ", "").Replace("　", "");

    private void AddFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        uint tag = ReadUInt32(reader);
        var offsets = new List<uint>();
        if (tag == 0x74746366) // 'ttcf'
        {
            reader.BaseStream.Seek(4, SeekOrigin.Current); // version
            uint numFonts = ReadUInt32(reader);
            for (uint i = 0; i < numFonts && i < 64; i++) offsets.Add(ReadUInt32(reader));
        }
        else
        {
            offsets.Add(0);
        }
        FileCount++;

        for (int fontIndex = 0; fontIndex < offsets.Count; fontIndex++)
        {
            foreach (var name in ReadNames(reader, offsets[fontIndex]))
                _byName.TryAdd(Normalize(name), (path, fontIndex));
        }
    }

    /// <summary>
    /// 1 書体ぶんの name テーブルから、引くのに使える名前を列挙する。
    /// 全名称（ID 4）、族名（ID 1）、族名＋サブファミリー（ID 1 + 2）、タイポグラフィック族名＋スタイル（ID 16 + 17）を、
    /// 言語ごとに（日本語名と英語名の両方）返す。
    /// </summary>
    private static IEnumerable<string> ReadNames(BinaryReader reader, uint fontOffset)
    {
        reader.BaseStream.Seek(fontOffset, SeekOrigin.Begin);
        uint sfnt = ReadUInt32(reader);
        if (sfnt != 0x00010000 && sfnt != 0x4F54544F && sfnt != 0x74727565) yield break; // 1.0 / 'OTTO' / 'true'
        ushort numTables = ReadUInt16(reader);
        reader.BaseStream.Seek(6, SeekOrigin.Current);

        uint nameOffset = 0, nameLength = 0;
        for (int i = 0; i < numTables; i++)
        {
            uint t = ReadUInt32(reader);
            reader.BaseStream.Seek(4, SeekOrigin.Current);
            uint off = ReadUInt32(reader);
            uint len = ReadUInt32(reader);
            if (t == 0x6E616D65) { nameOffset = off; nameLength = len; break; } // 'name'
        }
        if (nameOffset == 0 || nameLength == 0) yield break;

        reader.BaseStream.Seek(nameOffset, SeekOrigin.Begin);
        var table = reader.ReadBytes((int)Math.Min(nameLength, 1 << 20));
        if (table.Length < 6) yield break;
        ushort count = (ushort)((table[2] << 8) | table[3]);
        ushort stringOffset = (ushort)((table[4] << 8) | table[5]);

        // (言語, nameID) → 文字列。言語ごとに族名とスタイル名を組み合わせるため、いったん集める。
        var byLang = new Dictionary<ushort, Dictionary<ushort, string>>();
        for (int i = 0; i < count; i++)
        {
            int rec = 6 + i * 12;
            if (rec + 12 > table.Length) break;
            ushort platform = (ushort)((table[rec] << 8) | table[rec + 1]);
            ushort encoding = (ushort)((table[rec + 2] << 8) | table[rec + 3]);
            ushort language = (ushort)((table[rec + 4] << 8) | table[rec + 5]);
            ushort nameId = (ushort)((table[rec + 6] << 8) | table[rec + 7]);
            ushort length = (ushort)((table[rec + 8] << 8) | table[rec + 9]);
            ushort offset = (ushort)((table[rec + 10] << 8) | table[rec + 11]);
            if (nameId is not (1 or 2 or 4 or 16 or 17)) continue;
            int start = stringOffset + offset;
            if (start + length > table.Length) continue;

            string? value = platform switch
            {
                3 when encoding is 1 or 10 => Encoding.BigEndianUnicode.GetString(table, start, length),
                0 => Encoding.BigEndianUnicode.GetString(table, start, length),
                1 when encoding == 0 => Encoding.ASCII.GetString(table, start, length),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value) || value.Any(c => c < ' ')) continue;
            if (!byLang.TryGetValue(language, out var names)) byLang[language] = names = new Dictionary<ushort, string>();
            names.TryAdd(nameId, value.Trim());
        }

        foreach (var names in byLang.Values)
        {
            if (names.TryGetValue(4, out var full)) yield return full;
            if (names.TryGetValue(1, out var family))
            {
                yield return family;
                if (names.TryGetValue(2, out var sub) && !string.Equals(sub, "Regular", StringComparison.OrdinalIgnoreCase))
                    yield return family + " " + sub;
            }
            if (names.TryGetValue(16, out var typoFamily))
            {
                yield return typoFamily;
                if (names.TryGetValue(17, out var typoStyle) && !string.Equals(typoStyle, "Regular", StringComparison.OrdinalIgnoreCase))
                    yield return typoFamily + " " + typoStyle;
            }
        }
    }

    private static uint ReadUInt32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        if (b.Length < 4) throw new EndOfStreamException();
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }

    private static ushort ReadUInt16(BinaryReader r)
    {
        var b = r.ReadBytes(2);
        if (b.Length < 2) throw new EndOfStreamException();
        return (ushort)((b[0] << 8) | b[1]);
    }
}
