
-- =============================================================================
-- Migration: v1.3.0 — bgm_cue_credits.credit_role を enum → varchar(32) + roles FK 化
-- =============================================================================
-- 背景・冪等性・Safe Update Mode 対策は song_credits 用と同じ方針。
-- 既存値リネーム：COMPOSER → COMPOSITION, ARRANGER → ARRANGEMENT。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

SET SESSION SQL_SAFE_UPDATES = 0;

-- (1-a) 残存作業列があれば DROP。
SET @work_col_exists = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND COLUMN_NAME = 'credit_role_code'
);
SET @stmt = IF(@work_col_exists > 0,
  'ALTER TABLE `bgm_cue_credits` DROP COLUMN `credit_role_code`',
  'SELECT ''skip step 1-a: credit_role_code does not exist'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (1-b) credit_role が enum なら作業列を追加。
SET @old_role_is_enum = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND COLUMN_NAME = 'credit_role'
    AND COLUMN_TYPE LIKE 'enum%'
);
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `bgm_cue_credits`
     ADD COLUMN `credit_role_code` VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL AFTER `credit_role`',
  'SELECT ''skip step 1-b: credit_role is no longer enum'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (2) UPDATE で値リネームしながらコピー。
SET @stmt = IF(@old_role_is_enum > 0,
  'UPDATE `bgm_cue_credits` SET `credit_role_code` = CASE `credit_role`
      WHEN ''COMPOSER'' THEN ''COMPOSITION''
      WHEN ''ARRANGER'' THEN ''ARRANGEMENT''
      ELSE `credit_role`
    END',
  'SELECT ''skip step 2: credit_role is no longer enum'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (3-pre) PK 削除前に FK fk_bgm_cue_credits_cue が依存できる代替インデックスを確保。
--          fk_bgm_cue_credits_cue は (series_id, m_no_detail) を参照しているので、
--          同じ列順の補助インデックスを作る。これで PK を安全に DROP できる。
SET @has_cue_idx = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND INDEX_NAME = 'ix_bgm_cue_credits_cue'
);
SET @stmt = IF(@has_cue_idx = 0,
  'ALTER TABLE `bgm_cue_credits` ADD KEY `ix_bgm_cue_credits_cue` (`series_id`, `m_no_detail`)',
  'SELECT ''skip step 3-pre: ix_bgm_cue_credits_cue already exists'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (3) PK を一旦削除。
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `bgm_cue_credits` DROP PRIMARY KEY',
  'SELECT ''skip step 3: already migrated'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (4) 旧 enum 列を削除。
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `bgm_cue_credits` DROP COLUMN `credit_role`',
  'SELECT ''skip step 4'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (5) 作業列を credit_role にリネーム + NOT NULL。
SET @work_col_exists_now = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND COLUMN_NAME = 'credit_role_code'
);
SET @stmt = IF(@work_col_exists_now > 0,
  'ALTER TABLE `bgm_cue_credits`
     CHANGE COLUMN `credit_role_code` `credit_role` VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL',
  'SELECT ''skip step 5'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (6) PK を再構成（4 列複合）。
SET @has_pk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND CONSTRAINT_TYPE = 'PRIMARY KEY'
);
SET @stmt = IF(@has_pk = 0,
  'ALTER TABLE `bgm_cue_credits` ADD PRIMARY KEY (`series_id`, `m_no_detail`, `credit_role`, `credit_seq`)',
  'SELECT ''skip step 6'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (7) credit_role 単独の検索インデックス。
SET @has_idx = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND INDEX_NAME = 'ix_bgm_cue_credits_role'
);
SET @stmt = IF(@has_idx = 0,
  'ALTER TABLE `bgm_cue_credits` ADD KEY `ix_bgm_cue_credits_role` (`credit_role`)',
  'SELECT ''skip step 7'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (8) roles マスタへの FK。
SET @has_fk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'bgm_cue_credits'
    AND CONSTRAINT_NAME = 'fk_bgm_cue_credits_role'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk = 0,
  'ALTER TABLE `bgm_cue_credits`
     ADD CONSTRAINT `fk_bgm_cue_credits_role`
       FOREIGN KEY (`credit_role`) REFERENCES `roles`(`role_code`)
       ON UPDATE CASCADE ON DELETE RESTRICT',
  'SELECT ''skip step 8'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: bgm_cue_credits.credit_role to varchar + roles FK' AS final_status;
