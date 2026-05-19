-- =============================================================================
-- v1.3.5 差分マイグレーション（Stage 2）：precures から誕生日カラムを撤去する
-- =============================================================================
--
-- 背景:
--   誕生日は v1.3.5 Stage 1 で persons / characters 側へ移設し、既存 precures の
--   誕生月日は v1.3.5_migration_persons_characters_birthday.sql で対応キャラへ
--   非破壊バックフィル済み。Stage 2 でアプリ側（Precure モデル / PrecuresRepository /
--   PrecureListRow / Catalog プリキュアタブ）が precures.birth_month / birth_day を
--   一切参照しなくなったため、本マイグレーションで物理削除する。
--
-- 前提（重要）:
--   先に v1.3.5_migration_persons_characters_birthday.sql を適用し、characters への
--   バックフィルが完了していること。本スクリプトは characters への移送を行わない
--   （撤去のみ）。順序を誤って先に本スクリプトを実行すると precures の誕生日が
--   失われるため、必ずバックフィル → 本撤去の順で適用する。
--
-- 変更内容:
--   1. CHECK 制約 ck_precures_birth_month / ck_precures_birth_day を削除（存在時）
--   2. precures.birth_month / birth_day カラムを削除（存在時）
--
-- 適用対象:
--   v1.3.5 Stage 1 までを適用済み、かつ characters バックフィル完了の
--   precure_datastars データベース。
--
-- 冪等性:
--   INFORMATION_SCHEMA で制約・カラムの存在を確認してから動的 SQL で DROP
--   （未存在時は DO 0 で素通り）。再実行しても安全。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.3.5_migration_drop_precure_birthday.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;

-- ===== CHECK 制約の削除（カラムより先に外す）=====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'precures'
    AND CONSTRAINT_NAME = 'ck_precures_birth_month' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c > 0,
  'ALTER TABLE `precures` DROP CHECK `ck_precures_birth_month`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'precures'
    AND CONSTRAINT_NAME = 'ck_precures_birth_day' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c > 0,
  'ALTER TABLE `precures` DROP CHECK `ck_precures_birth_day`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- ===== カラムの削除 =====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'precures' AND COLUMN_NAME = 'birth_month');
SET @s := IF(@c > 0,
  'ALTER TABLE `precures` DROP COLUMN `birth_month`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'precures' AND COLUMN_NAME = 'birth_day');
SET @s := IF(@c > 0,
  'ALTER TABLE `precures` DROP COLUMN `birth_day`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
