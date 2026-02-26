namespace PrecureDataStars.Data.Models;

/// <summary>
/// episode_parts テーブルに対応するエンティティモデル（複合 PK: episode_id + episode_seq）。
/// <para>
/// 1 つのエピソードを構成するパート（アバンタイトル、A パート、B パート、ED、次回予告 等）を
/// 時系列順に管理する。各パートの放送尺（OA）・円盤尺・配信尺を秒単位で記録できる。
/// </para>
/// </summary>
public sealed class EpisodePart
{
    /// <summary>所属エピソードの ID（FK → episodes.episode_id）。</summary>
    public int EpisodeId { get; set; }

    /// <summary>エピソード内の並び順（1 始まり、TINYINT UNSIGNED）。</summary>
    public byte EpisodeSeq { get; set; }

    /// <summary>パート種別コード（FK → part_types.part_type）。</summary>
    public string PartType { get; set; } = "";

    /// <summary>OA（放送版）の尺（秒）。NULL はデータ未入力を示す。</summary>
    public ushort? OaLength { get; set; }

    /// <summary>円盤（Blu-ray / DVD）収録版の尺（秒）。</summary>
    public ushort? DiscLength { get; set; }

    /// <summary>配信（VOD）版の尺（秒）。</summary>
    public ushort? VodLength { get; set; }

    /// <summary>備考（自由テキスト）。</summary>
    public string? Notes { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}
