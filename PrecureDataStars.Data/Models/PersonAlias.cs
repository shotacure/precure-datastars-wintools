namespace PrecureDataStars.Data.Models;

/// <summary>
/// person_aliases テーブルに対応するエンティティモデル（PK: alias_id）。
/// <para>
/// 人物の名義（表記）。1 person に多くの alias が紐付き、改名時は
/// <c>predecessor_alias_id</c> / <c>successor_alias_id</c> でリンクすることで
/// 表記履歴を辿れるようにする。alias と person の結び付けは
/// <c>person_alias_persons</c> 中間テーブル経由（通常 1:1、共同名義のみ多対多）。
/// </para>
/// <para>
/// v1.2.3 追加：<see cref="DisplayTextOverride"/> はユニット名義などで定形外の長い
/// 表示文字列が必要なケース用。非 NULL の場合、表示ロジックは <see cref="Name"/> より
/// この値を優先する。例: name=「プリキュアシンガーズ+1」、override=「プリキュアシンガーズ+1(五條真由美、池田 彩、うちやえゆか、二場裕美)」。
/// </para>
/// <para>
/// v1.2.3 追加：ユニット名義の構成メンバー（連名の中身）は新設の
/// <c>person_alias_members</c> テーブルで管理する。本モデルから直接列としては保持しない
/// （リポジトリ <c>PersonAliasMembersRepository</c> 経由で取得する）。
/// </para>
/// </summary>
public sealed class PersonAlias
{
    /// <summary>名義の主キー（AUTO_INCREMENT）。</summary>
    public int AliasId { get; set; }

    /// <summary>名義表記（必須、例: "金月真美" "ゆう" "ゆうきまさみ"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>名義表記の読み。</summary>
    public string? NameKana { get; set; }

    /// <summary>名義の英語表記（v1.2.4 追加）。英文クレジット出力で使用。</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// 表示用の上書き文字列（v1.2.3 追加、VARCHAR(1024) NULL）。
    /// 非 NULL のとき、アプリ側の表示ロジックは <see cref="Name"/> ではなくこの値を使う。
    /// 通常のユニット名義（"Berryz工房" 等、表示が name で済むもの）では NULL のままにし、
    /// "プリキュアシンガーズ+1(...)" のような定形外の長い表示テキストが必要なときだけ埋める。
    /// </summary>
    public string? DisplayTextOverride { get; set; }

    /// <summary>前任名義 ID（改名前の名義を指す自参照、任意）。</summary>
    public int? PredecessorAliasId { get; set; }

    /// <summary>後任名義 ID（改名後の名義を指す自参照、任意）。</summary>
    public int? SuccessorAliasId { get; set; }

    /// <summary>名義の有効開始日（任意）。</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>名義の有効終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 表示用文字列を返す（v1.2.3 追加）。
    /// <see cref="DisplayTextOverride"/> が非空ならそちらを、なければ <see cref="Name"/> を返す。
    /// </summary>
    public string GetDisplayName()
        => string.IsNullOrWhiteSpace(DisplayTextOverride) ? Name : DisplayTextOverride!;
}
