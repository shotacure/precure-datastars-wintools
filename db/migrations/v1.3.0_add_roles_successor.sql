-- =============================================================================
-- Migration: v1.3.0 — roles テーブルに successor_role_code 列を追加
-- =============================================================================
-- 背景:
--   役職マスタ（roles）の系譜（変更元 → 変更先）を表現するため、
--   successor_role_code 列を追加する。1 つの役職は最大 1 つの後継役職を指す。
--   B → A, C → A のように複数の役職が同じ A を指せば、A は B/C の統合先となる
--   （統合）。1 つの A から B/C 両方を指すことは構造上できないので、分岐は
--   逆向きで表現する。
--
--   関係性ツリー（クラスタ）：role_code → successor_role_code の有向リンクを
--   辿って到達できる役職をすべて同一クラスタとみなす。クラスタの代表は
--   末端（successor_role_code IS NULL）のうち display_order 最小の役職。
--   統計集計では同一クラスタ内のすべての role_code を 1 つにまとめてカウントする。
--
--   このマイグレでは列追加と FK 制約のみを行う。
--   各役職に successor を設定するのは運用者が GUI から個別に行う想定。
--
-- 対象列:
--   roles.successor_role_code  VARCHAR(64)  NULL
--     FK → roles.role_code
--     ON UPDATE CASCADE / ON DELETE SET NULL
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で列の存在を確認してから ALTER TABLE を発行する
--   - FK の存在も INFORMATION_SCHEMA.TABLE_CONSTRAINTS で確認する
--   - 再実行しても同じ最終状態になる
-- =============================================================================

-- 列の存在を確認
SET @has_successor = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'roles'
    AND COLUMN_NAME  = 'successor_role_code'
);

-- 列が無ければ追加（display_order の直後に並べる）。
SET @stmt = IF(@has_successor = 0,
  'ALTER TABLE `roles` ADD COLUMN `successor_role_code` VARCHAR(64) NULL AFTER `display_order`',
  'SELECT ''roles.successor_role_code already exists, skipping ALTER'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- FK の存在を確認
SET @has_fk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME       = 'roles'
    AND CONSTRAINT_NAME  = 'fk_roles_successor'
    AND CONSTRAINT_TYPE  = 'FOREIGN KEY'
);

-- FK が無ければ追加。自己参照 FK で、変更先役職が削除されたら NULL に戻す。
-- ON UPDATE CASCADE は role_code が変わった場合に追従させる安全側設定。
SET @stmt = IF(@has_fk = 0,
  'ALTER TABLE `roles` ADD CONSTRAINT `fk_roles_successor` FOREIGN KEY (`successor_role_code`) REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE SET NULL',
  'SELECT ''fk_roles_successor already exists, skipping'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: roles.successor_role_code added' AS final_status;
