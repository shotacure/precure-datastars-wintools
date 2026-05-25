-- ============================================================================
-- モブキャラ分離 + 固有名重複統合 + ID 再採番
-- ============================================================================
-- 対象テーブル:
--   characters / character_aliases / persons / person_aliases
--
-- 副作用:
--   フェーズ A: モブキャラ（女生徒 / 少年 / 強盗 / 科学部員 / サッカー部員 /
--               光の国の住人 / 少女）を「クレジット entry ごとに別 character +
--               別 character_alias」へ分離する。
--               既存運用バグの名残で「1 character_id に多数の alias がぶら下がる」
--               「1 alias_id に多数の声優エントリがぶら下がる」状態を解消する。
--               2 件目以降の (alias, entry) ペアを新規 character + 新規 alias に
--               切り出して、credit_block_entries.character_alias_id を新 alias に
--               振り替える。1 件目はそのまま元の character / alias に残す。
--   フェーズ B: クレジットから参照されていない「孤立 character_alias」を削除する。
--               対象は事前に手動で特定した 8 件（サッカー部員 3 件 / 谷口聖子 3 件 /
--               木俣の祖父 1 件 / 木俣の祖母 1 件）。
--   フェーズ C: person_aliases / persons / character_aliases の重複統合。
--               固有名の重複（過去の Bulk Apply Pending 重複排除バグの名残）を
--               同一 person_id / 同一 character_id でまとめる。
--               characters の同名統合はフェーズ A の意図（モブを別 character に
--               分離）を破壊するため、本ファイルでは実施しない。
--   フェーズ D: characters / character_aliases / persons / person_aliases の ID を
--               1, 2, 3, ... に詰める（飛び番再採番、FK 列も全て同期更新）。
--               company_aliases / companies / logos などは今回スコープ外。
--
-- 実行手順:
--   1. 必ず事前にバックアップ（mysqldump 等）を取る
--   2. 本スクリプトを 1 トランザクションで流す（最後の COMMIT を実行するまで
--      全部 ROLLBACK 可能）
--   3. 末尾の検証 SELECT 群で結果を確認し、問題なければ COMMIT、問題があれば
--      ROLLBACK
--
-- 設計メモ:
--   - SET FOREIGN_KEY_CHECKS = 0 で FK 制約を一時無効化（NO ACTION な FK もあるため）
--   - フェーズ A は対象モブ × (alias, entry) ペアの構成を事前 SELECT で確定済み。
--     SQL はハードコードで書き下す（汎用クエリだとモブ判定の境界が曖昧になるため）。
--   - フェーズ C は同一 v1.4.2_master_cleanup.sql のフェーズ 1.1〜1.3 を一部流用。
--     ただし characters / company 系の重複統合（旧ドラフトのフェーズ 1.4〜1.6）は
--     不要 / スコープ外として持ち込まない。
--   - 再採番は「全 FK 列を負数へ → 正値へ反転」の 2 段階で衝突回避。
-- ============================================================================

START TRANSACTION;
SET FOREIGN_KEY_CHECKS = 0;
-- MySQL Workbench の safe update mode（KEY 列無しの UPDATE/DELETE をブロック）を
-- 一時無効化。JOIN ベースの一括更新で Error 1175 が出るのを回避。
SET SQL_SAFE_UPDATES = 0;


-- ============================================================================
-- フェーズ A: モブキャラの「クレジット entry ごとに別キャラ分離」
-- ============================================================================
-- 各モブについて、1 件目の (alias, entry) ペアは元の character / alias を維持し、
-- 2 件目以降を新 character + 新 alias に切り出す。
--
-- 既存実態（事前 SELECT で確定済み）:
--   サッカー部員 char_id=44: alias 53(entry 1100), 54(1101), 72(1484), 73(1485)
--                            + orphan 67, 69, 70（フェーズ B で削除）
--   科学部員     char_id=43: alias 51(entry 1098), 52(1099)
--   科学部員     char_id=52: alias 75 に entry 1642, 1643, 1644（B 型: 1 alias 多 entry）
--   強盗         char_id=37: alias 43(entry 851), 44(852), 45(853)
--   光の国の住人 char_id=59: alias 82 に entry 2023, 2024, 2025, 2026, 2027（B 型）
--   女生徒       char_id=17: alias 17(205), 23(367), 24(368), 39(617), 55(1102),
--                            60(1327), 61(1328)
--   少女         char_id=63: alias 86 に entry 2183, 2184（B 型）
--   少年         char_id=27: alias 31(450), 32(451)
--
-- 「A 型」（1 character 多 alias、各 alias 1 entry）は alias の character_id を
-- 新 character に振り替えるだけ（entry の振替は不要、alias 経由で自動）。
-- 「B 型」（1 character 1 alias 多 entry）は新 character + 新 alias を作って、
-- entry の character_alias_id を新 alias に振り替える。


