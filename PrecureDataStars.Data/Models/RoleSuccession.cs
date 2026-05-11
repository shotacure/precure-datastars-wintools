
namespace PrecureDataStars.Data.Models;

/// <summary>
/// role_successions テーブルに対応するモデル（複合 PK: from_role_code + to_role_code）。
/// 役職の系譜を多対多で表現する関係エンティティ
/// （v1.3.0 ブラッシュアップ続編で新設）。
/// <para>
/// 「<see cref="FromRoleCode"/> 役職が <see cref="ToRoleCode"/> 役職に移行された／引き継がれた」
/// という意味を持つ 1 本の有向辺。同じ from から複数の to を持てるので分裂が表現できる
/// （A → B かつ A → C）。複数の from から同じ to を持てるので併合も表現できる
/// （B → A かつ C → A）。
/// </para>
/// <para>
/// クラスタ：from↔to の関係を「無向辺」とみなして連結成分をたどると、
/// 同じ役職の歴代の名前のすべてが 1 つのクラスタに集約される。
/// 統計集計ではクラスタ単位で 1 役職とみなす。
/// </para>
/// <para>
/// 自己ループ（FromRoleCode == ToRoleCode）は登録不可。
/// 本来 DB の CHECK 制約で禁止したいが、MySQL 8 では FK の参照アクション（CASCADE 等）で
/// 変更される列を CHECK で参照できない仕様のため、<see cref="RoleSuccessionsRepository"/>
/// の UpsertAsync 入口でアプリ層ガードを行う方針。
/// </para>
/// </summary>
public sealed class RoleSuccession
{
    /// <summary>系譜元の役職コード（移行する前の役職）。</summary>
    public string FromRoleCode { get; set; } = "";

    /// <summary>系譜先の役職コード（移行した先の役職）。</summary>
    public string ToRoleCode { get; set; } = "";

    /// <summary>備考（移行の経緯、出典など）。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
