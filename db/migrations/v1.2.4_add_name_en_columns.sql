-- ===========================================================================
-- Migration: v1.2.4 内追加 — name_en 列追加
--   person_aliases / company_aliases / characters / character_aliases の 4 表に
--   name_en VARCHAR(128) NULL 列を追加する。
--
--   背景: 親テーブル persons / companies は v1.2.0 から name_en 列を持っているのに、
--   名義テーブル（*_aliases）と characters / character_aliases は持っていなかった。
--   英文クレジット出力では「人物名義」「企業屋号」「キャラクター名義」の表記単位で
--   英語表記が要るシーンが多いため、対称性確保もかねて 4 表に揃えて追加する。
--   v1.2.4 内のフォローアップマイグレで、本体マイグレ
--   v1.2.4_add_precures_and_family.sql とは別ファイルに分離している
--   （プリキュア追加と関心事が違うため、独立して再実行できるよう）。
--
-- 実行方法（既に v1.2.4_add_precures_and_family.sql は適用済みの DB に対して）:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.4_add_name_en_columns.sql
--
-- 冪等性:
--   各列の存在を INFORMATION_SCHEMA.COLUMNS で確認してから ALTER を発行するため、
--   再実行しても安全。
-- ===========================================================================

-- ---------------------------------------------------------------------------
-- person_aliases.name_en
-- ---------------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'person_aliases'
    AND COLUMN_NAME  = 'name_en'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `person_aliases` ADD COLUMN `name_en` VARCHAR(128) DEFAULT NULL AFTER `name_kana`',
  'SELECT ''person_aliases.name_en already exists, skipping'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- company_aliases.name_en
-- ---------------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'company_aliases'
    AND COLUMN_NAME  = 'name_en'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `company_aliases` ADD COLUMN `name_en` VARCHAR(128) DEFAULT NULL AFTER `name_kana`',
  'SELECT ''company_aliases.name_en already exists, skipping'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- characters.name_en
-- ---------------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'characters'
    AND COLUMN_NAME  = 'name_en'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `characters` ADD COLUMN `name_en` VARCHAR(128) DEFAULT NULL AFTER `name_kana`',
  'SELECT ''characters.name_en already exists, skipping'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- character_aliases.name_en
-- ---------------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'character_aliases'
    AND COLUMN_NAME  = 'name_en'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `character_aliases` ADD COLUMN `name_en` VARCHAR(128) DEFAULT NULL AFTER `name_kana`',
  'SELECT ''character_aliases.name_en already exists, skipping'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SELECT 'v1.2.4 follow-up migration completed: name_en added to person_aliases / company_aliases / characters / character_aliases' AS final_status;