-- ---- サッカー部員（char_id=44, A 型）-----------------------------------------
-- keep: alias 53（entry 1100, 飯田 利信 #13）
-- move alias 54 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 44;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 54;
-- move alias 72 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 44;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 72;
-- move alias 73 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 44;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 73;


-- ---- 科学部員（char_id=43, A 型）---------------------------------------------
-- keep: alias 51（entry 1098, 埴岡 由紀子 #13）
-- move alias 52 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 43;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 52;


-- ---- 科学部員（char_id=52, B 型: alias 75 に 3 entries）----------------------
-- keep: entry 1642 を alias 75 / char 52 に残す（菊池 心 #20）
-- move entry 1643 → 新 char + 新 alias（埴岡 由紀子 #20）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 52;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '科学部員', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 1643;
-- move entry 1644 → 新 char + 新 alias（仙台 エリ #20）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 52;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '科学部員', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 1644;


-- ---- 強盗（char_id=37, A 型）------------------------------------------------
-- keep: alias 43（entry 851, 園部 啓一 #10）
-- move alias 44 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 37;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 44;
-- move alias 45 → 新 char
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 37;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 45;


-- ---- 光の国の住人（char_id=59, B 型: alias 82 に 5 entries）-----------------
-- keep: entry 2023 を alias 82 / char 59 に残す（間島 淳司 #25）
-- move entry 2024 → 新 char + 新 alias（埴岡 由紀子 #25）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 59;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '光の国の住人', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 2024;
-- move entry 2025 → 新 char + 新 alias（文月 くん #25）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 59;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '光の国の住人', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 2025;
-- move entry 2026 → 新 char + 新 alias（河本 邦弘 #25）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 59;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '光の国の住人', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 2026;
-- move entry 2027 → 新 char + 新 alias（鶴 博幸 #25）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 59;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '光の国の住人', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 2027;


-- ---- 女生徒（char_id=17, A 型）----------------------------------------------
-- keep: alias 17（entry 205, 西野 陽子 #2）
-- move alias 23 → 新 char（木川 絵理子 #4）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 23;
-- move alias 24 → 新 char（西野 陽子 #4）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 24;
-- move alias 39 → 新 char（桂 由利香 #7）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 39;
-- move alias 55 → 新 char（西野 陽子 #13）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 55;
-- move alias 60 → 新 char（木川 絵理子 #16）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 60;
-- move alias 61 → 新 char（西野 陽子 #16）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 17;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 61;


-- ---- 少女（char_id=63, B 型: alias 86 に 2 entries）-------------------------
-- keep: entry 2183 を alias 86 / char 63 に残す（西野 陽子 #27）
-- move entry 2184 → 新 char + 新 alias（菊池 こころ #27）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 63;
INSERT INTO character_aliases (character_id, name, created_by, updated_by)
VALUES (LAST_INSERT_ID(), '少女', 'shota', 'shota');
UPDATE credit_block_entries SET character_alias_id = LAST_INSERT_ID() WHERE entry_id = 2184;


-- ---- 少年（char_id=27, A 型）------------------------------------------------
-- keep: alias 31（entry 450, 飯田 利信 #5）
-- move alias 32 → 新 char（天田 真人 #5）
INSERT INTO characters (name, character_kind, created_by, updated_by)
SELECT name, character_kind, 'shota', 'shota' FROM characters WHERE character_id = 27;
UPDATE character_aliases SET character_id = LAST_INSERT_ID() WHERE alias_id = 32;


-- ============================================================================
-- フェーズ B: 孤立 character_alias の削除（クレジット未参照の 8 件）
-- ============================================================================
-- 事前 SELECT で「credit_block_entries / song_recording_singers / precures 等
-- いずれからも参照されていない」ことを確認済みの 8 件をハードコードで削除。
DELETE FROM character_aliases WHERE alias_id IN (
    67, 69, 70,    -- サッカー部員 orphan
    66, 68, 71,    -- 谷口聖子 orphan
    62,            -- 木俣の祖父 orphan
    63             -- 木俣の祖母 orphan
);


