using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>track_audio_fingerprints テーブル（<see cref="TrackAudioFingerprint"/>）のリポジトリ。</summary>
public sealed class TrackAudioFingerprintsRepository : RepositoryBase
{
    public TrackAudioFingerprintsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
        SELECT catalog_no AS CatalogNo, track_no AS TrackNo, sub_order AS SubOrder, method_version AS MethodVersion,
               sample_rate_hz AS SampleRateHz, fft_size AS FftSize, hop_samples AS HopSamples, hash_count AS HashCount,
               duration_ms AS DurationMs, pcm_sha256 AS PcmSha256, fingerprint AS Fingerprint, read_at AS ReadAt,
               created_by AS CreatedBy, updated_by AS UpdatedBy
          FROM track_audio_fingerprints
        """;

    /// <summary>品番のディスクの全トラックの指紋（トラック番号順）。</summary>
    public async Task<IReadOnlyList<TrackAudioFingerprint>> GetByCatalogAsync(string catalogNo, CancellationToken ct = default)
    {
        return await QueryListAsync<TrackAudioFingerprint>(SelectColumns + " WHERE catalog_no = @catalogNo ORDER BY track_no, sub_order;", new { catalogNo }, ct).ConfigureAwait(false);
    }

    /// <summary>品番のディスクで指紋を持つトラックの番号と読んだ日時（指紋のバイト列は読まない）。</summary>
    public async Task<IReadOnlyList<(byte TrackNo, byte MethodVersion, DateTime ReadAt)>> GetHeadersByCatalogAsync(string catalogNo, CancellationToken ct = default)
    {
        var rows = await QueryListAsync<HeaderRow>(
            "SELECT track_no AS TrackNo, method_version AS MethodVersion, read_at AS ReadAt FROM track_audio_fingerprints WHERE catalog_no = @catalogNo ORDER BY track_no;",
            new { catalogNo }, ct).ConfigureAwait(false);
        return rows.Select(r => (r.TrackNo, r.MethodVersion, r.ReadAt)).ToList();
    }

    private sealed class HeaderRow
    {
        public byte TrackNo { get; set; }
        public byte MethodVersion { get; set; }
        public DateTime ReadAt { get; set; }
    }

    /// <summary>1 トラックの指紋を入れる（同じトラックの行があれば置き換える）。</summary>
    public async Task UpsertAsync(TrackAudioFingerprint row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO track_audio_fingerprints
              (catalog_no, track_no, sub_order, method_version, sample_rate_hz, fft_size, hop_samples, hash_count,
               duration_ms, pcm_sha256, fingerprint, read_at, created_by, updated_by)
            VALUES
              (@CatalogNo, @TrackNo, @SubOrder, @MethodVersion, @SampleRateHz, @FftSize, @HopSamples, @HashCount,
               @DurationMs, @PcmSha256, @Fingerprint, @ReadAt, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              method_version = VALUES(method_version), sample_rate_hz = VALUES(sample_rate_hz), fft_size = VALUES(fft_size),
              hop_samples = VALUES(hop_samples), hash_count = VALUES(hash_count), duration_ms = VALUES(duration_ms),
              pcm_sha256 = VALUES(pcm_sha256), fingerprint = VALUES(fingerprint), read_at = VALUES(read_at),
              updated_by = VALUES(updated_by);
            """;
        await ExecuteAsync(sql, row, ct).ConfigureAwait(false);
    }
}
