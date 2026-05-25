-- ============================================================================
-- マスタクリーニング: 完全重複統合 + 飛び番再採番
-- ============================================================================
-- 対象テーブル:
--   persons / person_aliases / characters / character_aliases / companies / company_aliases
--
-- 副作用:
--   フェーズ 1: 完全重複行を「参照されてない側」だけ消す形で統合
--     - 同名グループのうち、片方に参照あり片方に参照なし → 参照なし側を DELETE、参照あり側を残す
--     - 全部に参照あり → 最小 ID を keeper にして他の参照を keeper に張り替え、loser を DELETE
--     - 全部に参照なし → 何もしない（手動入力の温存）
--   フェーズ 2: ID を 1, 2, 3, ... に再採番（関連 FK も全部書き換え + AUTO_INCREMENT リセット）
--
-- 実行手順:
--   1. 必ず事前にバックアップ（mysqldump 等）を取る
--   2. このスクリプトを 1 トランザクションで流す（最後の COMMIT を実行するまで全部 ROLLBACK 可能）
--   3. 結果を確認し、問題なければ COMMIT、問題があれば ROLLBACK
--
-- 設計メモ:
--   - SET FOREIGN_KEY_CHECKS = 0 で FK 制約を一時無効化（ON UPDATE NO ACTION な FK もあるため）
--   - 重複判定キーは以下:
--       persons          : (full_name, family_name, given_name, full_name_kana, name_en)
--       person_aliases   : (name, name_kana, name_en, display_text_override, person_id_via_pap)
--       characters       : (name, name_kana, character_kind)
--       character_aliases: (character_id, name, name_kana, name_en)
--       companies        : (name, name_kana, name_en)
--       company_aliases  : (company_id, name, name_kana, name_en)
--   - ID 再採番は「親テーブルの ID を負数経由で新値に置換 → 全 FK 列も同期更新」の流れ
-- ============================================================================

START TRANSACTION;
SET FOREIGN_KEY_CHECKS = 0;
-- MySQL Workbench の safe update mode（KEY 列を含む WHERE 無しの UPDATE/DELETE をブロック）を一時無効化。
-- JOIN ベースの一括更新は KEY 列条件を明示しないため、これが有効だと Error 1175 で全停止する。
SET SQL_SAFE_UPDATES = 0;

-- ============================================================================
-- フェーズ 1.1: person_aliases の重複統合
-- ============================================================================

