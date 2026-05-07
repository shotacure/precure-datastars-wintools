-- ===========================================================================
-- Migration: v1.2.3 -> v1.2.4
--   プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）テーブルを追加。
--   併せて未使用となった character_voice_castings テーブルを撤去する。
--
-- 変更内容:
--   1. character_voice_castings テーブルを撤去（無条件 DROP）。
--      事前に件数を NOTE 表示するが、業務側で 0 件を確認済みのため挙動は単純に DROP。
--      代わりに credit_block_entries（CHARACTER_VOICE エントリ）の登場行を
--      「ノンクレ除いてクレジットされている＝キャスティング」として扱う設計に変更
--      （実体テーブルとしての character_voice_castings は不要となる）。
--   2. character_relation_kinds テーブルを新設。キャラクター続柄マスタ。
--      初期データとして父・母・兄・弟・姉・妹・祖父・祖母・叔父・叔母・いとこ・
--      ペット・その他家族の 13 種を投入。
--   3. character_family_relations テーブルを新設。characters 同士の家族関係を
--      表す中間表（汎用、プリキュア以外でも使える）。
--      PK: (character_id, related_character_id, relation_code)。
--      自分自身（character_id = related_character_id）は MySQL 8.0.16+ の制約で
--      CHECK 制約として書けないため（FK の参照アクションで使う列を CHECK で
--      参照不可、Error 3823）、BEFORE INSERT/UPDATE トリガで SIGNAL する方式で禁止。
--   4. precures テーブルを新設。プリキュア本体マスタ。
--      変身前 / 変身後 / 変身後 2 / 別形態 の 4 alias FK、誕生日 (birth_month +
--      birth_day)、声優 person FK、肌色 HSL/RGB の 6 列、学校・クラス・家業の
--      テキスト 3 列を持つ。
--   5. precures に BEFORE INSERT/UPDATE トリガを追加。4 本の alias が指す
--      character_id がすべて同一であることを SIGNAL で検証。
--
-- 実行方法（既に v1.2.3 の DB に対して）:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.4_add_precures_and_family.sql
--
-- 冪等性:
--   各オブジェクトは INFORMATION_SCHEMA で存在確認してから DDL を発行するため
--   再実行しても安全。トリガは DROP IF EXISTS してから再作成する流儀。
-- ===========================================================================

