-- ============================================================================
-- マスタ健全性チェック（READ ONLY、SELECT のみ）
-- ============================================================================
-- 各セクションは「件数 0 が理想」の検査。1 件以上ヒットしたら何らかの異常 or
-- 注意要のデータが存在することを意味する。各 SELECT の先頭コメントに
-- 「何を検知するか」「件数 > 0 の意味」「対処の方向性」を明記している。
--
-- 用途:
--   - 定期実行で過去の入力バグの累積を検知
--   - データクリーンアップ後の検証
--   - 怪しい振る舞いに遭遇したときの一次切り分け
--
-- 副作用:
--   - 一切無し（全 SELECT、トランザクション内でも外でも安全に流せる）
--   - DB に書き込む UPDATE/DELETE/INSERT は一切含まない
--
-- セクション構成:
--   A. マスタ遊離検出（紐付き先が全部消えていて宙に浮いている）
--   B. 同名重複検出（誤統合 / 誤分割の兆候）
--   C. キャスティング異常検出（同一キャラに複数声優、共有名義の濫用）
--   D. 補足整合性（entry_kind と紐付け列の不整合）
-- ============================================================================


-- ============================================================================
-- A. マスタ遊離検出
-- ============================================================================

-- A1: person → person_alias_persons から参照されていない孤立人物
--   件数 > 0 の意味: 「person 単独で居て alias が一切付いていない」状態。
--   通常運用では person は必ず person_alias 経由で参照されるため、孤立 person は
--   過去の編集ミスや未完了の登録の残骸の可能性が高い。
--   対処: マスタ管理画面で確認して、不要なら削除 or alias を作って復活させる。
SELECT 'A1 orphan persons' AS check_name, p.person_id, p.full_name
FROM persons p
LEFT JOIN person_alias_persons pap ON pap.person_id = p.person_id
WHERE p.is_deleted = 0 AND pap.alias_id IS NULL
ORDER BY p.person_id;


-- A2: person_alias がどこからも参照されていない孤立 alias
--   件数 > 0 の意味: 「person_aliases 行はあるが、person_alias_persons /
--   credit_block_entries / song_credits / song_recording_singers /
--   bgm_cue_credits からも参照されていない」状態。
--   ただし「後で使う目的で先行登録した名義」は意図的に温存されているため、
--   この結果に出てくるすべてが異常とは限らない。マスタ管理画面で確認して、
--   不要なら削除、温存意図があればそのまま。
SELECT 'A2 orphan person_aliases' AS check_name, pa.alias_id, pa.name
FROM person_aliases pa
WHERE pa.is_deleted = 0
  AND NOT EXISTS (SELECT 1 FROM person_alias_persons   p WHERE p.alias_id              = pa.alias_id)
  AND NOT EXISTS (SELECT 1 FROM credit_block_entries   p WHERE p.person_alias_id       = pa.alias_id)
  AND NOT EXISTS (SELECT 1 FROM song_credits           p WHERE p.person_alias_id       = pa.alias_id)
  AND NOT EXISTS (SELECT 1 FROM song_recording_singers p WHERE p.person_alias_id       = pa.alias_id
                                                            OR p.voice_person_alias_id = pa.alias_id
                                                            OR p.slash_person_alias_id = pa.alias_id)
  AND NOT EXISTS (SELECT 1 FROM bgm_cue_credits        p WHERE p.person_alias_id       = pa.alias_id)
  AND NOT EXISTS (SELECT 1 FROM person_alias_members   p WHERE p.parent_alias_id       = pa.alias_id
                                                            OR p.member_person_alias_id = pa.alias_id)
ORDER BY pa.alias_id;


-- A3: character → character_aliases から参照されていない孤立キャラ
--   件数 > 0 の意味: 「characters 行があるが、配下の alias が 1 件も生きていない」状態。
--   alias が全部論理削除された / 一度も登録されなかった のいずれか。
SELECT 'A3 orphan characters' AS check_name, c.character_id, c.name
FROM characters c
LEFT JOIN character_aliases ca ON ca.character_id = c.character_id AND ca.is_deleted = 0
WHERE c.is_deleted = 0 AND ca.alias_id IS NULL
ORDER BY c.character_id;


