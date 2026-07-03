namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// コンボ／リストの DataSource に流す「ID + 表示文字列」のジェネリック DTO
/// （DisplayMember="Label" / ValueMember="Id" バインド用。プロパティ名は両バインド文字列と
/// 結合しているため変更しないこと）。ID 型は int / int? / string などを型引数で使い分ける。
/// 各フォームが private の非ジェネリック版（IdLabel / CodeLabel / IdLabelNullable / IdLabelStr）を
/// 重複定義していたものを単一定義へ集約した。
/// 表示に <c>ToString()</c> を使う（DisplayMember を設定しない）コンボ用の型は対象外で、
/// 各フォームの <c>ToString() => Label</c> 実装付き private 型を引き続き使う。
/// </summary>
internal sealed record IdLabel<TId>(TId Id, string Label);
