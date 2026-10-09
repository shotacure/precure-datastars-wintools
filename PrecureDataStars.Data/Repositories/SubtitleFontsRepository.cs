using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// subtitle_fonts テーブル（サブタイトルのテロップ画像に使うフォントのマスタ）の読み取りリポジトリ。
/// 値は SQL で直接入れる運用なので、取得だけを持つ。
/// </summary>
public sealed class SubtitleFontsRepository : RepositoryBase
{
    /// <summary><see cref="SubtitleFontsRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SubtitleFontsRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>全フォントを書体名順で取得する。</summary>
    public async Task<IReadOnlyList<SubtitleFont>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              font_name    AS FontName,
              license_kind AS LicenseKind,
              display_name AS DisplayName,
              product_url  AS ProductUrl
            FROM subtitle_fonts
            ORDER BY font_name;
        """;

        return await QueryListAsync<SubtitleFont>(sql, ct: ct).ConfigureAwait(false);
    }
}
