
-- =============================================================================
-- Migration: v1.3.0 — roles.successor_role_code 列を削除（ロールバック）
-- =============================================================================
-- 背景:
--   v1.3.0_add_roles_successor.sql で追加した successor_role_code 列は
--   1 対 1 関係しか表現できず、役職の分裂を扱えないため廃止する。
--   後継は v1.3.0_add_role_successions.sql の role_successions テーブル（多対多）
--   に置き換わる。
--
--   v1.3.0_add_roles_successor.sql 適用済み環境向けに本ファイルでロールバックする。
--   未適用環境では列も FK も存在しないので、本マイグレーションは何もせず終了する。
--
--   既存データ（successor_role_code に値が入っていた場合）は
--   v1.3.0_add_role_successions.sql の中で role_successions に移送済みなので、
--   このタイミングで列を消しても情報は失わない。
--
-- 実行順序:
--   1) v1.3.0_add_role_successions.sql （データ移送＋新テーブル作成）
--   2) v1.3.0_drop_roles_successor.sql  （旧列削除）  ← 本ファイル
--   この順を必ず守る。
--
-- 冪等性:
--   - INFORMATION_SCHEMA で FK と列の存在をチェックしてから DROP
--   - 再実行しても同じ最終状態
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- 先に FK を削除（列削除より FK 削除を先にしないとエラーになる MySQL 仕様）。
SET @has_fk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA     = DATABASE()
    AND TABLE_NAME       = 'roles'
    AND CONSTRAINT_NAME  = 'fk_roles_successor'
    AND CONSTRAINT_TYPE  = 'FOREIGN KEY'
);

SET @stmt = IF(@has_fk = 1,
  'ALTER TABLE `roles` DROP FOREIGN KEY `fk_roles_successor`',
  'SELECT ''fk_roles_successor does not exist, skipping DROP FK'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- 列を削除。
SET @has_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'roles'
    AND COLUMN_NAME  = 'successor_role_code'
);

SET @stmt = IF(@has_col = 1,
  'ALTER TABLE `roles` DROP COLUMN `successor_role_code`',
  'SELECT ''roles.successor_role_code does not exist, skipping DROP COLUMN'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: roles.successor_role_code dropped' AS final_status;