-- 1.1a 参照されている person_alias_id を集計
DROP TEMPORARY TABLE IF EXISTS _used_pa;
CREATE TEMPORARY TABLE _used_pa (alias_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT alias_id FROM person_alias_persons;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT person_alias_id FROM credit_block_entries WHERE person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT person_alias_id FROM song_credits WHERE person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT person_alias_id FROM song_recording_singers WHERE person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT voice_person_alias_id FROM song_recording_singers WHERE voice_person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT slash_person_alias_id FROM song_recording_singers WHERE slash_person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT person_alias_id FROM bgm_cue_credits WHERE person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT parent_alias_id FROM person_alias_members WHERE parent_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT member_person_alias_id FROM person_alias_members WHERE member_person_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT predecessor_alias_id FROM person_aliases WHERE predecessor_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_pa
  SELECT DISTINCT successor_alias_id FROM person_aliases WHERE successor_alias_id IS NOT NULL;

-- 1.1b 「主人物 person_id」を引いた拡張ビュー（同名でも別人物なら別グループ）
DROP TEMPORARY TABLE IF EXISTS _pa_extended;
CREATE TEMPORARY TABLE _pa_extended (
  alias_id INT PRIMARY KEY,
  name VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255), dto_n VARCHAR(255),
  primary_person_id INT,
  is_used TINYINT
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
INSERT INTO _pa_extended
SELECT
  pa.alias_id,
  pa.name,
  IFNULL(pa.name_kana,''),
  IFNULL(pa.name_en,''),
  IFNULL(pa.display_text_override,''),
  (SELECT person_id FROM person_alias_persons p WHERE p.alias_id = pa.alias_id ORDER BY p.person_seq LIMIT 1),
  CASE WHEN EXISTS (SELECT 1 FROM _used_pa u WHERE u.alias_id = pa.alias_id) THEN 1 ELSE 0 END
FROM person_aliases pa
WHERE pa.is_deleted = 0;

-- 1.1c 重複グループの keeper を決定（参照ありの最小 ID、無ければ全体最小）
DROP TEMPORARY TABLE IF EXISTS _pa_groups;
CREATE TEMPORARY TABLE _pa_groups (
  name VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255), dto_n VARCHAR(255),
  primary_person_id INT,
  keeper_id INT,
  dup_count INT,
  used_count INT
  -- PRIMARY KEY (name, kana_n, en_n, dto_n, primary_person_id) は utf8mb4 で 3072 bytes
  -- 上限を超えるため撤廃。一時テーブルなので JOIN 性能は二の次でよい。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
INSERT INTO _pa_groups
SELECT
  name, kana_n, en_n, dto_n,
  IFNULL(primary_person_id, -1) AS primary_person_id,
  COALESCE(MIN(CASE WHEN is_used = 1 THEN alias_id END), MIN(alias_id)) AS keeper_id,
  COUNT(*) AS dup_count,
  SUM(is_used) AS used_count
FROM _pa_extended
GROUP BY name, kana_n, en_n, dto_n, IFNULL(primary_person_id, -1)
HAVING COUNT(*) > 1;

-- 1.1d loser → keeper のマップ（参照ありを統合 + 参照なしを keeper に揃える対象）
DROP TEMPORARY TABLE IF EXISTS _pa_id_map;
CREATE TEMPORARY TABLE _pa_id_map (
  old_id INT PRIMARY KEY,
  new_id INT NOT NULL,
  loser_is_used TINYINT NOT NULL
);
INSERT INTO _pa_id_map
SELECT pe.alias_id, g.keeper_id, pe.is_used
FROM _pa_extended pe
JOIN _pa_groups g
  ON pe.name = g.name AND pe.kana_n = g.kana_n AND pe.en_n = g.en_n AND pe.dto_n = g.dto_n
  AND IFNULL(pe.primary_person_id, -1) = g.primary_person_id
WHERE pe.alias_id != g.keeper_id
  AND g.used_count > 0;  -- 全部参照なしのグループは温存（map に入れない）

-- 1.1e 「参照あり loser」の参照を keeper に張り替え（FK CASCADE が効かない NO ACTION 系も含めて明示）
-- MySQL の TEMPORARY TABLE は同一クエリ内で 2 回以上参照できない（Error 1137）ため JOIN 構文を使う。
UPDATE person_alias_persons   p JOIN _pa_id_map m ON p.alias_id              = m.old_id SET p.alias_id              = m.new_id;
UPDATE person_aliases         p JOIN _pa_id_map m ON p.predecessor_alias_id  = m.old_id SET p.predecessor_alias_id  = m.new_id;
UPDATE person_aliases         p JOIN _pa_id_map m ON p.successor_alias_id    = m.old_id SET p.successor_alias_id    = m.new_id;
UPDATE person_alias_members   p JOIN _pa_id_map m ON p.parent_alias_id       = m.old_id SET p.parent_alias_id       = m.new_id;
UPDATE person_alias_members   p JOIN _pa_id_map m ON p.member_person_alias_id= m.old_id SET p.member_person_alias_id= m.new_id;
UPDATE credit_block_entries   p JOIN _pa_id_map m ON p.person_alias_id       = m.old_id SET p.person_alias_id       = m.new_id;
UPDATE song_credits           p JOIN _pa_id_map m ON p.person_alias_id       = m.old_id SET p.person_alias_id       = m.new_id;
UPDATE song_recording_singers p JOIN _pa_id_map m ON p.person_alias_id       = m.old_id SET p.person_alias_id       = m.new_id;
UPDATE song_recording_singers p JOIN _pa_id_map m ON p.voice_person_alias_id = m.old_id SET p.voice_person_alias_id = m.new_id;
UPDATE song_recording_singers p JOIN _pa_id_map m ON p.slash_person_alias_id = m.old_id SET p.slash_person_alias_id = m.new_id;
UPDATE bgm_cue_credits        p JOIN _pa_id_map m ON p.person_alias_id       = m.old_id SET p.person_alias_id       = m.new_id;

-- 1.1f loser 行を DELETE（person_alias_persons の重複行も同時にお片付け）
DELETE FROM person_alias_persons WHERE NOT EXISTS (SELECT 1 FROM person_aliases pa WHERE pa.alias_id = person_alias_persons.alias_id);
-- person_alias_persons の (alias_id, person_id) 重複を排除（INSERT IGNORE 動作と同等を後追い）
DELETE p1 FROM person_alias_persons p1
JOIN person_alias_persons p2
  ON p1.alias_id = p2.alias_id AND p1.person_id = p2.person_id AND p1.person_seq > p2.person_seq;
DELETE FROM person_aliases WHERE alias_id IN (SELECT old_id FROM _pa_id_map);


-- ============================================================================
-- フェーズ 1.2: persons の重複統合
-- ============================================================================

DROP TEMPORARY TABLE IF EXISTS _used_p;
CREATE TEMPORARY TABLE _used_p (person_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_p
  SELECT DISTINCT person_id FROM person_alias_persons;
INSERT IGNORE INTO _used_p
  SELECT DISTINCT voice_actor_person_id FROM precures WHERE voice_actor_person_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _p_groups;
CREATE TEMPORARY TABLE _p_groups (
  full_name VARCHAR(255), family_n VARCHAR(255), given_n VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255),
  keeper_id INT,
  dup_count INT,
  used_count INT
  -- PK は utf8mb4 で 3072 bytes 超過するため撤廃。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
-- 一時テーブル _used_p は LEFT JOIN で 1 度だけ参照（Error 1137 回避）
-- 本テーブル persons の列 collation が混在しているため、GROUP BY と JOIN の文字列比較は
-- 明示的に utf8mb4_ja_0900_as_cs_ks に揃える（Error 1267 回避 + 厳格マッチ確保）
INSERT INTO _p_groups
SELECT
  p.full_name,
  IFNULL(p.family_name,''),
  IFNULL(p.given_name,''),
  IFNULL(p.full_name_kana,''),
  IFNULL(p.name_en,''),
  COALESCE(
    MIN(CASE WHEN u.person_id IS NOT NULL THEN p.person_id END),
    MIN(p.person_id)
  ) AS keeper_id,
  COUNT(*) AS dup_count,
  SUM(CASE WHEN u.person_id IS NOT NULL THEN 1 ELSE 0 END) AS used_count
FROM persons p
LEFT JOIN _used_p u ON u.person_id = p.person_id
WHERE p.is_deleted = 0
-- GROUP BY は元テーブル列の collation で grouping させる（SELECT と式を揃えるため COLLATE を付けない。
-- ONLY_FULL_GROUP_BY と機能的従属性を壊さない）。collation 統一は次段の JOIN 比較側で行う。
GROUP BY p.full_name, IFNULL(p.family_name,''), IFNULL(p.given_name,''), IFNULL(p.full_name_kana,''), IFNULL(p.name_en,'')
HAVING COUNT(*) > 1;

DROP TEMPORARY TABLE IF EXISTS _p_id_map;
CREATE TEMPORARY TABLE _p_id_map (
  old_id INT PRIMARY KEY,
  new_id INT NOT NULL
);
INSERT INTO _p_id_map
SELECT p.person_id, g.keeper_id
FROM persons p
JOIN _p_groups g
  ON p.full_name                  COLLATE utf8mb4_ja_0900_as_cs_ks = g.full_name
  AND IFNULL(p.family_name,'')    COLLATE utf8mb4_ja_0900_as_cs_ks = g.family_n
  AND IFNULL(p.given_name,'')     COLLATE utf8mb4_ja_0900_as_cs_ks = g.given_n
  AND IFNULL(p.full_name_kana,'') COLLATE utf8mb4_ja_0900_as_cs_ks = g.kana_n
  AND IFNULL(p.name_en,'')        COLLATE utf8mb4_ja_0900_as_cs_ks = g.en_n
WHERE p.person_id != g.keeper_id
  AND g.used_count > 0;

-- JOIN 構文（Error 1137 回避）
UPDATE person_alias_persons p JOIN _p_id_map m ON p.person_id            = m.old_id SET p.person_id            = m.new_id;
UPDATE precures             p JOIN _p_id_map m ON p.voice_actor_person_id= m.old_id SET p.voice_actor_person_id= m.new_id;

-- 中間表の (alias_id, person_id) 重複を排除
DELETE p1 FROM person_alias_persons p1
JOIN person_alias_persons p2
  ON p1.alias_id = p2.alias_id AND p1.person_id = p2.person_id AND p1.person_seq > p2.person_seq;

DELETE FROM persons WHERE person_id IN (SELECT old_id FROM _p_id_map);


-- ============================================================================
-- フェーズ 1.3: character_aliases の重複統合
-- ============================================================================

DROP TEMPORARY TABLE IF EXISTS _used_ca;
CREATE TEMPORARY TABLE _used_ca (alias_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT character_alias_id FROM credit_block_entries WHERE character_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT character_alias_id FROM song_recording_singers WHERE character_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT slash_character_alias_id FROM song_recording_singers WHERE slash_character_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT pre_transform_alias_id FROM precures WHERE pre_transform_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT transform_alias_id FROM precures WHERE transform_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT transform2_alias_id FROM precures WHERE transform2_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT alt_form_alias_id FROM precures WHERE alt_form_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_ca
  SELECT DISTINCT member_character_alias_id FROM person_alias_members WHERE member_character_alias_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _ca_groups;
CREATE TEMPORARY TABLE _ca_groups (
  character_id INT, name VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255),
  keeper_id INT,
  dup_count INT, used_count INT
  -- PK は utf8mb4 で 3072 bytes 超過するため撤廃。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
-- _used_ca は LEFT JOIN で 1 度だけ参照、文字列比較は明示 COLLATE で揃える
INSERT INTO _ca_groups
SELECT
  ca.character_id, ca.name,
  IFNULL(ca.name_kana,''), IFNULL(ca.name_en,''),
  COALESCE(
    MIN(CASE WHEN u.alias_id IS NOT NULL THEN ca.alias_id END),
    MIN(ca.alias_id)
  ),
  COUNT(*), SUM(CASE WHEN u.alias_id IS NOT NULL THEN 1 ELSE 0 END)
FROM character_aliases ca
LEFT JOIN _used_ca u ON u.alias_id = ca.alias_id
WHERE ca.is_deleted = 0
GROUP BY ca.character_id, ca.name, IFNULL(ca.name_kana,''), IFNULL(ca.name_en,'')
HAVING COUNT(*) > 1;

DROP TEMPORARY TABLE IF EXISTS _ca_id_map;
CREATE TEMPORARY TABLE _ca_id_map (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _ca_id_map
SELECT ca.alias_id, g.keeper_id
FROM character_aliases ca
JOIN _ca_groups g
  ON ca.character_id              = g.character_id
  AND ca.name                     COLLATE utf8mb4_ja_0900_as_cs_ks = g.name
  AND IFNULL(ca.name_kana,'')     COLLATE utf8mb4_ja_0900_as_cs_ks = g.kana_n
  AND IFNULL(ca.name_en,'')       COLLATE utf8mb4_ja_0900_as_cs_ks = g.en_n
WHERE ca.alias_id != g.keeper_id AND g.used_count > 0;

-- JOIN 構文（Error 1137 回避）
UPDATE credit_block_entries   p JOIN _ca_id_map m ON p.character_alias_id        = m.old_id SET p.character_alias_id        = m.new_id;
UPDATE song_recording_singers p JOIN _ca_id_map m ON p.character_alias_id        = m.old_id SET p.character_alias_id        = m.new_id;
UPDATE song_recording_singers p JOIN _ca_id_map m ON p.slash_character_alias_id  = m.old_id SET p.slash_character_alias_id  = m.new_id;
UPDATE precures               p JOIN _ca_id_map m ON p.pre_transform_alias_id    = m.old_id SET p.pre_transform_alias_id    = m.new_id;
UPDATE precures               p JOIN _ca_id_map m ON p.transform_alias_id        = m.old_id SET p.transform_alias_id        = m.new_id;
UPDATE precures               p JOIN _ca_id_map m ON p.transform2_alias_id       = m.old_id SET p.transform2_alias_id       = m.new_id;
UPDATE precures               p JOIN _ca_id_map m ON p.alt_form_alias_id         = m.old_id SET p.alt_form_alias_id         = m.new_id;
UPDATE person_alias_members   p JOIN _ca_id_map m ON p.member_character_alias_id = m.old_id SET p.member_character_alias_id = m.new_id;

DELETE FROM character_aliases WHERE alias_id IN (SELECT old_id FROM _ca_id_map);


-- ============================================================================
-- フェーズ 1.4: characters の重複統合
-- ============================================================================

DROP TEMPORARY TABLE IF EXISTS _used_c;
CREATE TEMPORARY TABLE _used_c (character_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_c
  SELECT DISTINCT character_id FROM character_aliases WHERE character_id IS NOT NULL;
INSERT IGNORE INTO _used_c
  SELECT DISTINCT character_id FROM character_family_relations WHERE character_id IS NOT NULL;
INSERT IGNORE INTO _used_c
  SELECT DISTINCT related_character_id FROM character_family_relations WHERE related_character_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _c_groups;
CREATE TEMPORARY TABLE _c_groups (
  name VARCHAR(255), kana_n VARCHAR(255), kind_n VARCHAR(50),
  keeper_id INT, dup_count INT, used_count INT
  -- PK は utf8mb4 で 3072 bytes 超過するため撤廃。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
-- _used_c は LEFT JOIN で 1 度だけ参照、文字列比較は明示 COLLATE で揃える
INSERT INTO _c_groups
SELECT
  c.name, IFNULL(c.name_kana,''), IFNULL(c.character_kind,''),
  COALESCE(
    MIN(CASE WHEN u.character_id IS NOT NULL THEN c.character_id END),
    MIN(c.character_id)
  ),
  COUNT(*), SUM(CASE WHEN u.character_id IS NOT NULL THEN 1 ELSE 0 END)
FROM characters c
LEFT JOIN _used_c u ON u.character_id = c.character_id
WHERE c.is_deleted = 0
GROUP BY c.name, IFNULL(c.name_kana,''), IFNULL(c.character_kind,'')
HAVING COUNT(*) > 1;

DROP TEMPORARY TABLE IF EXISTS _c_id_map;
CREATE TEMPORARY TABLE _c_id_map (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _c_id_map
SELECT c.character_id, g.keeper_id
FROM characters c
JOIN _c_groups g
  ON c.name                        COLLATE utf8mb4_ja_0900_as_cs_ks = g.name
  AND IFNULL(c.name_kana,'')       COLLATE utf8mb4_ja_0900_as_cs_ks = g.kana_n
  AND IFNULL(c.character_kind,'')  COLLATE utf8mb4_ja_0900_as_cs_ks = g.kind_n
WHERE c.character_id != g.keeper_id AND g.used_count > 0;

-- JOIN 構文（Error 1137 回避）
UPDATE character_aliases          p JOIN _c_id_map m ON p.character_id        = m.old_id SET p.character_id         = m.new_id;
UPDATE character_family_relations p JOIN _c_id_map m ON p.character_id        = m.old_id SET p.character_id         = m.new_id;
UPDATE character_family_relations p JOIN _c_id_map m ON p.related_character_id= m.old_id SET p.related_character_id = m.new_id;

DELETE FROM characters WHERE character_id IN (SELECT old_id FROM _c_id_map);


-- ============================================================================
-- フェーズ 1.5: company_aliases の重複統合
-- ============================================================================

DROP TEMPORARY TABLE IF EXISTS _used_coa;
CREATE TEMPORARY TABLE _used_coa (alias_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT company_alias_id FROM logos WHERE company_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT company_alias_id FROM credit_block_entries WHERE company_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT affiliation_company_alias_id FROM credit_block_entries WHERE affiliation_company_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT leading_company_alias_id FROM credit_role_blocks WHERE leading_company_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT predecessor_alias_id FROM company_aliases WHERE predecessor_alias_id IS NOT NULL;
INSERT IGNORE INTO _used_coa
  SELECT DISTINCT successor_alias_id FROM company_aliases WHERE successor_alias_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _coa_groups;
CREATE TEMPORARY TABLE _coa_groups (
  company_id INT, name VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255),
  keeper_id INT, dup_count INT, used_count INT
  -- PK は utf8mb4 で 3072 bytes 超過するため撤廃。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
-- _used_coa は LEFT JOIN で 1 度だけ参照、文字列比較は明示 COLLATE で揃える
INSERT INTO _coa_groups
SELECT
  ca.company_id, ca.name, IFNULL(ca.name_kana,''), IFNULL(ca.name_en,''),
  COALESCE(
    MIN(CASE WHEN u.alias_id IS NOT NULL THEN ca.alias_id END),
    MIN(ca.alias_id)
  ),
  COUNT(*), SUM(CASE WHEN u.alias_id IS NOT NULL THEN 1 ELSE 0 END)
FROM company_aliases ca
LEFT JOIN _used_coa u ON u.alias_id = ca.alias_id
WHERE ca.is_deleted = 0
GROUP BY ca.company_id, ca.name, IFNULL(ca.name_kana,''), IFNULL(ca.name_en,'')
HAVING COUNT(*) > 1;

DROP TEMPORARY TABLE IF EXISTS _coa_id_map;
CREATE TEMPORARY TABLE _coa_id_map (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _coa_id_map
SELECT ca.alias_id, g.keeper_id
FROM company_aliases ca
JOIN _coa_groups g
  ON ca.company_id                = g.company_id
  AND ca.name                     COLLATE utf8mb4_ja_0900_as_cs_ks = g.name
  AND IFNULL(ca.name_kana,'')     COLLATE utf8mb4_ja_0900_as_cs_ks = g.kana_n
  AND IFNULL(ca.name_en,'')       COLLATE utf8mb4_ja_0900_as_cs_ks = g.en_n
WHERE ca.alias_id != g.keeper_id AND g.used_count > 0;

-- JOIN 構文（Error 1137 回避）
UPDATE logos                p JOIN _coa_id_map m ON p.company_alias_id             = m.old_id SET p.company_alias_id             = m.new_id;
UPDATE credit_block_entries p JOIN _coa_id_map m ON p.company_alias_id             = m.old_id SET p.company_alias_id             = m.new_id;
UPDATE credit_block_entries p JOIN _coa_id_map m ON p.affiliation_company_alias_id = m.old_id SET p.affiliation_company_alias_id = m.new_id;
UPDATE credit_role_blocks   p JOIN _coa_id_map m ON p.leading_company_alias_id     = m.old_id SET p.leading_company_alias_id     = m.new_id;
UPDATE company_aliases      p JOIN _coa_id_map m ON p.predecessor_alias_id         = m.old_id SET p.predecessor_alias_id         = m.new_id;
UPDATE company_aliases      p JOIN _coa_id_map m ON p.successor_alias_id           = m.old_id SET p.successor_alias_id           = m.new_id;

DELETE FROM company_aliases WHERE alias_id IN (SELECT old_id FROM _coa_id_map);


-- ============================================================================
-- フェーズ 1.6: companies の重複統合
-- ============================================================================

DROP TEMPORARY TABLE IF EXISTS _used_co;
CREATE TEMPORARY TABLE _used_co (company_id INT PRIMARY KEY);
INSERT IGNORE INTO _used_co
  SELECT DISTINCT company_id FROM company_aliases WHERE company_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _co_groups;
CREATE TEMPORARY TABLE _co_groups (
  name VARCHAR(255), kana_n VARCHAR(255), en_n VARCHAR(255),
  keeper_id INT, dup_count INT, used_count INT
  -- PK は utf8mb4 で 3072 bytes 超過するため撤廃。
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
-- _used_co は LEFT JOIN で 1 度だけ参照、文字列比較は明示 COLLATE で揃える
INSERT INTO _co_groups
SELECT
  c.name, IFNULL(c.name_kana,''), IFNULL(c.name_en,''),
  COALESCE(
    MIN(CASE WHEN u.company_id IS NOT NULL THEN c.company_id END),
    MIN(c.company_id)
  ),
  COUNT(*), SUM(CASE WHEN u.company_id IS NOT NULL THEN 1 ELSE 0 END)
FROM companies c
LEFT JOIN _used_co u ON u.company_id = c.company_id
WHERE c.is_deleted = 0
GROUP BY c.name, IFNULL(c.name_kana,''), IFNULL(c.name_en,'')
HAVING COUNT(*) > 1;

DROP TEMPORARY TABLE IF EXISTS _co_id_map;
CREATE TEMPORARY TABLE _co_id_map (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _co_id_map
SELECT c.company_id, g.keeper_id
FROM companies c
JOIN _co_groups g
  ON c.name                       COLLATE utf8mb4_ja_0900_as_cs_ks = g.name
  AND IFNULL(c.name_kana,'')      COLLATE utf8mb4_ja_0900_as_cs_ks = g.kana_n
  AND IFNULL(c.name_en,'')        COLLATE utf8mb4_ja_0900_as_cs_ks = g.en_n
WHERE c.company_id != g.keeper_id AND g.used_count > 0;

-- JOIN 構文（Error 1137 回避）
UPDATE company_aliases p JOIN _co_id_map m ON p.company_id = m.old_id SET p.company_id = m.new_id;

DELETE FROM companies WHERE company_id IN (SELECT old_id FROM _co_id_map);


-- ============================================================================
-- フェーズ 2: ID 飛び番再採番（1, 2, 3, ... に詰める）
-- ============================================================================
-- 各マスタについて、旧 ID → 新 ID マップを作り、関連 FK 列を全部更新する。
-- 親テーブルの PK を直接 UPDATE すると一時的に重複が発生する可能性があるため、
-- 「全部負数に逃がす → 正の新 ID に戻す」の 2 段階で安全に行う。
-- FK_CHECKS=0 なので NO ACTION 系の FK も自由に書き換え可能。

-- 2.1 person_aliases の再採番
DROP TEMPORARY TABLE IF EXISTS _pa_renum;
CREATE TEMPORARY TABLE _pa_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _pa_renum
SELECT alias_id, ROW_NUMBER() OVER (ORDER BY alias_id) FROM person_aliases ORDER BY alias_id;

-- 親 + 全 FK を負数化。JOIN 構文なので CASCADE が効いた FK は既に値が反映済みで JOIN にマッチせず無更新（冪等）、
-- 効かない FK は明示的に書き換わる。
UPDATE person_aliases         p JOIN _pa_renum r ON p.alias_id               = r.old_id SET p.alias_id               = -r.new_id;
UPDATE person_alias_persons   p JOIN _pa_renum r ON p.alias_id               = r.old_id SET p.alias_id               = -r.new_id;
UPDATE person_aliases         p JOIN _pa_renum r ON p.predecessor_alias_id   = r.old_id SET p.predecessor_alias_id   = -r.new_id;
UPDATE person_aliases         p JOIN _pa_renum r ON p.successor_alias_id     = r.old_id SET p.successor_alias_id     = -r.new_id;
UPDATE person_alias_members   p JOIN _pa_renum r ON p.parent_alias_id        = r.old_id SET p.parent_alias_id        = -r.new_id;
UPDATE person_alias_members   p JOIN _pa_renum r ON p.member_person_alias_id = r.old_id SET p.member_person_alias_id = -r.new_id;
UPDATE credit_block_entries   p JOIN _pa_renum r ON p.person_alias_id        = r.old_id SET p.person_alias_id        = -r.new_id;
UPDATE song_credits           p JOIN _pa_renum r ON p.person_alias_id        = r.old_id SET p.person_alias_id        = -r.new_id;
UPDATE song_recording_singers p JOIN _pa_renum r ON p.person_alias_id        = r.old_id SET p.person_alias_id        = -r.new_id;
UPDATE song_recording_singers p JOIN _pa_renum r ON p.voice_person_alias_id  = r.old_id SET p.voice_person_alias_id  = -r.new_id;
UPDATE song_recording_singers p JOIN _pa_renum r ON p.slash_person_alias_id  = r.old_id SET p.slash_person_alias_id  = -r.new_id;
UPDATE bgm_cue_credits        p JOIN _pa_renum r ON p.person_alias_id        = r.old_id SET p.person_alias_id        = -r.new_id;

-- 負数 → 正の値へ反転
UPDATE person_aliases SET alias_id = -alias_id WHERE alias_id < 0;
UPDATE person_alias_persons SET alias_id = -alias_id WHERE alias_id < 0;
UPDATE person_aliases SET predecessor_alias_id = -predecessor_alias_id WHERE predecessor_alias_id IS NOT NULL AND predecessor_alias_id < 0;
UPDATE person_aliases SET successor_alias_id = -successor_alias_id WHERE successor_alias_id IS NOT NULL AND successor_alias_id < 0;
UPDATE person_alias_members SET parent_alias_id = -parent_alias_id WHERE parent_alias_id IS NOT NULL AND parent_alias_id < 0;
UPDATE person_alias_members SET member_person_alias_id = -member_person_alias_id WHERE member_person_alias_id IS NOT NULL AND member_person_alias_id < 0;
UPDATE credit_block_entries SET person_alias_id = -person_alias_id WHERE person_alias_id IS NOT NULL AND person_alias_id < 0;
UPDATE song_credits SET person_alias_id = -person_alias_id WHERE person_alias_id IS NOT NULL AND person_alias_id < 0;
UPDATE song_recording_singers SET person_alias_id = -person_alias_id WHERE person_alias_id IS NOT NULL AND person_alias_id < 0;
UPDATE song_recording_singers SET voice_person_alias_id = -voice_person_alias_id WHERE voice_person_alias_id IS NOT NULL AND voice_person_alias_id < 0;
UPDATE song_recording_singers SET slash_person_alias_id = -slash_person_alias_id WHERE slash_person_alias_id IS NOT NULL AND slash_person_alias_id < 0;
UPDATE bgm_cue_credits SET person_alias_id = -person_alias_id WHERE person_alias_id IS NOT NULL AND person_alias_id < 0;

ALTER TABLE person_aliases AUTO_INCREMENT = 1;


-- 2.2 persons の再採番
DROP TEMPORARY TABLE IF EXISTS _p_renum;
CREATE TEMPORARY TABLE _p_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _p_renum
SELECT person_id, ROW_NUMBER() OVER (ORDER BY person_id) FROM persons ORDER BY person_id;

UPDATE persons              p JOIN _p_renum r ON p.person_id             = r.old_id SET p.person_id             = -r.new_id;
UPDATE person_alias_persons p JOIN _p_renum r ON p.person_id             = r.old_id SET p.person_id             = -r.new_id;
UPDATE precures             p JOIN _p_renum r ON p.voice_actor_person_id = r.old_id SET p.voice_actor_person_id = -r.new_id;

UPDATE persons SET person_id = -person_id WHERE person_id < 0;
UPDATE person_alias_persons SET person_id = -person_id WHERE person_id < 0;
UPDATE precures SET voice_actor_person_id = -voice_actor_person_id WHERE voice_actor_person_id IS NOT NULL AND voice_actor_person_id < 0;

ALTER TABLE persons AUTO_INCREMENT = 1;


-- 2.3 character_aliases の再採番
DROP TEMPORARY TABLE IF EXISTS _ca_renum;
CREATE TEMPORARY TABLE _ca_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _ca_renum
SELECT alias_id, ROW_NUMBER() OVER (ORDER BY alias_id) FROM character_aliases ORDER BY alias_id;

UPDATE character_aliases      p JOIN _ca_renum r ON p.alias_id                  = r.old_id SET p.alias_id                  = -r.new_id;
UPDATE credit_block_entries   p JOIN _ca_renum r ON p.character_alias_id        = r.old_id SET p.character_alias_id        = -r.new_id;
UPDATE song_recording_singers p JOIN _ca_renum r ON p.character_alias_id        = r.old_id SET p.character_alias_id        = -r.new_id;
UPDATE song_recording_singers p JOIN _ca_renum r ON p.slash_character_alias_id  = r.old_id SET p.slash_character_alias_id  = -r.new_id;
UPDATE precures               p JOIN _ca_renum r ON p.pre_transform_alias_id    = r.old_id SET p.pre_transform_alias_id    = -r.new_id;
UPDATE precures               p JOIN _ca_renum r ON p.transform_alias_id        = r.old_id SET p.transform_alias_id        = -r.new_id;
UPDATE precures               p JOIN _ca_renum r ON p.transform2_alias_id       = r.old_id SET p.transform2_alias_id       = -r.new_id;
UPDATE precures               p JOIN _ca_renum r ON p.alt_form_alias_id         = r.old_id SET p.alt_form_alias_id         = -r.new_id;
UPDATE person_alias_members   p JOIN _ca_renum r ON p.member_character_alias_id = r.old_id SET p.member_character_alias_id = -r.new_id;

UPDATE character_aliases SET alias_id = -alias_id WHERE alias_id < 0;
UPDATE credit_block_entries SET character_alias_id = -character_alias_id WHERE character_alias_id IS NOT NULL AND character_alias_id < 0;
UPDATE song_recording_singers SET character_alias_id = -character_alias_id WHERE character_alias_id IS NOT NULL AND character_alias_id < 0;
UPDATE song_recording_singers SET slash_character_alias_id = -slash_character_alias_id WHERE slash_character_alias_id IS NOT NULL AND slash_character_alias_id < 0;
UPDATE precures SET pre_transform_alias_id = -pre_transform_alias_id WHERE pre_transform_alias_id IS NOT NULL AND pre_transform_alias_id < 0;
UPDATE precures SET transform_alias_id = -transform_alias_id WHERE transform_alias_id IS NOT NULL AND transform_alias_id < 0;
UPDATE precures SET transform2_alias_id = -transform2_alias_id WHERE transform2_alias_id IS NOT NULL AND transform2_alias_id < 0;
UPDATE precures SET alt_form_alias_id = -alt_form_alias_id WHERE alt_form_alias_id IS NOT NULL AND alt_form_alias_id < 0;
UPDATE person_alias_members SET member_character_alias_id = -member_character_alias_id WHERE member_character_alias_id IS NOT NULL AND member_character_alias_id < 0;

ALTER TABLE character_aliases AUTO_INCREMENT = 1;


-- 2.4 characters の再採番
DROP TEMPORARY TABLE IF EXISTS _c_renum;
CREATE TEMPORARY TABLE _c_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _c_renum
SELECT character_id, ROW_NUMBER() OVER (ORDER BY character_id) FROM characters ORDER BY character_id;

UPDATE characters                 p JOIN _c_renum r ON p.character_id         = r.old_id SET p.character_id         = -r.new_id;
UPDATE character_aliases          p JOIN _c_renum r ON p.character_id         = r.old_id SET p.character_id         = -r.new_id;
UPDATE character_family_relations p JOIN _c_renum r ON p.character_id         = r.old_id SET p.character_id         = -r.new_id;
UPDATE character_family_relations p JOIN _c_renum r ON p.related_character_id = r.old_id SET p.related_character_id = -r.new_id;

UPDATE characters SET character_id = -character_id WHERE character_id < 0;
UPDATE character_aliases SET character_id = -character_id WHERE character_id IS NOT NULL AND character_id < 0;
UPDATE character_family_relations SET character_id = -character_id WHERE character_id IS NOT NULL AND character_id < 0;
UPDATE character_family_relations SET related_character_id = -related_character_id WHERE related_character_id IS NOT NULL AND related_character_id < 0;

ALTER TABLE characters AUTO_INCREMENT = 1;


-- 2.5 company_aliases の再採番
DROP TEMPORARY TABLE IF EXISTS _coa_renum;
CREATE TEMPORARY TABLE _coa_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _coa_renum
SELECT alias_id, ROW_NUMBER() OVER (ORDER BY alias_id) FROM company_aliases ORDER BY alias_id;

UPDATE company_aliases      p JOIN _coa_renum r ON p.alias_id                     = r.old_id SET p.alias_id                     = -r.new_id;
UPDATE logos                p JOIN _coa_renum r ON p.company_alias_id             = r.old_id SET p.company_alias_id             = -r.new_id;
UPDATE credit_block_entries p JOIN _coa_renum r ON p.company_alias_id             = r.old_id SET p.company_alias_id             = -r.new_id;
UPDATE credit_block_entries p JOIN _coa_renum r ON p.affiliation_company_alias_id = r.old_id SET p.affiliation_company_alias_id = -r.new_id;
UPDATE credit_role_blocks   p JOIN _coa_renum r ON p.leading_company_alias_id     = r.old_id SET p.leading_company_alias_id     = -r.new_id;
UPDATE company_aliases      p JOIN _coa_renum r ON p.predecessor_alias_id         = r.old_id SET p.predecessor_alias_id         = -r.new_id;
UPDATE company_aliases      p JOIN _coa_renum r ON p.successor_alias_id           = r.old_id SET p.successor_alias_id           = -r.new_id;

UPDATE company_aliases SET alias_id = -alias_id WHERE alias_id < 0;
UPDATE logos SET company_alias_id = -company_alias_id WHERE company_alias_id IS NOT NULL AND company_alias_id < 0;
UPDATE credit_block_entries SET company_alias_id = -company_alias_id WHERE company_alias_id IS NOT NULL AND company_alias_id < 0;
UPDATE credit_block_entries SET affiliation_company_alias_id = -affiliation_company_alias_id WHERE affiliation_company_alias_id IS NOT NULL AND affiliation_company_alias_id < 0;
UPDATE credit_role_blocks SET leading_company_alias_id = -leading_company_alias_id WHERE leading_company_alias_id IS NOT NULL AND leading_company_alias_id < 0;
UPDATE company_aliases SET predecessor_alias_id = -predecessor_alias_id WHERE predecessor_alias_id IS NOT NULL AND predecessor_alias_id < 0;
UPDATE company_aliases SET successor_alias_id = -successor_alias_id WHERE successor_alias_id IS NOT NULL AND successor_alias_id < 0;

ALTER TABLE company_aliases AUTO_INCREMENT = 1;


-- 2.6 companies の再採番
DROP TEMPORARY TABLE IF EXISTS _co_renum;
CREATE TEMPORARY TABLE _co_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _co_renum
SELECT company_id, ROW_NUMBER() OVER (ORDER BY company_id) FROM companies ORDER BY company_id;

UPDATE companies       p JOIN _co_renum r ON p.company_id = r.old_id SET p.company_id = -r.new_id;
UPDATE company_aliases p JOIN _co_renum r ON p.company_id = r.old_id SET p.company_id = -r.new_id;

UPDATE companies SET company_id = -company_id WHERE company_id < 0;
UPDATE company_aliases SET company_id = -company_id WHERE company_id IS NOT NULL AND company_id < 0;

ALTER TABLE companies AUTO_INCREMENT = 1;


-- ============================================================================
-- 後処理
-- ============================================================================
SET FOREIGN_KEY_CHECKS = 1;
SET SQL_SAFE_UPDATES = 1;

-- 結果サマリ確認用クエリ（実行後に手動で動かすと便利）
-- SELECT COUNT(*) AS persons_count FROM persons;
-- SELECT COUNT(*) AS person_aliases_count FROM person_aliases;
-- SELECT COUNT(*) AS characters_count FROM characters;
-- SELECT COUNT(*) AS character_aliases_count FROM character_aliases;
-- SELECT COUNT(*) AS companies_count FROM companies;
-- SELECT COUNT(*) AS company_aliases_count FROM company_aliases;
-- SELECT MAX(person_id) AS persons_max_id FROM persons;
-- SELECT MAX(alias_id) AS person_aliases_max_id FROM person_aliases;

-- ============================================================================
-- 確認後、問題なければ次行を有効化してコミット。問題があれば ROLLBACK。
-- ============================================================================
-- COMMIT;
-- ROLLBACK;