-- A4: character_alias がどこからも参照されていない孤立 alias
--   件数 > 0 の意味: 「character_aliases 行はあるが、credit_block_entries /
--   song_recording_singers / precures / person_alias_members からも
--   参照されていない」状態。マスタ汚染の典型パターン。
SELECT 'A4 orphan character_aliases' AS check_name, ca.alias_id, ca.character_id, ca.name
FROM character_aliases ca
WHERE ca.is_deleted = 0
  AND NOT EXISTS (SELECT 1 FROM credit_block_entries   p WHERE p.character_alias_id        = ca.alias_id)
  AND NOT EXISTS (SELECT 1 FROM song_recording_singers p WHERE p.character_alias_id        = ca.alias_id
                                                            OR p.slash_character_alias_id  = ca.alias_id)
  AND NOT EXISTS (SELECT 1 FROM precures               p WHERE p.pre_transform_alias_id    = ca.alias_id
                                                            OR p.transform_alias_id        = ca.alias_id
                                                            OR p.transform2_alias_id       = ca.alias_id
                                                            OR p.alt_form_alias_id         = ca.alias_id)
  AND NOT EXISTS (SELECT 1 FROM person_alias_members   p WHERE p.member_character_alias_id = ca.alias_id)
ORDER BY ca.alias_id;


-- A5: company → company_aliases から参照されていない孤立企業
--   件数 > 0 の意味: 「companies 行があるが、配下の alias が 1 件も生きていない」状態。
SELECT 'A5 orphan companies' AS check_name, c.company_id, c.name
FROM companies c
LEFT JOIN company_aliases ca ON ca.company_id = c.company_id AND ca.is_deleted = 0
WHERE c.is_deleted = 0 AND ca.alias_id IS NULL
ORDER BY c.company_id;


-- A6: company_alias がどこからも参照されていない孤立 alias
--   件数 > 0 の意味: 「company_aliases 行はあるが、logos / credit_block_entries
--   (company_alias_id, affiliation_company_alias_id) / credit_role_blocks
--   (leading_company_alias_id) / product_companies からも参照されていない」状態。
SELECT 'A6 orphan company_aliases' AS check_name, ca.alias_id, ca.company_id, ca.name
FROM company_aliases ca
WHERE ca.is_deleted = 0
  AND NOT EXISTS (SELECT 1 FROM logos                p WHERE p.company_alias_id            = ca.alias_id)
  AND NOT EXISTS (SELECT 1 FROM credit_block_entries p WHERE p.company_alias_id            = ca.alias_id
                                                           OR p.affiliation_company_alias_id = ca.alias_id)
  AND NOT EXISTS (SELECT 1 FROM credit_role_blocks   p WHERE p.leading_company_alias_id     = ca.alias_id)
ORDER BY ca.alias_id;


-- A7: logo がどこからも参照されていない孤立ロゴ
--   件数 > 0 の意味: 「logos 行はあるが、credit_block_entries.logo_id から
--   参照されていない」状態。CI バージョン違いのロゴを先行登録しているケース等で
--   意図的に温存している可能性もあるため、結果は確認の上で判断する。
SELECT 'A7 orphan logos' AS check_name, l.logo_id, l.company_alias_id, ca.name AS company_alias_name, l.ci_version_label
FROM logos l
LEFT JOIN company_aliases ca ON ca.alias_id = l.company_alias_id
WHERE l.is_deleted = 0
  AND NOT EXISTS (SELECT 1 FROM credit_block_entries p WHERE p.logo_id = l.logo_id)
ORDER BY l.logo_id;


-- ============================================================================
-- B. 同名重複検出
-- ============================================================================

