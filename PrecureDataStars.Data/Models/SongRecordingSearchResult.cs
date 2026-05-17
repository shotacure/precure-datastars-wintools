namespace PrecureDataStars.Data.Models;

/// <summary>
/// <see cref="SongRecording"/> の検索結果用 DTO。
/// <para>
/// クレジット系マスタ管理 / クレジット本体編集の各ピッカーダイアログから呼ばれる
/// <c>SongRecordingsRepository.SearchAsync</c> の結果として、親曲タイトル
/// （songs.title / title_kana）を JOIN して返すために、<see cref="SongRecording"/> 本体に
/// 持たせるべきでないクエリ用カラムを別途格納する読み取り専用型。
/// </para>
/// <para>
/// この型は INSERT / UPDATE には使わない。CRUD では従来通り <see cref="SongRecording"/>
/// を使うこと。
/// </para>
/// </summary>
public sealed class SongRecordingSearchResult
{
    /// <summary>song_recordings.song_recording_id（主キー）。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>song_recordings.song_id（親曲 ID）。</summary>
    public int SongId { get; set; }

    /// <summary>songs.title（親曲タイトル、検索結果の主表示）。</summary>
    public string? SongTitle { get; set; }

    /// <summary>songs.title_kana（親曲タイトルかな）。</summary>
    public string? SongTitleKana { get; set; }

    /// <summary>song_recordings.singer_name（歌手名）。</summary>
    public string? SingerName { get; set; }

    /// <summary>song_recordings.singer_name_kana（歌手名かな）。</summary>
    public string? SingerNameKana { get; set; }

    /// <summary>song_recordings.variant_label（バリエーションラベル、TV size 等）。</summary>
    public string? VariantLabel { get; set; }
}
