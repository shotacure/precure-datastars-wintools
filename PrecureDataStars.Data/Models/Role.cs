namespace PrecureDataStars.Data.Models;

/// <summary>
/// roles テーブルに対応するマスタモデル（PK: role_code）。
/// クレジット内の役職を定義する。<see cref="RoleFormatKind"/> で「この役職下のエントリは
/// どのような書式・参照を取るか」を分類する。
/// <see cref="RoleFormatKind"/> の意味:
/// "NORMAL"       ... 単純な「役職: 名義列」（脚本／演出／作画監督 等） / 
/// "SERIAL"       ... 連載。書式は <c>role_templates</c> テーブルで持つ / 
/// "THEME_SONG"   ... 主題歌。entry が song_recording を持つ / 
/// "VOICE_CAST"   ... 声の出演。entry がキャラクター名義 + 人物名義のペアを持つ / 
/// "COMPANY_ONLY" ... 企業のみが並ぶ役職（制作著作・製作協力・レーベル等） / 
/// "LOGO_ONLY"    ... ロゴのみが並ぶ役職 /
/// "NOTICE"       ... 表記のみの役職（著作権表記・映画のタイトルカード等）。クレジット上に表示はするが、
///                    人物・企業の関与（クレジット履歴・担当数）には数えず、役職詳細ページも作らない。
/// 役職の書式テンプレートは <see cref="RoleTemplate"/>（<c>role_templates</c> テーブル）で
/// 管理し、本テーブルには持たない。
/// 役職の系譜（変更元 → 変更先）は分裂・併合を含む多対多関係で表現する必要があるため、
/// <see cref="RoleSuccession"/>（<c>role_successions</c> テーブル）で持つ。
/// </summary>
public sealed class Role
{
    /// <summary>役職コード（PK、例: "SCRIPT", "VOICE_CAST", "PRODUCTION_AUTH"）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>日本語表示名（例: "脚本"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>役職書式区分（"NORMAL"/"SERIAL"/"THEME_SONG"/"VOICE_CAST"/"COMPANY_ONLY"/"LOGO_ONLY"/"NOTICE"）。既定 "NORMAL"。</summary>
    public string RoleFormatKind { get; set; } = "NORMAL";

    /// <summary>
    /// 音楽クレジットの区分（<see cref="MusicCreditGroups"/> の値）。null は本編クレジットだけで使う役職。
    /// 楽曲・劇伴・音盤のクレジット（song_credits / song_recording_singers / bgm_cue_credits / music_credits）は
    /// この区分で「作詞・作曲・編曲 / 演奏・コーラス等 / レコーディング / 音盤製作」に分けて表示する。
    /// </summary>
    public string? MusicCreditGroup { get; set; }

    /// <summary>表示順（小さい値ほど先頭。UNIQUE）。</summary>
    public ushort? DisplayOrder { get; set; }

    /// <summary>
    /// クレジット HTML 描画で役職名カラム（左セル）を非表示にするかのフラグ。
    /// 0=表示（既定）、1=非表示。LABEL 役職のように「データは別役職として持つが、
    /// 表示上は親役職の末尾に屋号だけ並べたい」というケースで 1 をセットする。
    /// 集計（CreditInvolvementIndex / 役職別ランキング / 企業関与一覧）には影響せず、
    /// 純粋に Catalog プレビュー / SiteBuilder の HTML テンプレ側で
    /// <c>td.role-name</c> セルを空文字に置き換える。
    /// </summary>
    public byte HideRoleNameInCredit { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>roles.music_credit_group の値。表示順もこの並び（作詞・作曲・編曲 → 演奏・コーラス等 → レコーディング → 音盤製作）。</summary>
public static class MusicCreditGroups
{
    /// <summary>作詞・作曲・編曲。</summary>
    public const string Writing = "WRITING";
    /// <summary>演奏・コーラス等。</summary>
    public const string Performance = "PERFORMANCE";
    /// <summary>レコーディング（録音側のスタッフ・スタジオ）。</summary>
    public const string Recording = "RECORDING";
    /// <summary>音盤製作（盤側のスタッフ）。</summary>
    public const string Release = "RELEASE";

    /// <summary>表示順に並べた全区分。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Writing, Performance, Recording, Release };

    /// <summary>区分の見出し。</summary>
    public static string Label(string group) => group switch
    {
        Writing => "作詞・作曲・編曲",
        Performance => "演奏・コーラス等",
        Recording => "レコーディング",
        Release => "音盤製作",
        _ => group
    };
}
