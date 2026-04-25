using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.CsvImport;

/// <summary>
/// 劇伴マスタ（bgm_cues）用の CSV 取り込みサービス（v1.1.3 追加）。
/// <para>
/// 期待する CSV ヘッダ（UTF-8、カンマ区切り、ヘッダ行必須）:
/// <code>
/// series_title_short,m_no_detail,session_name,m_no_class,menu_title,composer_name,composer_name_kana,arranger_name,arranger_name_kana,length_seconds,is_temp_m_no,notes
/// </code>
/// </para>
/// <para>
/// 動作:
/// <list type="bullet">
///   <item><c>series_title_short</c>: <c>series.title_short</c> で突き合わせ、無ければ <c>title</c> 部分一致。未解決行はスキップ（警告付与）</item>
///   <item><c>session_name</c>: シリーズ内の <c>bgm_sessions</c> を <c>session_name</c> 完全一致で検索。無ければ同シリーズ内で新規採番（既存最大 session_no + 1）して追加する</item>
///   <item><c>m_no_detail</c> 空欄時: <c>is_temp_m_no</c> 列が真相当なら自動で <c>_temp_NNNNNN</c> を採番、偽なら行スキップ</item>
///   <item>UPSERT: <c>(series_id, m_no_detail)</c> の既存行があれば更新、無ければ追加</item>
///   <item><c>is_temp_m_no</c> は <c>1</c> / <c>true</c> / <c>yes</c> を真とみなす（大文字小文字無視）。既定 <c>0</c></item>
///   <item><c>length_seconds</c> が数値にパース不可なら NULL（警告付与）</item>
/// </list>
/// </para>
/// </summary>
public sealed class BgmCueCsvImportService
{
    private readonly BgmCuesRepository _cuesRepo;
    private readonly BgmSessionsRepository _sessionsRepo;
    private readonly SeriesRepository _seriesRepo;

