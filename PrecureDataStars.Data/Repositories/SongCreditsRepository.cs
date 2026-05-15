using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_credits テーブル（歌の作家連名）の CRUD リポジトリ（v1.2.3 追加 / v1.3.0 ブラッシュアップ続編で role 型変更）。
/// <para>
/// 1 曲（song_id）に対して、credit_role（roles マスタの role_code、典型値は
/// LYRICS / COMPOSITION / ARRANGEMENT）ごとに連名を順序付き（credit_seq）で持つ。
/// <see cref="GetDisplayStringAsync"/> は役単位で連名行を結合し、表示用の 1 行文字列を返す。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ続編で credit_role の型を enum から varchar(32) に変更し、
/// 値も LYRICIST/COMPOSER/ARRANGER → LYRICS/COMPOSITION/ARRANGEMENT にリネームした。
/// それに伴い、本リポジトリ内の enum⇔文字列変換ヘルパは撤廃され、Dapper が直接 string で
/// マップする素直な実装になった。
/// </para>
/// </summary>
public sealed class SongCreditsRepository
{
    private readonly IConnectionFactory _factory;

    public SongCreditsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          song_id              AS SongId,
          credit_role          AS CreditRole,
          credit_seq           AS CreditSeq,
          person_alias_id      AS PersonAliasId,
          preceding_separator  AS PrecedingSeparator,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>
    /// 指定曲の全クレジット行を (role, seq) 順で取得する。
    /// 役の並び順は LYRICS → COMPOSITION → ARRANGEMENT を優先（主題歌の慣習順）、それ以外は role_code 昇順。
    /// </summary>
    public async Task<IReadOnlyList<SongCredit>> GetBySongAsync(int songId, CancellationToken ct = default)
    {
        // 主題歌系の慣習順（作詞 → 作曲 → 編曲）を FIELD でソートに乗せる。
        // 並びに無い役職（運用者が新規定義した役職など）は末尾に来る（FIELD は未マッチで 0 を返すため）。
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_credits
            WHERE song_id = @songId
            ORDER BY FIELD(credit_role,'LYRICS','COMPOSITION','ARRANGEMENT'), credit_role, credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongCredit>(new CommandDefinition(sql, new { songId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定曲・役の連名行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<SongCredit>> GetBySongAndRoleAsync(int songId, string role, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_credits
            WHERE song_id = @songId AND credit_role = @role
            ORDER BY credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongCredit>(new CommandDefinition(sql, new { songId, role }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定曲・役の表示文字列を返す（v1.2.3）。
    /// 行が無ければ空文字。1 行なら名義の表示名そのまま、2 行以上なら
    /// preceding_separator で連結する。表示名は person_aliases.display_text_override が
    /// あればそちらを、なければ name を使う。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int songId, string role, CancellationToken ct = default)
    {
        // 連名と alias 表示名（display_text_override 優先）を 1 ショットで JOIN 取得する。
        const string sql = """
            SELECT
              sc.credit_seq                                              AS Seq,
              sc.preceding_separator                                     AS Sep,
              COALESCE(NULLIF(pa.display_text_override, ''), pa.name)    AS DisplayName
            FROM song_credits sc
            JOIN person_aliases pa ON pa.alias_id = sc.person_alias_id
            WHERE sc.song_id = @songId
              AND sc.credit_role = @role
            ORDER BY sc.credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<(byte Seq, string? Sep, string DisplayName)>(
            new CommandDefinition(sql, new { songId, role }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        // seq=1 は区切りなしで先頭、それ以降は preceding_separator + 名前を連結。
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(rows[i].Sep ?? "");
            sb.Append(rows[i].DisplayName);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 指定曲・役の表示 HTML 文字列を返す（v1.3.1 stage B-4-prep 追加）。
    /// <para>
    /// <see cref="GetDisplayStringAsync"/> の HTML 版。各連名要素を
    /// <see cref="ILookupCache.LookupPersonAliasHtmlAsync(int)"/> でリンク化済み HTML 断片に展開し、
    /// <c>preceding_separator</c> は HtmlEncode した上で間に挟む。
    /// </para>
    /// <para>
    /// SiteBuilder 側 <see cref="ILookupCache"/> 実装ではリンク付き <c>&lt;a href=...&gt;名義&lt;/a&gt;</c> が、
    /// Catalog 側ではプレーンな HtmlEncode 済みテキストが返るため、出力先環境に応じて
    /// 自動的に「リンクあり/なし」の表示が切り替わる。
    /// </para>
    /// <para>
    /// 行が無ければ空文字を返す。クエリ自体は <see cref="GetDisplayStringAsync"/> と同じ JOIN 一発取得を流用するが、
    /// このメソッドでは <c>DisplayName</c> ではなく <c>PersonAliasId</c> を引いて lookup に通すため SELECT 列を変える。
    /// </para>
    /// </summary>
    /// <param name="songId">曲 ID。</param>
    /// <param name="role">役職コード（"LYRICS" / "COMPOSITION" / "ARRANGEMENT" 等）。</param>
    /// <param name="lookup">名義解決インターフェース（SiteBuilder/Catalog 側で実装が切り替わる）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>連名の HTML 文字列。0 件のとき空文字。</returns>
    public async Task<string> GetDisplayHtmlAsync(int songId, string role, ILookupCache lookup, CancellationToken ct = default)
    {
        // 連名行を seq 順で取得し、各行の preceding_separator と person_alias_id を集める。
        // 表示名そのものは lookup 経由で別途引くため、SELECT で取らない（lookup 側でキャッシュ済みを期待）。
        const string sql = """
            SELECT
              sc.credit_seq            AS Seq,
              sc.preceding_separator   AS Sep,
              sc.person_alias_id       AS PersonAliasId
            FROM song_credits sc
            WHERE sc.song_id = @songId
              AND sc.credit_role = @role
            ORDER BY sc.credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<(byte Seq, string? Sep, int PersonAliasId)>(
            new CommandDefinition(sql, new { songId, role }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                // セパレータは生 HTML として挿入されることを避けるため、HtmlEncode で安全化する。
                // 典型値は "、"・"／"・", " など短いテキストで、エスケープ後も視覚的にそのまま読める。
                sb.Append(System.Net.WebUtility.HtmlEncode(rows[i].Sep ?? ""));
            }
            // lookup でリンク化済み HTML を取得。未解決時（alias 削除済み等）は空文字を使い、
            // 表示上は連名要素が欠落するが、レイアウト崩壊は避ける。
            var html = await lookup.LookupPersonAliasHtmlAsync(rows[i].PersonAliasId).ConfigureAwait(false);
            sb.Append(html ?? "");
        }
        return sb.ToString();
    }

    /// <summary>1 行追加（呼び出し側で credit_seq を採番済みの前提）。</summary>
    public async Task InsertAsync(SongCredit c, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_credits
              (song_id, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
            VALUES
              (@SongId, @CreditRole, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, c, cancellationToken: ct));
    }

    /// <summary>1 行更新。</summary>
    public async Task UpdateAsync(SongCredit c, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE song_credits SET
              person_alias_id     = @PersonAliasId,
              preceding_separator = @PrecedingSeparator,
              notes               = @Notes,
              updated_by          = @UpdatedBy
            WHERE song_id     = @SongId
              AND credit_role = @CreditRole
              AND credit_seq  = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, c, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int songId, string role, byte creditSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM song_credits
            WHERE song_id = @SongId AND credit_role = @Role AND credit_seq = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { SongId = songId, Role = role, CreditSeq = creditSeq }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定曲・役の連名行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int songId, string role, IReadOnlyList<SongCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM song_credits WHERE song_id = @SongId AND credit_role = @Role;",
                new { SongId = songId, Role = role },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO song_credits
                      (song_id, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
                    VALUES
                      (@SongId, @Role, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SongId = songId,
                        Role = role,
                        CreditSeq = seq,
                        c.PersonAliasId,
                        // seq=1 では preceding_separator は強制 NULL（CHECK にはしていないが整合性維持のため）
                        PrecedingSeparator = seq == 1 ? null : c.PrecedingSeparator,
                        c.Notes,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy
                    },
                    transaction: tx, cancellationToken: ct));
                seq++;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}