-- B1: 同名 person_alias が複数（誤統合の兆候）
--   件数 > 0 の意味: 「同じ name の person_alias が DB に複数存在する」状態。
--   過去の Bulk Apply Pending 重複排除バグの名残や、別人の同姓同名（その場合は
--   person_id が異なる）が混在する。同 person_id の重複は統合対象、別 person_id
--   の同姓同名は alias_id 明示参照記法 (#alias_id) で区別運用する。
SELECT 'B1 duplicate person_aliases by name' AS check_name,
    pa.name,
    COUNT(*) AS dup_count,
    GROUP_CONCAT(pa.alias_id ORDER BY pa.alias_id) AS alias_ids
FROM person_aliases pa
WHERE pa.is_deleted = 0
GROUP BY pa.name
HAVING COUNT(*) > 1
ORDER BY dup_count DESC, pa.name;


-- B2: 同 character_id 内に同名 character_alias が複数
--   件数 > 0 の意味: 「同じ character の配下に同じ name の alias が複数」状態。
--   表記揺れではなく完全同名の重複なら統合対象（マスタクリーニング SQL の対象）。
SELECT 'B2 duplicate character_aliases by (char_id, name)' AS check_name,
    ca.character_id,
    c.name AS char_name,
    ca.name AS alias_name,
    COUNT(*) AS dup_count,
    GROUP_CONCAT(ca.alias_id ORDER BY ca.alias_id) AS alias_ids
FROM character_aliases ca
JOIN characters c ON c.character_id = ca.character_id
WHERE ca.is_deleted = 0
GROUP BY ca.character_id, c.name, ca.name
HAVING COUNT(*) > 1
ORDER BY dup_count DESC, c.name;


-- B3: 同 company_id 内に同名 company_alias が複数
--   件数 > 0 の意味: 「同じ company の配下に同じ name の alias が複数」状態。
--   表記揺れではなく完全同名の重複なら統合対象。
SELECT 'B3 duplicate company_aliases by (company_id, name)' AS check_name,
    ca.company_id,
    c.name AS company_name,
    ca.name AS alias_name,
    COUNT(*) AS dup_count,
    GROUP_CONCAT(ca.alias_id ORDER BY ca.alias_id) AS alias_ids
FROM company_aliases ca
JOIN companies c ON c.company_id = ca.company_id
WHERE ca.is_deleted = 0
GROUP BY ca.company_id, c.name, ca.name
HAVING COUNT(*) > 1
ORDER BY dup_count DESC, c.name;


-- ============================================================================
-- C. キャスティング異常検出
-- ============================================================================

-- C1: 同一キャラに複数の人物（person_id）が声優として紐付いている
--   件数 > 0 の意味: 「同じ character_id の配下にぶら下がる alias 群が、
--   credit_block_entries 経由で複数の person_id にキャスティングされている」状態。
--   通常のプリキュア仕様では「変身前後で声優違い」はあり得ない（あれば代役 / 交代）
--   ため、本検査でヒットしたものは原則異常として扱う。
--   - 真の代役 / 交代（代行収録、声優交代等）：実態として正しい、メモで扱う
--   - 「光の国の住人」型の誤統合（モブを同 character_id に複数声優ぶら下げ）：
--     キャラ分離マイグレーションの対象
SELECT 'C1 multiple person_ids per character' AS check_name,
    c.character_id,
    c.name AS char_name,
    COUNT(DISTINCT pap.person_id) AS distinct_person_count,
    GROUP_CONCAT(DISTINCT p.full_name ORDER BY p.full_name SEPARATOR ' / ') AS person_names
FROM characters c
JOIN character_aliases ca ON ca.character_id = c.character_id AND ca.is_deleted = 0
JOIN credit_block_entries cbe ON cbe.character_alias_id = ca.alias_id
JOIN person_alias_persons pap ON pap.alias_id = cbe.person_alias_id
JOIN persons p ON p.person_id = pap.person_id
WHERE c.is_deleted = 0
GROUP BY c.character_id, c.name
HAVING COUNT(DISTINCT pap.person_id) > 1
ORDER BY distinct_person_count DESC, c.name;


-- C2: 1 person_alias が複数の person_id に紐付いている
--   件数 > 0 の意味: 「同じ name の alias_id が複数 person を指している」状態。
--   ユニット名（複数メンバー共有名義）等は意図的にこの構造を取る運用だが、
--   過去の誤統合や手動編集ミスで「本来別人だった alias が同一視されている」
--   ケースも混じり得る。GROUP_CONCAT 列で並ぶ person 名が同一人物の表記揺れか
--   別人ユニットかを目視判定する。
SELECT 'C2 person_alias bound to multiple persons' AS check_name,
    pap.alias_id,
    pa.name AS alias_name,
    COUNT(DISTINCT pap.person_id) AS person_count,
    GROUP_CONCAT(DISTINCT p.full_name ORDER BY p.full_name SEPARATOR ' / ') AS person_names
FROM person_alias_persons pap
JOIN person_aliases pa ON pa.alias_id = pap.alias_id
JOIN persons p ON p.person_id = pap.person_id
WHERE pa.is_deleted = 0
GROUP BY pap.alias_id, pa.name
HAVING COUNT(DISTINCT pap.person_id) > 1
ORDER BY person_count DESC, pa.name;


-- ============================================================================
-- D. 補足整合性
-- ============================================================================

-- D1: credit_block_entries の entry_kind と紐付け列の不整合
--   件数 > 0 の意味: 「entry_kind が示すレコード種別と、対応する *_alias_id /
--   logo_id / raw_text の NULL/NOT NULL 状態が矛盾している」状態。
--   DB トリガで弾かれているはずだが、トリガを無効化した過去マイグレーション
--   等で漏れていれば検出される。
SELECT 'D1 entry_kind / nullability mismatch' AS check_name,
    cbe.entry_id,
    cbe.entry_kind,
    cbe.person_alias_id,
    cbe.character_alias_id,
    cbe.company_alias_id,
    cbe.logo_id,
    cbe.raw_text
FROM credit_block_entries cbe
WHERE
    (cbe.entry_kind = 'PERSON'          AND cbe.person_alias_id IS NULL)
 OR (cbe.entry_kind = 'CHARACTER_VOICE' AND (cbe.person_alias_id IS NULL OR (cbe.character_alias_id IS NULL AND cbe.raw_character_text IS NULL)))
 OR (cbe.entry_kind = 'COMPANY'         AND cbe.company_alias_id IS NULL)
 OR (cbe.entry_kind = 'LOGO'            AND cbe.logo_id IS NULL)
 OR (cbe.entry_kind = 'TEXT'            AND cbe.raw_text IS NULL)
ORDER BY cbe.entry_id;


-- ============================================================================
-- 全件カウントサマリ（最後に実行すると一覧で異常箇所がわかる）
-- ============================================================================
-- 各検査の HIT 数を 1 行ずつ表示。0 が並んでいれば全部健全。
SELECT 'A1 orphan persons'                          AS check_name, COUNT(*) AS hit FROM persons p LEFT JOIN person_alias_persons pap ON pap.person_id = p.person_id WHERE p.is_deleted = 0 AND pap.alias_id IS NULL
UNION ALL SELECT 'A2 orphan person_aliases',           COUNT(*) FROM person_aliases pa
    WHERE pa.is_deleted = 0
      AND NOT EXISTS (SELECT 1 FROM person_alias_persons   p WHERE p.alias_id              = pa.alias_id)
      AND NOT EXISTS (SELECT 1 FROM credit_block_entries   p WHERE p.person_alias_id       = pa.alias_id)
      AND NOT EXISTS (SELECT 1 FROM song_credits           p WHERE p.person_alias_id       = pa.alias_id)
      AND NOT EXISTS (SELECT 1 FROM song_recording_singers p WHERE p.person_alias_id       = pa.alias_id OR p.voice_person_alias_id = pa.alias_id OR p.slash_person_alias_id = pa.alias_id)
      AND NOT EXISTS (SELECT 1 FROM bgm_cue_credits        p WHERE p.person_alias_id       = pa.alias_id)
      AND NOT EXISTS (SELECT 1 FROM person_alias_members   p WHERE p.parent_alias_id       = pa.alias_id OR p.member_person_alias_id = pa.alias_id)
UNION ALL SELECT 'A3 orphan characters',               COUNT(*) FROM characters c LEFT JOIN character_aliases ca ON ca.character_id = c.character_id AND ca.is_deleted = 0 WHERE c.is_deleted = 0 AND ca.alias_id IS NULL
UNION ALL SELECT 'A4 orphan character_aliases',        COUNT(*) FROM character_aliases ca
    WHERE ca.is_deleted = 0
      AND NOT EXISTS (SELECT 1 FROM credit_block_entries   p WHERE p.character_alias_id        = ca.alias_id)
      AND NOT EXISTS (SELECT 1 FROM song_recording_singers p WHERE p.character_alias_id        = ca.alias_id OR p.slash_character_alias_id = ca.alias_id)
      AND NOT EXISTS (SELECT 1 FROM precures               p WHERE p.pre_transform_alias_id    = ca.alias_id OR p.transform_alias_id = ca.alias_id OR p.transform2_alias_id = ca.alias_id OR p.alt_form_alias_id = ca.alias_id)
      AND NOT EXISTS (SELECT 1 FROM person_alias_members   p WHERE p.member_character_alias_id = ca.alias_id)
UNION ALL SELECT 'A5 orphan companies',                COUNT(*) FROM companies c LEFT JOIN company_aliases ca ON ca.company_id = c.company_id AND ca.is_deleted = 0 WHERE c.is_deleted = 0 AND ca.alias_id IS NULL
UNION ALL SELECT 'A6 orphan company_aliases',          COUNT(*) FROM company_aliases ca
    WHERE ca.is_deleted = 0
      AND NOT EXISTS (SELECT 1 FROM logos                p WHERE p.company_alias_id            = ca.alias_id)
      AND NOT EXISTS (SELECT 1 FROM credit_block_entries p WHERE p.company_alias_id            = ca.alias_id OR p.affiliation_company_alias_id = ca.alias_id)
      AND NOT EXISTS (SELECT 1 FROM credit_role_blocks   p WHERE p.leading_company_alias_id     = ca.alias_id)
UNION ALL SELECT 'A7 orphan logos',                    COUNT(*) FROM logos l WHERE l.is_deleted = 0 AND NOT EXISTS (SELECT 1 FROM credit_block_entries p WHERE p.logo_id = l.logo_id)
UNION ALL SELECT 'B1 duplicate person_aliases',        (SELECT COUNT(*) FROM (SELECT 1 FROM person_aliases pa WHERE pa.is_deleted = 0 GROUP BY pa.name HAVING COUNT(*) > 1) t)
UNION ALL SELECT 'B2 duplicate character_aliases',     (SELECT COUNT(*) FROM (SELECT 1 FROM character_aliases ca WHERE ca.is_deleted = 0 GROUP BY ca.character_id, ca.name HAVING COUNT(*) > 1) t)
UNION ALL SELECT 'B3 duplicate company_aliases',       (SELECT COUNT(*) FROM (SELECT 1 FROM company_aliases ca WHERE ca.is_deleted = 0 GROUP BY ca.company_id, ca.name HAVING COUNT(*) > 1) t)
UNION ALL SELECT 'C1 multi-person per character',      (SELECT COUNT(*) FROM (SELECT 1 FROM characters c JOIN character_aliases ca ON ca.character_id = c.character_id AND ca.is_deleted = 0 JOIN credit_block_entries cbe ON cbe.character_alias_id = ca.alias_id JOIN person_alias_persons pap ON pap.alias_id = cbe.person_alias_id WHERE c.is_deleted = 0 GROUP BY c.character_id HAVING COUNT(DISTINCT pap.person_id) > 1) t)
UNION ALL SELECT 'C2 person_alias multi-persons',      (SELECT COUNT(*) FROM (SELECT 1 FROM person_alias_persons pap JOIN person_aliases pa ON pa.alias_id = pap.alias_id WHERE pa.is_deleted = 0 GROUP BY pap.alias_id HAVING COUNT(DISTINCT pap.person_id) > 1) t)
UNION ALL SELECT 'D1 entry_kind mismatch',             COUNT(*) FROM credit_block_entries cbe WHERE
       (cbe.entry_kind = 'PERSON'          AND cbe.person_alias_id IS NULL)
    OR (cbe.entry_kind = 'CHARACTER_VOICE' AND (cbe.person_alias_id IS NULL OR (cbe.character_alias_id IS NULL AND cbe.raw_character_text IS NULL)))
    OR (cbe.entry_kind = 'COMPANY'         AND cbe.company_alias_id IS NULL)
    OR (cbe.entry_kind = 'LOGO'            AND cbe.logo_id IS NULL)
    OR (cbe.entry_kind = 'TEXT'            AND cbe.raw_text IS NULL);