-- ---------------------------------------------------------------------------
-- STEP 1: character_voice_castings 撤去
-- ---------------------------------------------------------------------------
-- 件数を NOTE 表示してから DROP。0 件でなくても無条件で DROP する方針
-- （業務側で利用していないことを v1.2.4 リリース前に確認済み）。
--
-- 注意: MySQL は IF(cond, a, b) を実行する際、両方の分岐を先にパースする。
--       そのため `IF(@cvc_exists = 0, 0, (SELECT COUNT(*) FROM character_voice_castings))`
--       のように書くと、テーブルが既に存在しない再実行時に第 2 引数の SELECT で
--       Error 1146 (Table doesn't exist) が出てしまう。
--       これを避けるため、テーブルが存在するときだけ COUNT 実行用の SQL 文字列を
--       組み立てて PREPARE/EXECUTE で遅延評価する方式に変更している（DROP 側と同じ流儀）。
SET @cvc_exists := (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.TABLES
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'character_voice_castings'
);

-- 件数取得：テーブルが存在するときだけ COUNT(*) を発行する。
-- 存在しない再実行時には @cvc_count を 0 に固定して DROP もスキップする。
SET @sql := IF(@cvc_exists = 0,
  'SET @cvc_count := 0',
  'SET @cvc_count := (SELECT COUNT(*) FROM `character_voice_castings`)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 件数の事前表示（DROP 後に値を取り直せないため、消す前にメッセージ生成しておく）
SET @msg := CONCAT(
  'character_voice_castings: existed=', @cvc_exists,
  ', rows_before_drop=', @cvc_count,
  ' (will be dropped unconditionally)');
SELECT @msg AS step1_status;

SET @sql := IF(@cvc_exists = 0,
  'SELECT ''character_voice_castings does not exist. skipping DROP.'' AS msg',
  'DROP TABLE `character_voice_castings`');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- STEP 2: character_relation_kinds 新設
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `character_relation_kinds` (
  `relation_code`  VARCHAR(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin       NOT NULL,
  `name_ja`        VARCHAR(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`        VARCHAR(64)  DEFAULT NULL,
  `display_order`  TINYINT UNSIGNED DEFAULT NULL,
  `notes`          TEXT         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     VARCHAR(64)  DEFAULT NULL,
  `updated_by`     VARCHAR(64)  DEFAULT NULL,
  PRIMARY KEY (`relation_code`),
  UNIQUE KEY `uq_character_relation_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 初期データ（既存と衝突しないよう INSERT IGNORE）
INSERT IGNORE INTO `character_relation_kinds`
  (`relation_code`, `name_ja`,        `name_en`,                `display_order`)
VALUES
  ('FATHER',         '父',           'Father',                 10),
  ('MOTHER',         '母',           'Mother',                 20),
  ('BROTHER_OLDER',  '兄',           'Older Brother',          30),
  ('BROTHER_YOUNGER','弟',           'Younger Brother',        40),
  ('SISTER_OLDER',   '姉',           'Older Sister',           50),
  ('SISTER_YOUNGER', '妹',           'Younger Sister',         60),
  ('GRANDFATHER',    '祖父',         'Grandfather',            70),
  ('GRANDMOTHER',    '祖母',         'Grandmother',            80),
  ('UNCLE',          '叔父・伯父',   'Uncle',                  90),
  ('AUNT',           '叔母・伯母',   'Aunt',                  100),
  ('COUSIN',         'いとこ',       'Cousin',                110),
  ('PET',            'ペット',       'Pet',                   120),
  ('OTHER_FAMILY',   'その他家族',   'Other Family Member',   130);

-- ---------------------------------------------------------------------------
-- STEP 3: character_family_relations 新設
-- ---------------------------------------------------------------------------
-- 1 行 = 「character_id から見た related_character_id の続柄」。
-- 双方向で持つ場合は 2 行を別途立てる（自動補完しない）。
-- 自己参照禁止（character_id = related_character_id）は当初 CHECK 制約 ck_cfr_no_self で
-- 表現していたが、MySQL 8.0.16+ では「FK の参照アクション（CASCADE 等）で使う列を CHECK
-- 制約から参照できない」(Error 3823) ため定義不能。STEP 3 末尾の BEFORE INSERT /
-- BEFORE UPDATE トリガ tr_cfr_check_no_self_bi / _bu で SIGNAL する方式に変更した
-- （precures の character_id 整合性検証と同じトリガパターン）。
CREATE TABLE IF NOT EXISTS `character_family_relations` (
  `character_id`         INT             NOT NULL,
  `related_character_id` INT             NOT NULL,
  `relation_code`        VARCHAR(32)     CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `display_order`        TINYINT UNSIGNED DEFAULT NULL,
  `notes`                TEXT            CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`           TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`           VARCHAR(64)     DEFAULT NULL,
  `updated_by`           VARCHAR(64)     DEFAULT NULL,
  PRIMARY KEY (`character_id`, `related_character_id`, `relation_code`),
  KEY `ix_cfr_related`        (`related_character_id`),
  KEY `ix_cfr_relation_code`  (`relation_code`),
  CONSTRAINT `fk_cfr_character`     FOREIGN KEY (`character_id`)         REFERENCES `characters`               (`character_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_cfr_related`       FOREIGN KEY (`related_character_id`) REFERENCES `characters`               (`character_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_cfr_relation_kind` FOREIGN KEY (`relation_code`)        REFERENCES `character_relation_kinds` (`relation_code`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- character_family_relations の自己参照禁止トリガ。
-- DROP TRIGGER IF EXISTS してから再作成する流儀で冪等。
DROP TRIGGER IF EXISTS `tr_cfr_check_no_self_bi`;
DROP TRIGGER IF EXISTS `tr_cfr_check_no_self_bu`;

DELIMITER ;;
CREATE TRIGGER `tr_cfr_check_no_self_bi` BEFORE INSERT ON `character_family_relations`
FOR EACH ROW
BEGIN
  IF NEW.character_id = NEW.related_character_id THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'character_family_relations: character_id and related_character_id must differ (self-relation forbidden)';
  END IF;
END;;
CREATE TRIGGER `tr_cfr_check_no_self_bu` BEFORE UPDATE ON `character_family_relations`
FOR EACH ROW
BEGIN
  IF NEW.character_id = NEW.related_character_id THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'character_family_relations: character_id and related_character_id must differ (self-relation forbidden)';
  END IF;
END;;
DELIMITER ;

-- ---------------------------------------------------------------------------
-- STEP 4: precures 新設
-- ---------------------------------------------------------------------------
-- 名義は character_aliases を参照する 4 本の FK。pre_transform / transform は
-- 必須、transform2 / alt_form は任意。誕生日は birth_month + birth_day の
-- 2 列に正規化（和文・英文表示は GUI 側で生成）。
-- 肌色は HSL（H 0-360, S 0-100, L 0-100）と RGB（R/G/B 0-255）を併記し、
-- アプリ側で両者の整合性（CIE76 ΔE）を確認する設計。
CREATE TABLE IF NOT EXISTS `precures` (
  `precure_id`             INT                NOT NULL AUTO_INCREMENT,
  `pre_transform_alias_id` INT                NOT NULL,
  `transform_alias_id`     INT                NOT NULL,
  `transform2_alias_id`    INT                DEFAULT NULL,
  `alt_form_alias_id`      INT                DEFAULT NULL,
  `birth_month`            TINYINT UNSIGNED   DEFAULT NULL,
  `birth_day`              TINYINT UNSIGNED   DEFAULT NULL,
  `voice_actor_person_id`  INT                DEFAULT NULL,
  `skin_color_h`           SMALLINT UNSIGNED  DEFAULT NULL,
  `skin_color_s`           TINYINT UNSIGNED   DEFAULT NULL,
  `skin_color_l`           TINYINT UNSIGNED   DEFAULT NULL,
  `skin_color_r`           TINYINT UNSIGNED   DEFAULT NULL,
  `skin_color_g`           TINYINT UNSIGNED   DEFAULT NULL,
  `skin_color_b`           TINYINT UNSIGNED   DEFAULT NULL,
  `school`                 VARCHAR(128)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `school_class`           VARCHAR(64)        CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `family_business`        VARCHAR(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`                  TEXT               CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`             TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`             VARCHAR(64)        DEFAULT NULL,
  `updated_by`             VARCHAR(64)        DEFAULT NULL,
  `is_deleted`             TINYINT NOT NULL DEFAULT '0',
  PRIMARY KEY (`precure_id`),
  UNIQUE KEY `uq_precures_transform_alias`     (`transform_alias_id`),
  KEY `ix_precures_pre_transform_alias`        (`pre_transform_alias_id`),
  KEY `ix_precures_transform2_alias`           (`transform2_alias_id`),
  KEY `ix_precures_alt_form_alias`             (`alt_form_alias_id`),
  KEY `ix_precures_voice_actor`                (`voice_actor_person_id`),
  CONSTRAINT `fk_precures_pre_transform`  FOREIGN KEY (`pre_transform_alias_id`) REFERENCES `character_aliases` (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_transform`      FOREIGN KEY (`transform_alias_id`)     REFERENCES `character_aliases` (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_transform2`     FOREIGN KEY (`transform2_alias_id`)    REFERENCES `character_aliases` (`alias_id`)  ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_alt_form`       FOREIGN KEY (`alt_form_alias_id`)      REFERENCES `character_aliases` (`alias_id`)  ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_voice_actor`    FOREIGN KEY (`voice_actor_person_id`)  REFERENCES `persons`           (`person_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_precures_birth_month`    CHECK (`birth_month` IS NULL OR (`birth_month` BETWEEN 1 AND 12)),
  CONSTRAINT `ck_precures_birth_day`      CHECK (`birth_day`   IS NULL OR (`birth_day`   BETWEEN 1 AND 31)),
  CONSTRAINT `ck_precures_skin_h`         CHECK (`skin_color_h` IS NULL OR (`skin_color_h` BETWEEN 0 AND 360)),
  CONSTRAINT `ck_precures_skin_s`         CHECK (`skin_color_s` IS NULL OR (`skin_color_s` BETWEEN 0 AND 100)),
  CONSTRAINT `ck_precures_skin_l`         CHECK (`skin_color_l` IS NULL OR (`skin_color_l` BETWEEN 0 AND 100))
  -- skin_color_r / _g / _b は TINYINT UNSIGNED の型範囲（0-255）と完全一致のため CHECK 不要。
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------------
-- STEP 5: precures BEFORE INSERT / UPDATE トリガ
-- ---------------------------------------------------------------------------
-- 4 本の alias FK が指す character_id が同一であることを保証する。
-- 「レギュラープリキュアで変身前後で別キャラになる者はいない」という業務ルールを
-- DB レイヤーで強制する。CHECK 制約で別テーブルを参照できない MySQL 8.0 の
-- 制限のため、トリガで SIGNAL する方式（v1.2.0 の credit_block_entries と同じ）。
DROP TRIGGER IF EXISTS `tr_precures_check_character_bi`;
DROP TRIGGER IF EXISTS `tr_precures_check_character_bu`;

DELIMITER ;;
CREATE TRIGGER `tr_precures_check_character_bi` BEFORE INSERT ON `precures`
FOR EACH ROW
BEGIN
  DECLARE c_pre   INT DEFAULT NULL;
  DECLARE c_main  INT DEFAULT NULL;
  DECLARE c_main2 INT DEFAULT NULL;
  DECLARE c_alt   INT DEFAULT NULL;
  SELECT character_id INTO c_pre   FROM character_aliases WHERE alias_id = NEW.pre_transform_alias_id;
  SELECT character_id INTO c_main  FROM character_aliases WHERE alias_id = NEW.transform_alias_id;
  IF NEW.transform2_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_main2 FROM character_aliases WHERE alias_id = NEW.transform2_alias_id;
    IF c_main2 <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform2_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF NEW.alt_form_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_alt FROM character_aliases WHERE alias_id = NEW.alt_form_alias_id;
    IF c_alt <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: alt_form_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF c_main <> c_pre THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform_alias_id must point to the same character as pre_transform_alias_id';
  END IF;
END;;
CREATE TRIGGER `tr_precures_check_character_bu` BEFORE UPDATE ON `precures`
FOR EACH ROW
BEGIN
  DECLARE c_pre   INT DEFAULT NULL;
  DECLARE c_main  INT DEFAULT NULL;
  DECLARE c_main2 INT DEFAULT NULL;
  DECLARE c_alt   INT DEFAULT NULL;
  SELECT character_id INTO c_pre   FROM character_aliases WHERE alias_id = NEW.pre_transform_alias_id;
  SELECT character_id INTO c_main  FROM character_aliases WHERE alias_id = NEW.transform_alias_id;
  IF NEW.transform2_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_main2 FROM character_aliases WHERE alias_id = NEW.transform2_alias_id;
    IF c_main2 <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform2_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF NEW.alt_form_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_alt FROM character_aliases WHERE alias_id = NEW.alt_form_alias_id;
    IF c_alt <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: alt_form_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF c_main <> c_pre THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform_alias_id must point to the same character as pre_transform_alias_id';
  END IF;
END;;
DELIMITER ;

-- Migration v1.2.4 completed
SELECT 'v1.2.4 migration completed: character_voice_castings dropped, precures + character_relation_kinds + character_family_relations created' AS final_status;
