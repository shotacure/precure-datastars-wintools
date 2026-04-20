namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cues テーブルに対応するエンティティモデル（複合 PK: series_id + m_no_detail）。
/// <para>
/// 劇伴（BGM）の「音源 1 件 = 1 行」を表す。シリーズ × M 番号詳細表記で 1 意。
/// v1.1.0 の旧 bgm_cues + bgm_recordings の二階層構造は廃止し、1 テーブルに統合した。
/// 録音セッションは <see cref="SessionNo"/> 属性として保持し、シリーズごとの <c>bgm_sessions</c> マスタに FK する。
/// </para>
/// <para>
/// <see cref="MNoDetail"/> は旧データ準拠の詳細表記（例: "M220b Rhythm Cut", "M01",
/// "M224 ShortVer A"）を保持する。枝番を畳んだグループ化用キーは <see cref="MNoClass"/>
/// （例: "M220"）に別途格納され、UI 上の分類・ソートに利用できる。
/// </para>
/// <para>
/// 作曲者・編曲者は枝番（= 音源）ごとに異なる可能性があるため、それぞれの行が独立して保持する。
/// </para>
/// </summary>
public sealed class BgmCue
{
    /// <summary>所属シリーズ ID（→ series）。複合 PK の第 1 列。</summary>
    public int SeriesId { get; set; }

    /// <summary>M 番号詳細表記（例: "M220b Rhythm Cut"）。複合 PK の第 2 列。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>
    /// 録音セッション番号（→ bgm_sessions）。シリーズごとに 0, 1, 2, ... と採番される。
    /// 0 は「未設定」既定値。
    /// </summary>
    public byte SessionNo { get; set; } = 0;

    /// <summary>M 番号分類（旧 musics.m_no_class 相当。例: "M220"）。グループ化・ソート用。</summary>
    public string? MNoClass { get; set; }

    /// <summary>メニュー名（旧 musics.menu 相当）。枝番ごとに異なる可能性あり。</summary>
    public string? MenuTitle { get; set; }

    /// <summary>作曲者名。</summary>
    public string? ComposerName { get; set; }

    /// <summary>作曲者名（読み）。</summary>
    public string? ComposerNameKana { get; set; }

    /// <summary>編曲者名。</summary>
    public string? ArrangerName { get; set; }

    /// <summary>編曲者名（読み）。</summary>
    public string? ArrangerNameKana { get; set; }

    /// <summary>音源の尺（秒）。</summary>
    public ushort? LengthSeconds { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
