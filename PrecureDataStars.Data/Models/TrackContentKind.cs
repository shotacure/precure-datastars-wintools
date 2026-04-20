namespace PrecureDataStars.Data.Models;

/// <summary>
/// track_content_kinds テーブルに対応するマスタモデル（PK: kind_code）。
/// <para>
/// トラックの内容種別（歌・劇伴・ドラマ・ラジオ・ジングル・チャプター・その他）を定義する。
/// トラックはこのコードによって「どの実体テーブルを参照するか」が決まる。
/// </para>
/// </summary>
public sealed class TrackContentKind
{
    /// <summary>
    /// トラック内容コード（PK）。
    /// <list type="bullet">
    /// <item><c>SONG</c> — 歌（song_recordings 参照）</item>
    /// <item><c>BGM</c> — 劇伴（bgm_cues 参照）</item>
    /// <item><c>DRAMA</c> — ドラマ（タイトルは track_title_override）</item>
    /// <item><c>RADIO</c> — ラジオ（タイトルは track_title_override）</item>
    /// <item><c>JINGLE</c> — ジングル</item>
    /// <item><c>CHAPTER</c> — BD/DVD チャプター</item>
    /// <item><c>OTHER</c> — その他</item>
    /// </list>
    /// </summary>
    public string KindCode { get; set; } = "";

    /// <summary>日本語表示名。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>画面表示順（小さい値が先頭。UNIQUE）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}