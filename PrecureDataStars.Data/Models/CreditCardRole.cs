namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_roles テーブルに対応するエンティティモデル（PK: card_role_id）。
/// カード内に登場する役職 1 つ = 1 行。レイアウト位置は所属する Group（<see cref="CardGroupId"/>）と
/// グループ内左右順（<see cref="OrderInGroup"/>）で表現する。
/// モデルを大幅刷新：
/// <list type="bullet">
///   <item><description>旧構成: <c>(card_id, tier, group_in_tier, order_in_group)</c> の 4 列複合キー</description></item>
///   <item><description>新構成: <see cref="CardGroupId"/>（→ credit_card_groups.card_group_id）の FK 1 本 + <see cref="OrderInGroup"/></description></item>
/// </list>
/// Card / Tier / Group の階層関係は FK チェーンで一意に決まる
/// （card_role → card_group → card_tier → card）。
/// 同一 (card_group_id, order_in_group) は UNIQUE。
/// <see cref="RoleCode"/> を NULL にできるのは「ブランクロール」用途。
/// 役職ラベルを伴わないロゴ単独表示の枠などに使う。
/// </summary>
public sealed class CreditCardRole
{
    /// <summary>カード内役職の主キー（AUTO_INCREMENT）。</summary>
    public int CardRoleId { get; set; }

    /// <summary>所属する Group ID（→ credit_card_groups.card_group_id）。</summary>
    public int CardGroupId { get; set; }

    /// <summary>役職コード（→ roles.role_code、ブランクロール時は NULL）。</summary>
    public string? RoleCode { get; set; }

    /// <summary>同 Group 内での左右順（1 始まり）。</summary>
    public byte OrderInGroup { get; set; }

    /// <summary>人物エントリの所属（<c>affiliation_company_alias_id</c> / <c>affiliation_text</c>）の
    /// 描画方法。<c>"SUFFIX"</c> = 名前右の <c>(屋号)</c> 後置（TV キャスト所属など従来挙動）、
    /// <c>"PREFIX"</c> = 名前左の屋号列（映画の「製作:」「配給:」「宣伝:」の 2 カラム表記）。
    /// 同じ役職コードでも作品ごとに前置 / 後置が変わるため per-instance で持つ。</summary>
    public string AffiliationLayout { get; set; } = "SUFFIX";

    /// <summary>画面の役職の表記（例: 役職名「CGプロダクションマネージャー」に対して「CGプロダクション・マネージャー」）。
    /// 役職名（roles.name_ja）と表記が違うときだけ入れ、null なら役職名で出す。
    /// クレジットの表示だけに使い、集計は <see cref="RoleCode"/> のまま。</summary>
    public string? RoleLabelText { get; set; }

    /// <summary>画面に出た役職名の誤記（例: 役職「デジタル特殊効果」が画面では「デジタル特種効果」）。null なら誤記なし。
    /// 正しい表記は <see cref="RoleLabelText"/> か役職名。クレジットでは誤記を取り消し線で 1 行目、正しい表記を 2 行目に出す。</summary>
    public string? RoleMisprintText { get; set; }

    /// <summary>true なら直前の役職（同じ Group の <see cref="OrderInGroup"/> 一つ前）と 1 行にまとめて表示する。
    /// まとめる役職のうち 2 つ目以降に立てる。データは役職ごとに分けて同じエントリを入れ、まとめるのはクレジットの表示だけ。</summary>
    public bool JoinPrevious { get; set; }

    /// <summary>1 行にまとめた行での、直前の役職との区切りの文字（「・」「／」など画面どおり）。null は区切りなし。
    /// <see cref="JoinPrevious"/> が true の役職だけが持つ。まとめた行の役職名は、各役職の表記
    /// （<see cref="RoleLabelText"/> か役職名）をこの区切りでつないで組み立てる。</summary>
    public string? JoinSeparator { get; set; }

    /// <summary>true ならクレジットの表示でこの行の役職名を出さず、中身（会社・人物）だけを出す。集計・関与は <see cref="RoleCode"/> のまま。
    /// 画面で 1 つの見出しの下に会社ごとのスタッフが続くとき（例：「CG制作協力」の下に 2 社）、2 社目の見出しの行に立てる。</summary>
    public bool HideRoleLabel { get; set; }

    /// <summary>true ならこの行は画面にはクレジットされていない（毎回出る役職がその回だけ抜けているクレジット漏れ）。
    /// データは入れて集計・関与は <see cref="RoleCode"/> のまま数え、クレジットの表示だけ括弧で包んで薄くし、画面に無かったことを示す。</summary>
    public bool NotOnScreen { get; set; }

    /// <summary>この役職の行で主題歌ループ（<c>{#THEME_SONGS}</c>）が出す曲を絞る劇中順の範囲。
    /// <c>"N"</c>（seq=N の 1 曲）か <c>"N-M"</c>（seq が N 以上 M 以下）で、episode_theme_songs.seq / series_theme_songs.seq を指す。
    /// null なら絞らない。主題歌の形式（roles.role_format_kind='THEME_SONG'）の役職でだけ意味を持ち、
    /// 主題歌のカードが 2 枚に分かれて挿入歌を振り分けるときに使う。テンプレ側の <c>seq=</c> と両方あれば両方を満たす曲だけ出す。</summary>
    public string? ThemeSongSeq { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
