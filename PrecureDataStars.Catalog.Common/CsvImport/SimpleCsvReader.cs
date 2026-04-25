using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PrecureDataStars.Catalog.Common.CsvImport;

/// <summary>
/// シンプルな RFC 4180 準拠の CSV リーダー（v1.1.3 追加）。
/// <para>
/// ヘッダー行必須、カンマ区切り、ダブルクォート囲み可。UTF-8（BOM 付き / 無し）対応。
/// </para>
/// <para>
/// 制約:
/// <list type="bullet">
///   <item>区切り文字はカンマ固定（タブや ; は非対応）</item>
///   <item>改行を含むフィールド（"..\r\n.." 形式の埋め込み改行）も扱える</item>
///   <item>ヘッダは各列名を Trim して大文字小文字を区別して扱う</item>
/// </list>
/// CSV 取り込み機能の簡易フロントエンドであり、外部ライブラリ依存を増やさないための最小実装。
/// </para>
/// </summary>
public static class SimpleCsvReader
{
    /// <summary>
    /// 指定パスの CSV ファイルを読み、ヘッダと行（列名→値 の辞書）のリストを返す。
    /// 空行は無視する。ヘッダが空 or 列数が 0 の場合は例外を投げる。
    /// </summary>
    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows) ReadFile(string path)
    {
        // UTF-8 BOM 有無どちらでも読める Encoding を指定（Encoding.UTF8 は BOM を自動処理する）。
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Read(reader);
    }

    /// <summary>
    /// <see cref="TextReader"/> から CSV を読み取る。単体テスト用の口も兼ねる。
    /// </summary>
    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows) Read(TextReader reader)
    {
        var records = ParseRecords(reader).ToList();
        if (records.Count == 0)
        {
            throw new InvalidOperationException("CSV にヘッダ行がありません。");
        }

        var headers = records[0].Select(h => h.Trim()).ToList();
        if (headers.Count == 0 || headers.All(string.IsNullOrEmpty))
        {
            throw new InvalidOperationException("CSV のヘッダ列名が空です。");
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (int i = 1; i < records.Count; i++)
        {
            var fields = records[i];
            // 全列が空の行は読み飛ばし（末尾の余計な改行のみの行への配慮）
            if (fields.All(string.IsNullOrEmpty)) continue;

            var dict = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
            for (int c = 0; c < headers.Count; c++)
            {
                // 列数不足は空文字として扱う（CSV が短い行を許容するためのゆるめ運用）
                string value = c < fields.Count ? fields[c] : "";
                dict[headers[c]] = value;
            }
            rows.Add(dict);
        }
        return (headers, rows);
    }

    /// <summary>
    /// TextReader から 1 論理行ずつ（改行埋め込みフィールドも考慮して）フィールドリストにパースして返す。
    /// </summary>
    private static IEnumerable<List<string>> ParseRecords(TextReader reader)
    {
        var sb = new StringBuilder();
        var record = new List<string>();
        bool inQuotes = false;
        bool anyCharOnRecord = false;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            char c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    // 連続する "" は 1 つのダブルクォートとしてフィールド値に追加
                    int next = reader.Peek();
                    if (next == '"')
                    {
                        reader.Read();
                        sb.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                // フィールド値の途中ではなく先頭に出てくるのを想定（フィールド値途中の " は入力エラー）。
                // ここでは緩めに、先頭で無くとも突入を許容しておく。
                inQuotes = true;
                continue;
            }
            if (c == ',')
            {
                record.Add(sb.ToString());
                sb.Clear();
                anyCharOnRecord = true;
                continue;
            }
            if (c == '\r')
            {
                // CRLF の CR 側は LF 合流のために読み捨て
                int next = reader.Peek();
                if (next == '\n') reader.Read();
                record.Add(sb.ToString());
                sb.Clear();
                if (anyCharOnRecord || record.Count > 1 || !string.IsNullOrEmpty(record[0]))
                {
                    yield return record;
                }
                record = new List<string>();
                anyCharOnRecord = false;
                continue;
            }
            if (c == '\n')
            {
                record.Add(sb.ToString());
                sb.Clear();
                if (anyCharOnRecord || record.Count > 1 || !string.IsNullOrEmpty(record[0]))
                {
                    yield return record;
                }
                record = new List<string>();
                anyCharOnRecord = false;
                continue;
            }
            sb.Append(c);
            anyCharOnRecord = true;
        }

        // 最終行（終端に改行が無いファイル）を取りこぼさないためのフラッシュ
        if (sb.Length > 0 || record.Count > 0)
        {
            record.Add(sb.ToString());
            if (anyCharOnRecord || record.Count > 1 || !string.IsNullOrEmpty(record[0]))
            {
                yield return record;
            }
        }
    }
}