-- ============================================================================
-- フェーズ C.1: person_aliases の重複統合
-- ============================================================================
-- 過去の Bulk Apply Pending 重複排除バグの名残で同名 alias が複数登録されている
-- グループを最小 alias_id に統合する。前段の調査で 5 グループ・14 alias が
-- 該当する。全グループとも紐付く person_id は同一（同一人物の重複登録）。

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

-- 1.1b 主人物 person_id を引いた拡張ビュー（同名でも別人物なら別グループ）
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

-- 1.1d loser → keeper のマップ
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
  AND g.used_count > 0;

-- 1.1e 参照を keeper に張り替え（MySQL 一時テーブルは同一クエリで 2 回参照不可 → JOIN 構文）
-- person_alias_persons は PRIMARY KEY (alias_id, person_id) を持つため、loser 側を keeper に
-- UPDATE する直前に「keeper 側に同 person_id の行が既に存在する loser 行」を先に削除する
-- （そのまま UPDATE すると Error 1062 Duplicate entry になる）。
-- DELETE ... WHERE EXISTS で自己参照は Error 1093 になるため、削除対象を一時テーブルに切り出す。
DROP TEMPORARY TABLE IF EXISTS _pap_delete;
CREATE TEMPORARY TABLE _pap_delete (alias_id INT, person_id INT, PRIMARY KEY (alias_id, person_id));
INSERT INTO _pap_delete
SELECT pap.alias_id, pap.person_id
FROM person_alias_persons pap
JOIN _pa_id_map m ON pap.alias_id = m.old_id
JOIN person_alias_persons pap2 ON pap2.alias_id = m.new_id AND pap2.person_id = pap.person_id;
DELETE pap FROM person_alias_persons pap
JOIN _pap_delete d ON d.alias_id = pap.alias_id AND d.person_id = pap.person_id;

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

-- 1.1f person_alias_persons の重複行を排除した上で、loser alias 自体を削除
DELETE FROM person_alias_persons
WHERE NOT EXISTS (SELECT 1 FROM person_aliases pa WHERE pa.alias_id = person_alias_persons.alias_id);
DELETE p1 FROM person_alias_persons p1
JOIN person_alias_persons p2
  ON p1.alias_id = p2.alias_id AND p1.person_id = p2.person_id AND p1.person_seq > p2.person_seq;
DELETE FROM person_aliases WHERE alias_id IN (SELECT old_id FROM _pa_id_map);


-- ============================================================================
-- フェーズ C.2: persons の重複統合（あれば）
-- ============================================================================
-- 同名同 family/given/kana/en の persons が複数あれば 1 件に統合する。
-- 現状では重複は確認していないが、idempotent に走らせる。
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
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
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

UPDATE person_alias_persons p JOIN _p_id_map m ON p.person_id            = m.old_id SET p.person_id            = m.new_id;
UPDATE precures             p JOIN _p_id_map m ON p.voice_actor_person_id= m.old_id SET p.voice_actor_person_id= m.new_id;

DELETE p1 FROM person_alias_persons p1
JOIN person_alias_persons p2
  ON p1.alias_id = p2.alias_id AND p1.person_id = p2.person_id AND p1.person_seq > p2.person_seq;

DELETE FROM persons WHERE person_id IN (SELECT old_id FROM _p_id_map);


-- ============================================================================
-- フェーズ C.3: character_aliases 同 character_id 内同名統合（あれば）
-- ============================================================================
-- フェーズ A でモブを分離した結果、新 character はそれぞれ alias 1 件しか持たない
-- ため、同 character_id 内の同名 alias は通常存在しないはず。
-- ただし、過去の手動編集等で同 character_id 内に同名 alias が積み上がっている
-- ケースがあれば idempotent に統合する（無ければ空 SET で何も処理されない）。
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
  keeper_id INT, dup_count INT, used_count INT
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_ja_0900_as_cs_ks;
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
-- フェーズ D: ID 飛び番再採番（1, 2, 3, ... に詰める）
-- ============================================================================
-- characters / character_aliases / persons / person_aliases の 4 テーブルを対象に、
-- 旧 ID → 新 ID マップを ROW_NUMBER で作って FK 参照を全て同期更新する。
-- 親 PK を直接 UPDATE すると一時的に重複が発生し得るため、
-- 「全 FK 列を負数化 → 正値に反転」の 2 段階で衝突回避する。
-- companies / company_aliases / logos は今回スコープ外（再採番しない）。

