namespace PrecureDataStars.Data.Models;

/// <summary>
/// track_audio_fingerprints テーブルに対応するエンティティモデル（複合 PK: catalog_no + track_no + sub_order）。
/// CD の物理トラック 1 本の音から取った特徴量（ランドマーク指紋）と、同じ音かを見分けるための PCM 全体のハッシュを持つ。
/// 物理トラック単位なので <see cref="SubOrder"/> は常に 0。
/// </summary>
public sealed class TrackAudioFingerprint
{
    /// <summary>所属ディスクの品番（→ tracks.catalog_no）。</summary>
    public string CatalogNo { get; set; } = "";

    /// <summary>トラック番号（→ tracks.track_no）。</summary>
    public byte TrackNo { get; set; }

    /// <summary>トラック内順序。物理トラック単位なので常に 0。</summary>
    public byte SubOrder { get; set; }

    /// <summary>特徴量の取り方の版。版が違う指紋どうしは突き合わせない。</summary>
    public byte MethodVersion { get; set; }

    /// <summary>解析時のサンプリング周波数（Hz）。</summary>
    public int SampleRateHz { get; set; }

    /// <summary>FFT の点数。</summary>
    public int FftSize { get; set; }

    /// <summary>フレームの間隔（サンプル数）。フレーム番号 × これ ÷ サンプリング周波数 が秒。</summary>
    public int HopSamples { get; set; }

    /// <summary>指紋の項目数。</summary>
    public int HashCount { get; set; }

    /// <summary>読んだ音の長さ（ミリ秒）。</summary>
    public int DurationMs { get; set; }

    /// <summary>読んだ PCM 全体（16 ビット LE ステレオ）の SHA-256（16 進小文字 64 桁）。同じ音源かの見分けに使う。</summary>
    public string PcmSha256 { get; set; } = "";

    /// <summary>指紋のバイト列（1 項目 6 バイト：ハッシュ 3 バイト LE ＋ フレーム番号 3 バイト LE）。</summary>
    public byte[] Fingerprint { get; set; } = Array.Empty<byte>();

    /// <summary>音を読んだ日時。</summary>
    public DateTime ReadAt { get; set; }

    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