    public BgmCueCsvImportService(
        BgmCuesRepository cuesRepo,
        BgmSessionsRepository sessionsRepo,
        SeriesRepository seriesRepo)
    {
        _cuesRepo = cuesRepo ?? throw new ArgumentNullException(nameof(cuesRepo));
        _sessionsRepo = sessionsRepo ?? throw new ArgumentNullException(nameof(sessionsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
    }

    /// <summary>CSV インポート結果サマリ。</summary>
    public sealed record Result(int Inserted, int Updated, int Skipped, int SessionsCreated, IReadOnlyList<string> Warnings);

    /// <summary>
    /// CSV を取り込む。<paramref name="dryRun"/>=true で実際の書き込みを抑止（件数だけ集計）。
    /// </summary>
    public async Task<Result> ImportAsync(string csvPath, string operatorName, bool dryRun = false, CancellationToken ct = default)
    {
        var (_, rows) = SimpleCsvReader.ReadFile(csvPath);
        var warnings = new List<string>();
        int inserted = 0, updated = 0, skipped = 0, sessionsCreated = 0;

        // マスタ先行ロード
        var allSeries = (await _seriesRepo.GetAllAsync(ct)).ToList();
        // sessionsBySeries: series_id → そのシリーズの全セッション一覧（Name/No）
        var sessionsBySeries = new Dictionary<int, List<BgmSession>>();
        foreach (var g in (await _sessionsRepo.GetAllAsync(ct)).GroupBy(s => s.SeriesId))
        {
            sessionsBySeries[g.Key] = g.OrderBy(x => x.SessionNo).ToList();
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int csvLineNo = i + 2;

            // シリーズ解決
            string seriesKey = Get(row, "series_title_short").Trim();
            if (string.IsNullOrEmpty(seriesKey))
            {
                warnings.Add($"L{csvLineNo}: series_title_short が空のためスキップします。");
                skipped++; continue;
            }
            var seriesHit = allSeries.FirstOrDefault(s =>
                                string.Equals(s.TitleShort, seriesKey, StringComparison.OrdinalIgnoreCase))
                            ?? allSeries.FirstOrDefault(s =>
                                !string.IsNullOrEmpty(s.Title) && s.Title.Contains(seriesKey, StringComparison.OrdinalIgnoreCase));
            if (seriesHit is null)
            {
                warnings.Add($"L{csvLineNo}: シリーズ '{seriesKey}' が見つかりませんでした。スキップします。");
                skipped++; continue;
            }
            int seriesId = seriesHit.SeriesId;

            // セッション解決（無ければ自動追加）
            string sessionName = Get(row, "session_name").Trim();
            byte sessionNo = 1; // 既定：1 番目のセッション
            if (!sessionsBySeries.TryGetValue(seriesId, out var sessList))
            {
                sessList = new List<BgmSession>();
                sessionsBySeries[seriesId] = sessList;
            }
            if (!string.IsNullOrEmpty(sessionName))
            {
                var sessHit = sessList.FirstOrDefault(s =>
                    string.Equals(s.SessionName, sessionName, StringComparison.Ordinal));
                if (sessHit is not null)
                {
                    sessionNo = sessHit.SessionNo;
                }
                else
                {
                    // シリーズ内で次番号を採番して新規作成。
                    // BgmSessionsRepository.InsertNextAsync は「シリーズ内の MAX(session_no) + 1 を採番 → INSERT」
                    // をトランザクション内で行い、確定した session_no を返す。CSV 取り込み中の同シリーズ内重複採番を
                    // 防ぐため、ローカルキャッシュ sessList へも採番値とともに即時追加する。
                    byte nextNo;
                    if (!dryRun)
                    {
                        nextNo = await _sessionsRepo.InsertNextAsync(
                            seriesId, sessionName, notes: null, createdBy: operatorName, ct);
                    }
                    else
                    {
                        // DRY-RUN ではキャッシュのみで採番予測（実 INSERT は行わない）。
                        nextNo = (byte)((sessList.Count == 0 ? 0 : sessList.Max(x => x.SessionNo)) + 1);
                    }
                    var newSess = new BgmSession
                    {
                        SeriesId = seriesId,
                        SessionNo = nextNo,
                        SessionName = sessionName,
                        CreatedBy = operatorName,
                        UpdatedBy = operatorName
                    };
                    sessList.Add(newSess);
                    sessionNo = nextNo;
                    sessionsCreated++;
                }
            }
            else if (sessList.Count > 0)
            {
                // session_name 未指定なら既存セッションの最小番号（通常は 1）を採用
                sessionNo = sessList.Min(s => s.SessionNo);
            }
            else
            {
                // シリーズにセッションが全く無い状態で session_name も無指定な場合、
                // 既定セッション名 "default" を 1 番で作成する（採番 A 案に沿う）。
                // 上記分岐と同じ理由で InsertNextAsync を使い、確定した session_no を採番値として採る。
                byte assignedNo;
                if (!dryRun)
                {
                    assignedNo = await _sessionsRepo.InsertNextAsync(
                        seriesId, sessionName: "default", notes: "CSV 取り込み時に自動生成",
                        createdBy: operatorName, ct);
                }
                else
                {
                    assignedNo = 1;
                }
                var defaultSess = new BgmSession
                {
                    SeriesId = seriesId,
                    SessionNo = assignedNo,
                    SessionName = "default",
                    Notes = "CSV 取り込み時に自動生成",
                    CreatedBy = operatorName,
                    UpdatedBy = operatorName
                };
                sessList.Add(defaultSess);
                sessionNo = assignedNo;
                sessionsCreated++;
                warnings.Add($"L{csvLineNo}: シリーズ '{seriesKey}' にセッションが無かったため既定セッション 'default' を自動作成しました。");
            }

            // is_temp_m_no 判定
            bool isTemp = ParseBool(Get(row, "is_temp_m_no"));

            // m_no_detail（空のときは is_temp=true なら自動採番、それ以外はスキップ）
            string mNoDetail = Get(row, "m_no_detail").Trim();
            if (string.IsNullOrEmpty(mNoDetail))
            {
                if (!isTemp)
                {
                    warnings.Add($"L{csvLineNo}: m_no_detail が空で is_temp_m_no フラグも立っていません。スキップします。");
                    skipped++; continue;
                }
                // 仮番号を採番（DRY-RUN 時も採番して表示整合性を保つ）
                mNoDetail = await _cuesRepo.GenerateNextTempMNoAsync(seriesId, ct);
            }

            // 既存行があるか（UPSERT 判定）
            var existing = await _cuesRepo.GetAsync(seriesId, mNoDetail, ct);
            bool isUpdate = existing is not null;

            // 尺（秒）。数値化できなければ NULL。
            ushort? lengthSeconds = null;
            string lenRaw = Get(row, "length_seconds").Trim();
            if (!string.IsNullOrEmpty(lenRaw))
            {
                if (ushort.TryParse(lenRaw, out ushort parsed))
                {
                    lengthSeconds = parsed;
                }
                else
                {
                    warnings.Add($"L{csvLineNo}: length_seconds='{lenRaw}' を数値に解釈できません。NULL として登録します。");
                }
            }

            var cue = new BgmCue
            {
                SeriesId = seriesId,
                MNoDetail = mNoDetail,
                SessionNo = sessionNo,
                MNoClass = NullIfEmpty(Get(row, "m_no_class")),
                MenuTitle = NullIfEmpty(Get(row, "menu_title")),
                ComposerName = NullIfEmpty(Get(row, "composer_name")),
                ComposerNameKana = NullIfEmpty(Get(row, "composer_name_kana")),
                ArrangerName = NullIfEmpty(Get(row, "arranger_name")),
                ArrangerNameKana = NullIfEmpty(Get(row, "arranger_name_kana")),
                LengthSeconds = lengthSeconds,
                Notes = NullIfEmpty(Get(row, "notes")),
                IsTempMNo = isTemp,
                CreatedBy = operatorName,
                UpdatedBy = operatorName
            };

            if (dryRun)
            {
                if (isUpdate) updated++; else inserted++;
                continue;
            }

            await _cuesRepo.UpsertAsync(cue, ct);
            if (isUpdate) updated++; else inserted++;
        }

        return new Result(inserted, updated, skipped, sessionsCreated, warnings);
    }

    /// <summary>CSV 行辞書からキー取得。存在しなければ空文字。</summary>
    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v : "";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// 真偽値の緩めパース。"1" / "true" / "yes" / "y" / "t"（大小無視）を真として扱い、それ以外は偽。
    /// </summary>
    private static bool ParseBool(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim().ToLowerInvariant();
        return t is "1" or "true" or "yes" or "y" or "t";
    }
}
