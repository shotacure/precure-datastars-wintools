
-- =============================================================================
-- Migration: v1.3.0 — role_successions 関係テーブル新設（多対多版）
-- =============================================================================
-- 背景:
--   役職の系譜（変更元 → 変更先）を当初は roles.successor_role_code カラム
--   （v1.3.0_add_roles_successor.sql）で 1 対 1 表現していたが、現実の役職は
--   分裂（A → B かつ A → C）や併合（B → A かつ C → A）を伴うため、
--   1 対 1 のカラムでは表現力が不足する。
--
--   そこで多対多の関係テーブル role_successions を導入する。
--   既存の roles.successor_role_code は v1.3.0_drop_roles_successor.sql で
--   後段ロールバックされる前提（本マイグレーションでは扱わない）。
--
--   関係性ツリー（クラスタ）:
--     from_role_code → to_role_code の有向辺を「無向辺」とみなして連結成分を
--     たどり、到達できる役職全部を同一クラスタとする。
--     クラスタ代表は display_order が最小の役職（同点は role_code 昇順）。
--     統計集計では同一クラスタ内のすべての role_code を 1 つにまとめてカウントする。
--
-- 対象表:
--   role_successions
--     from_role_code VARCHAR(32) NOT NULL  FK → roles.role_code (CASCADE/CASCADE)
--     to_role_code   VARCHAR(32) NOT NULL  FK → roles.role_code (CASCADE/CASCADE)
--     PRIMARY KEY (from_role_code, to_role_code)
--
-- 自己ループ防止:
--   from_role_code = to_role_code の行は意味的に不正だが、CHECK 制約として
--   持たせると MySQL 8 では FK の参照アクション（CASCADE 等）で変更される列を
--   CHECK 内で参照できない（Error 3823）ため、CHECK は付けない。
--   代わりに RoleSuccessionsRepository.UpsertAsync の入口で自己ループをガードする
--   方針（アプリ層防御）。
--
-- 冪等性:
--   - INFORMATION_SCHEMA.TABLES でテーブル存在を確認してから CREATE する
--   - 再実行しても同じ最終状態
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

SET @has_table = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'role_successions'
);

-- テーブルが無ければ CREATE。
-- role_code は roles テーブルに合わせて VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin。
-- ON UPDATE CASCADE で role_code リネームに追従、ON DELETE CASCADE で役職削除時に系譜も消える。
SET @stmt = IF(@has_table = 0, '
  CREATE TABLE `role_successions` (
    `from_role_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `to_role_code`   varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `notes`          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
    `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
    `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `created_by`     varchar(64) DEFAULT NULL,
    `updated_by`     varchar(64) DEFAULT NULL,
    PRIMARY KEY (`from_role_code`, `to_role_code`),
    KEY `idx_role_successions_to` (`to_role_code`),
    CONSTRAINT `fk_role_successions_from` FOREIGN KEY (`from_role_code`) REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT `fk_role_successions_to`   FOREIGN KEY (`to_role_code`)   REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE CASCADE
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
', 'SELECT ''role_successions table already exists, skipping CREATE'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- 旧 roles.successor_role_code 列がまだ残っていれば、その内容をこの新テーブルに移送する。
-- v1.3.0_add_roles_successor.sql を適用済みかつ運用者が値を入れた環境への配慮。
-- 列が無い環境では何もしない。自己ループ（万一あれば）は WHERE で弾く。
SET @has_old_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'roles'
    AND COLUMN_NAME  = 'successor_role_code'
);

SET @stmt = IF(@has_old_col = 1,
  'INSERT IGNORE INTO `role_successions` (from_role_code, to_role_code, notes, created_by, updated_by)
   SELECT role_code, successor_role_code, ''migrated from roles.successor_role_code'', ''v1.3.0_migration'', ''v1.3.0_migration''
     FROM `roles`
    WHERE successor_role_code IS NOT NULL
      AND successor_role_code <> role_code',
  'SELECT ''roles.successor_role_code does not exist, no data migration'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: role_successions table added (and old data migrated if needed)' AS final_status;