-- ---- D.1 person_aliases の再採番 ---------------------------------------------
DROP TEMPORARY TABLE IF EXISTS _pa_renum;
CREATE TEMPORARY TABLE _pa_renum (old_id INT PRIMARY KEY, new_id INT NOT NULL);
INSERT INTO _pa_renum
SELECT alias_id, ROW_NUMBER() OVER (ORDER BY alias_id) FROM person_aliases ORDER BY alias_id;

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


-- ---- D.2 persons の再採番 ---------------------------------------------------
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


-- ---- D.3 character_aliases の再採番 -----------------------------------------
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


-- ---- D.4 characters の再採番 ------------------------------------------------
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


-- ============================================================================
-- 後処理
-- ============================================================================
SET FOREIGN_KEY_CHECKS = 1;
SET SQL_SAFE_UPDATES = 1;


-- ============================================================================
-- 検証 SELECT 群（COMMIT 前に手動で結果確認するためのクエリ）
-- ============================================================================
-- 1. 各マスタの件数と最大 ID（飛び番無し = COUNT = MAX なら理想）
SELECT 'characters'        AS table_name, COUNT(*) AS row_count, MAX(character_id) AS max_id FROM characters
UNION ALL
SELECT 'character_aliases', COUNT(*), MAX(alias_id) FROM character_aliases
UNION ALL
SELECT 'persons',           COUNT(*), MAX(person_id) FROM persons
UNION ALL
SELECT 'person_aliases',    COUNT(*), MAX(alias_id) FROM person_aliases;

-- 2. 分離対象モブが「entry 1 件 = 1 character」になっているかの確認
--    各モブ名について、name で引いた character_id 群と、紐付く alias 件数・entry 件数を集計
SELECT
    c.name AS char_name,
    COUNT(DISTINCT c.character_id)        AS character_count,
    COUNT(DISTINCT ca.alias_id)           AS alias_count,
    COUNT(DISTINCT cbe.entry_id)          AS entry_count
FROM characters c
LEFT JOIN character_aliases ca ON ca.character_id = c.character_id AND ca.is_deleted = 0
LEFT JOIN credit_block_entries cbe ON cbe.character_alias_id = ca.alias_id
WHERE c.name IN ('女生徒','少年','強盗','科学部員','サッカー部員','光の国の住人','少女')
  AND c.is_deleted = 0
GROUP BY c.name
ORDER BY c.name;

-- 3. 固有名の重複統合確認（各名前で alias 1 件・person 1 件になっていれば理想）
SELECT
    pa.name,
    COUNT(*) AS alias_count
FROM person_aliases pa
WHERE pa.name IN ('徳丸 完','巴 菁子','山岡 直子','吉田 小南美','座古 明史')
  AND pa.is_deleted = 0
GROUP BY pa.name
ORDER BY pa.name;

-- 4. 削除予定の orphan character_alias が消えているか（0 件なら成功）
SELECT COUNT(*) AS remaining_orphan_aliases
FROM character_aliases
WHERE alias_id IN (62, 63, 66, 67, 68, 69, 70, 71);

-- 5. 各テーブルの ID 連続性確認（飛び番があればそれを列挙）
SELECT 'characters' AS table_name, character_id AS gap_id FROM (
    SELECT character_id, character_id - ROW_NUMBER() OVER (ORDER BY character_id) AS gap_marker
    FROM characters
) t WHERE gap_marker > 0
UNION ALL
SELECT 'character_aliases', alias_id FROM (
    SELECT alias_id, alias_id - ROW_NUMBER() OVER (ORDER BY alias_id) AS gap_marker
    FROM character_aliases
) t WHERE gap_marker > 0
UNION ALL
SELECT 'persons', person_id FROM (
    SELECT person_id, person_id - ROW_NUMBER() OVER (ORDER BY person_id) AS gap_marker
    FROM persons
) t WHERE gap_marker > 0
UNION ALL
SELECT 'person_aliases', alias_id FROM (
    SELECT alias_id, alias_id - ROW_NUMBER() OVER (ORDER BY alias_id) AS gap_marker
    FROM person_aliases
) t WHERE gap_marker > 0;


-- ============================================================================
-- 確認後、問題なければ次行を有効化してコミット。問題があれば ROLLBACK。
-- ============================================================================
-- COMMIT;
