-- =============================================================================
-- Migration: v1.2.0 -> v1.2.1
--   character_aliases テーブルから valid_from / valid_to を削除する。
-- =============================================================================
-- 背景:
--   v1.2.0 工程 H-10 でロール書式（role_templates）系の valid_from/valid_to を撤去
--   して以来、キャラクター名義の有効期間も実運用上はまったく利用していなかった。
--   表記揺れ（"美墨なぎさ" / "キュアブラック" / "ブラック"）は別 alias 行として
--   並存させ、声優キャスティング側で REGULAR / SUBSTITUTE / TEMPORARY / MOB と
--   期間管理しているため、alias 自体に期間情報を持たせる必要は無い。
--   むしろ「いつから使い始めたか」「いつから使われなくなったか」が空欄のまま
--   登録されるケースばかりで、UI 上の入力負荷だけが残っていた。
--
-- v1.2.1 でクレジット名寄せ機能（alias の付け替え／改名ダイアログ）を追加するに
-- あたり、UI を簡素化したいので、本マイグレーションで物理列ごと撤去する。
--
-- 本スクリプトは INFORMATION_SCHEMA で列の存在を確認してから ALTER する冪等形式。
-- 何度実行しても同じ結果になる（既に削除済みなら no-op）。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.1_drop_character_aliases_valid_dates.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- -----------------------------------------------------------------------------
-- STEP 1: character_aliases.valid_from 列の DROP
--   既存環境にしか列は無いため、COLUMNS ビューで存在確認してから ALTER する。
-- -----------------------------------------------------------------------------
SET @has_valid_from = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'character_aliases'
    AND COLUMN_NAME  = 'valid_from'
);

SET @stmt = IF(@has_valid_from = 1,
  'ALTER TABLE `character_aliases` DROP COLUMN `valid_from`',
  'SELECT ''character_aliases.valid_from already removed. skipping DROP COLUMN.'' AS msg');

PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 2: character_aliases.valid_to 列の DROP
-- -----------------------------------------------------------------------------
SET @has_valid_to = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'character_aliases'
    AND COLUMN_NAME  = 'valid_to'
);

SET @stmt = IF(@has_valid_to = 1,
  'ALTER TABLE `character_aliases` DROP COLUMN `valid_to`',
  'SELECT ''character_aliases.valid_to already removed. skipping DROP COLUMN.'' AS msg');

PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

SELECT 'v1.2.1 migration completed: character_aliases.valid_from/valid_to dropped.' AS status;
