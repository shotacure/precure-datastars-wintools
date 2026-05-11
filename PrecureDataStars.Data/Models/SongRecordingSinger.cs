
namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_recording_singers テーブルに対応するエンティティモデル
/// （複合 PK: song_recording_id + role_code + singer_seq、v1.2.3 追加 / v1.3.0 ブラッシュアップ続編で role_code 追加）。
/// <para>
/// 1 録音（<see cref="SongRecording"/>）に対する歌唱者連名を順序付きで保持する。
/// 既存の <see cref="SongRecording.SingerName"/> フリーテキスト列は温存しており、
/// 本テーブルに行が無い録音では従来通りフリーテキストが表示に使われる
/// （フォールバック処理は SiteBuilder 側で当面実装しない、stage 16 のスコープ判断）。
/// </para>
/// <para>
/// <see cref="BillingKind"/> は 2 値：
/// <list type="bullet">
///   <item><c>PERSON</c> — 個人歌唱（例: 五條真由美）。<see cref="PersonAliasId"/> 必須。</item>
///   <item><c>CHARACTER_WITH_CV</c> — キャラ(CV:声優)（例: 美墨なぎさ(CV:本名陽子)）。
///     <see cref="CharacterAliasId"/> と <see cref="VoicePersonAliasId"/> 必須。</item>
/// </list>
/// 「キャラ名のみ（CV 表記なし）」「声優(キャラ) の語順倒置」は未正規化扱いで
/// 本モデルでは扱わない。それらの表記は元データを正規化するか、ユニット名義の
/// <c>display_text_override</c> で逃がす設計。
/// </para>
/// <para>
/// 「キュアブラック / 美墨なぎさ (CV: 本名 陽子)」のような同 CV のスラッシュ並列は、
/// <see cref="SlashCharacterAliasId"/>（または PERSON 行なら <see cref="SlashPersonAliasId"/>）
/// で表現する。スラッシュ並列は最大 1 個（2-way）まで。
/// </para>
/// <para>
/// <see cref="PrecedingSeparator"/> は seq>=2 の行で前 seq との区切り文字を保持する。
/// 例: "&amp;" "、" " with "。初出盤の表記をそのまま再現する目的。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ続編で <see cref="RoleCode"/> プロパティを追加した。
/// 録音に紐付く役職を「歌（VOCALS）」だけでなく「コーラス（CHORUS）」等にも拡張するため、
/// roles マスタへの FK を持たせる方針。既存データは全て VOCALS が既定値で埋まる。
/// PK は (song_recording_id, role_code, singer_seq) の 3 列複合に変更した
/// （song_credits / bgm_cue_credits と同じパターン）。
/// </para>
/// </summary>
public sealed class SongRecordingSinger
{
    /// <summary>対象録音 ID（→ song_recordings.song_recording_id、複合 PK 第 1 列）。</summary>
    public int SongRecordingId { get; set; }

    /// <summary>
    /// 役職コード（→ roles.role_code、複合 PK 第 2 列、v1.3.0 ブラッシュアップ続編で追加）。
    /// 既定値は <c>VOCALS</c>。CHORUS など別役職を運用者が定義した場合はその値が入る。
    /// </summary>
    public string RoleCode { get; set; } = "VOCALS";

    /// <summary>連名表示順（複合 PK 第 3 列、1 始まり）。役職コードごとに独立採番。</summary>
    public byte SingerSeq { get; set; }

    /// <summary>歌唱クレジット種別（PERSON / CHARACTER_WITH_CV）。</summary>
    public SingerBillingKind BillingKind { get; set; }

    /// <summary>
    /// 主名義（PERSON）：人物名義参照。
    /// <see cref="BillingKind"/> が PERSON のとき非 NULL。
    /// </summary>
    public int? PersonAliasId { get; set; }

    /// <summary>
    /// 主名義（CHARACTER_WITH_CV）：キャラクター名義参照。
    /// <see cref="BillingKind"/> が CHARACTER_WITH_CV のとき非 NULL。
    /// </summary>
    public int? CharacterAliasId { get; set; }

    /// <summary>
    /// CV（声優）の人物名義参照（<see cref="BillingKind"/>=CHARACTER_WITH_CV のとき必須）。
    /// </summary>
    public int? VoicePersonAliasId { get; set; }

    /// <summary>
    /// スラッシュ並列の相方（PERSON 主名義側）。最大 1 個。
    /// </summary>
    public int? SlashPersonAliasId { get; set; }

    /// <summary>
    /// スラッシュ並列の相方（CHARACTER_WITH_CV 主名義側）。最大 1 個。
    /// 例: 主＝キュアブラック / 相方＝美墨なぎさ、CV は両者共通。
    /// </summary>
    public int? SlashCharacterAliasId { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>所属表記（"with ヤング・フレッシュ" 等の補助テキスト用、任意）。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// <see cref="SongRecordingSinger.BillingKind"/> の種別（v1.2.3 追加）。
/// 文字列表現は DB の ENUM 値と一致させる。
/// </summary>
public enum SingerBillingKind
{
    /// <summary>個人名義のみ（例: 五條真由美）。</summary>
    Person = 0,
    /// <summary>キャラ名義 + CV 表記（例: 美墨なぎさ(CV:本名陽子)）。</summary>
    CharacterWithCv = 1
}

/// <summary>
/// song_recording_singers.role_code の典型値を表す定数群
/// （v1.3.0 ブラッシュアップ続編で追加）。
/// <para>
/// roles マスタの任意の role_code を受け入れるため <c>string</c> 型のフィールドだが、
/// 主用途は <see cref="Vocals"/> と <see cref="Chorus"/> の 2 値想定。
/// </para>
/// </summary>
public static class SongRecordingSingerRoles
{
    /// <summary>歌（既定）。</summary>
    public const string Vocals = "VOCALS";
    /// <summary>コーラス（運用者が roles マスタに別途投入した場合に使う）。</summary>
    public const string Chorus = "CHORUS";
}
