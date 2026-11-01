namespace PrecureDataStars.Data.Models;

/// <summary>
/// 名義に所属を添えられる作家連名の 1 行（歌の <see cref="SongCredit"/>・劇伴の <see cref="BgmCueCredit"/>）。
/// 所属の畳み方（同じ所属が続くときは最後の名義のあとにだけ添える、所属の会社名だけの行は続きを 1 回にまとめる）を
/// 歌と劇伴で同じ部品に通すための共通の形。
/// </summary>
/// <remarks>
/// <see cref="PersonAliasId"/> は、名義を持たない文字だけの行（劇伴の <see cref="BgmCueCreditEntryKinds.Text"/>）を表せるよう
/// null を許す。歌の作家連名は名義が必須なので、<see cref="SongCredit"/> 側は int のまま持ち、この形では明示実装で渡す。
/// </remarks>
public interface IAffiliatedPersonCredit
{
    /// <summary>名義（→ person_aliases.alias_id）。名義を持たない文字だけの行では null。</summary>
    int? PersonAliasId { get; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    string? PrecedingSeparator { get; }

    /// <summary>名義に添える所属の会社名義（→ company_aliases.alias_id）。</summary>
    int? AffiliationCompanyAliasId { get; }

    /// <summary>会社マスタに無い所属の自由記述。<see cref="AffiliationCompanyAliasId"/> があればそちらを使う。</summary>
    string? AffiliationText { get; }

    /// <summary>所属の出し方（<see cref="SongCreditAffiliationDisplay"/> の値）。</summary>
    string AffiliationDisplay { get; }
}
