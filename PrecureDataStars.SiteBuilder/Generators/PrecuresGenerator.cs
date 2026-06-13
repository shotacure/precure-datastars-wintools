using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// プリキュア索引（<c>/precures/</c>）のみを生成する。プリキュア詳細ページは廃止し、
/// 詳細閲覧はキャラクター詳細（<c>/characters/{character_id}/</c>）に統合済み
/// （PRECURE 種別キャラの詳細ページ内「プリキュア情報」セクションで 4 区分名義 /
/// 学校 / 学年・組 / 家業 / 専属声優の全情報を担う）。
/// 索引行のリンク先 character_id は precures.transform_alias_id 経由で逆引きする。
/// </summary>
public sealed class PrecuresGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly PersonsRepository _personsRepo;

    public PrecuresGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;

        _precuresRepo = new PrecuresRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating precures");

        // 索引用にプリキュア + 解決辞書を 1 度だけロード。
        var allPrecures = (await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        var characterAliasById = allCharacterAliases.ToDictionary(a => a.AliasId);
        var personsById = allPersons.ToDictionary(p => p.PersonId);

        // 索引ページのみ。詳細はキャラクター詳細に統合済み。
        GenerateIndex(allPrecures, characterAliasById, personsById);

        _ctx.Logger.Success($"precures: 1 ページ");
    }

    /// <summary>
    /// <c>/precures/</c>（プリキュア索引）。precure_id 昇順 = 概ね登場年代順。
    /// 各行のリンク先は <c>/characters/{character_id}/</c>（プリキュア詳細は廃止済み）。
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Precure> precures,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Person> personsById)
    {
        var rows = precures
            .OrderBy(p => p.PrecureId)
            .Select(p => new PrecureIndexRow
            {
                PrecureId = p.PrecureId,
                // 詳細リンク先となる character_id は変身後名義から解決する。
                CharacterId = aliasById.TryGetValue(p.TransformAliasId, out var trAlias) ? trAlias.CharacterId : 0,
                TransformName = aliasById.TryGetValue(p.TransformAliasId, out var trans) ? trans.Name : "",
                PreTransformName = aliasById.TryGetValue(p.PreTransformAliasId, out var pre) ? pre.Name : "",
                VoiceActorName = (p.VoiceActorPersonId is int vid && personsById.TryGetValue(vid, out var v))
                    ? v.FullName : "",
                VoiceActorPersonId = p.VoiceActorPersonId
            })
            .ToList();

        var content = new PrecuresIndexModel
        {
            Precures = rows,
            TotalCount = rows.Count,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアオールスターズ",
            MetaDescription = "初代から最新作まで、歴代の変身ヒロイン（プリキュア）を登場順に一覧にしました。変身前後の名前や担当声優をまとめた、オールスターズ名鑑です。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュアオールスターズ", Url = "" }
            }
        };
        _page.RenderAndWrite("/precures/", "precures", "precures-index.sbn", content, layout);
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class PrecuresIndexModel
    {
        public IReadOnlyList<PrecureIndexRow> Precures { get; set; } = Array.Empty<PrecureIndexRow>();
        public int TotalCount { get; set; }
        /// <summary>クレジット横断カバレッジラベル。 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記を テンプレ側の lead 段落末尾に表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class PrecureIndexRow
    {
        public int PrecureId { get; set; }
        /// <summary>リンク先となる character_id（precures.transform_alias_id 経由で解決済み）。 /characters/{CharacterId}/ がプリキュア詳細を兼ねる。</summary>
        public int CharacterId { get; set; }
        public string TransformName { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
    }
}
