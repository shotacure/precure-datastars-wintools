-- ===================================================================
--  v1.2.0 工程 H-10：credit_kinds マスタ化 + role_templates 統合
-- -------------------------------------------------------------------
--  本マイグレーションは v1.2.0_add_credits.sql を流した後に実行する。
--  冪等：再実行しても安全（INFORMATION_SCHEMA で各オブジェクトの存在を確認してから ALTER）。
--
--  実施内容：
--    (1) credit_kinds テーブル新設 + 'OP' / 'ED' のシード投入
--    (2) credits.credit_kind を ENUM('OP','ED') → VARCHAR(16) に変更し credit_kinds への FK を追加
--    (3) part_types.default_credit_kind を ENUM('OP','ED') → VARCHAR(16) に変更し credit_kinds への FK を追加
--    (4) role_templates テーブル新設（既定とシリーズ別を統合した単一テーブル設計）
--    (5) roles.default_format_template の既存値を role_templates(series_id=NULL) に移送
--    (6) series_role_format_overrides の既存値を role_templates(series_id=...) に移送
--    (7) roles.default_format_template 列を DROP
--    (8) series_role_format_overrides テーブルを DROP
--
--  コマンド例：
--    mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.0_h10_credit_kinds_and_role_templates.sql
-- ===================================================================

USE `precure_datastars`;

SET NAMES utf8mb4;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- -------------------------------------------------------------------
-- STEP 1: credit_kinds マスタテーブル新設
-- -------------------------------------------------------------------
-- 旧設計では credits.credit_kind と part_types.default_credit_kind が ENUM('OP','ED') 直書きで、
-- 表示名（オープニングクレジット／エンディングクレジット）の i18n や display_order などを
-- 持てなかった。マスタテーブルに移して柔軟性を確保する。

CREATE TABLE IF NOT EXISTS `credit_kinds` (
  `kind_code`     VARCHAR(16)  NOT NULL,
  `name_ja`       VARCHAR(64)  NOT NULL,
  `name_en`       VARCHAR(64)      NULL,
  `display_order` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  `notes`         TEXT             NULL,
  `created_at`    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `created_by`    VARCHAR(64)      NULL,
  `updated_by`    VARCHAR(64)      NULL,
  PRIMARY KEY (`kind_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='クレジット種別マスタ（OP/ED 等）。v1.2.0 工程 H-10 で導入。';

-- シード投入（INSERT IGNORE で再実行時も衝突しない）
INSERT IGNORE INTO `credit_kinds` (`kind_code`, `name_ja`, `name_en`, `display_order`, `created_by`, `updated_by`) VALUES
  ('OP', 'オープニングクレジット', 'Opening Credits', 10, 'migration_h10', 'migration_h10'),
  ('ED', 'エンディングクレジット', 'Ending Credits', 20, 'migration_h10', 'migration_h10');

-- -------------------------------------------------------------------
-- STEP 2: credits.credit_kind を ENUM → VARCHAR + FK 化
-- -------------------------------------------------------------------
-- 列型変更の前に、念のため既存値が credit_kinds に存在することを保証
-- （ENUM の許容値である 'OP'/'ED' は STEP 1 でシード済み）。

-- 既に VARCHAR 化済みかをチェック
SET @col_type := (
  SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND COLUMN_NAME = 'credit_kind'
);
SET @sql := IF(@col_type = 'enum',
  'ALTER TABLE `credits` MODIFY COLUMN `credit_kind` VARCHAR(16) NOT NULL',
  'SELECT ''credits.credit_kind already converted to VARCHAR'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- FK 追加（既存ならスキップ）
SET @fk_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY' AND CONSTRAINT_NAME = 'fk_credits_credit_kind'
);
SET @sql := IF(@fk_exists = 0,
  'ALTER TABLE `credits`
     ADD CONSTRAINT `fk_credits_credit_kind`
     FOREIGN KEY (`credit_kind`) REFERENCES `credit_kinds`(`kind_code`)
     ON UPDATE CASCADE ON DELETE RESTRICT',
  'SELECT ''fk_credits_credit_kind already exists'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------------
-- STEP 3: part_types.default_credit_kind を ENUM → VARCHAR + FK 化
-- -------------------------------------------------------------------

SET @col_type := (
  SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'part_types' AND COLUMN_NAME = 'default_credit_kind'
);
SET @sql := IF(@col_type = 'enum',
  'ALTER TABLE `part_types` MODIFY COLUMN `default_credit_kind` VARCHAR(16) NULL',
  'SELECT ''part_types.default_credit_kind already converted to VARCHAR'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @fk_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'part_types'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY' AND CONSTRAINT_NAME = 'fk_part_types_default_credit_kind'
);
SET @sql := IF(@fk_exists = 0,
  'ALTER TABLE `part_types`
     ADD CONSTRAINT `fk_part_types_default_credit_kind`
     FOREIGN KEY (`default_credit_kind`) REFERENCES `credit_kinds`(`kind_code`)
     ON UPDATE CASCADE ON DELETE SET NULL',
  'SELECT ''fk_part_types_default_credit_kind already exists'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------------
-- STEP 4: role_templates テーブル新設
-- -------------------------------------------------------------------
-- 旧設計：roles.default_format_template（既定）+ series_role_format_overrides（オーバーライド）
-- の二箇所運用が美しくなかったため、(role_code, series_id) の単一テーブルに統合する。
--   - series_id IS NULL ：既定テンプレ（全シリーズ共通）
--   - series_id IS NOT NULL：そのシリーズ専用のテンプレ
-- 解決ロジック：(role_code, series_id) で検索 → 無ければ (role_code, NULL) にフォールバック。
--
-- 期間制限（valid_from/valid_to）は当面持たない。シンプルさを優先（v1.2.0 工程 H-10 仕様）。

-- 注：role_code 列は roles.role_code（VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin）と
-- 完全一致させる必要がある。不一致だと MySQL の FK 互換チェック（Error 3780）でエラーになる。
-- 既存 DB の roles テーブルは長さ 32、binary collation で運用されているためここでも揃える。
CREATE TABLE IF NOT EXISTS `role_templates` (
  `template_id`     INT          NOT NULL AUTO_INCREMENT,
  `role_code`       VARCHAR(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `series_id`       INT              NULL,
  `format_template` TEXT         NOT NULL,
  `notes`           TEXT             NULL,
  `created_at`      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `created_by`      VARCHAR(64)      NULL,
  `updated_by`      VARCHAR(64)      NULL,
  PRIMARY KEY (`template_id`),
  -- (role_code, series_id) 一意。series_id=NULL 行は MySQL の UNIQUE 仕様で複数 NULL を許容するが、
  -- アプリ側で「既定は role_code につき 1 件まで」を保証する責務を持つ（リポジトリの UpsertAsync で実装）。
  UNIQUE KEY `uk_role_templates_role_series` (`role_code`, `series_id`),
  KEY `ix_role_templates_role` (`role_code`),
  KEY `ix_role_templates_series` (`series_id`),
  CONSTRAINT `fk_role_templates_role`
    FOREIGN KEY (`role_code`) REFERENCES `roles`(`role_code`)
    ON UPDATE CASCADE ON DELETE CASCADE,
  CONSTRAINT `fk_role_templates_series`
    FOREIGN KEY (`series_id`) REFERENCES `series`(`series_id`)
    ON UPDATE CASCADE ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='役職テンプレート（既定とシリーズ別を統合）。v1.2.0 工程 H-10 で導入。';

-- -------------------------------------------------------------------
-- STEP 5: roles.default_format_template の既存値を role_templates(series_id=NULL) に移送
-- -------------------------------------------------------------------
-- 列が既に DROP 済みなら何もしない。残っていれば値を吸い出して新テーブルに INSERT IGNORE。

SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'roles' AND COLUMN_NAME = 'default_format_template'
);
SET @sql := IF(@col_exists = 1,
  'INSERT IGNORE INTO `role_templates` (`role_code`, `series_id`, `format_template`, `created_by`, `updated_by`)
     SELECT r.`role_code`, NULL, r.`default_format_template`, ''migration_h10'', ''migration_h10''
       FROM `roles` r
      WHERE r.`default_format_template` IS NOT NULL
        AND r.`default_format_template` <> ''''',
  'SELECT ''roles.default_format_template column already dropped, skipping migration'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------------
-- STEP 6: series_role_format_overrides の既存値を role_templates(series_id=...) に移送
-- -------------------------------------------------------------------
-- テーブルが既に DROP 済みなら何もしない。残っていれば値を吸い出して新テーブルに INSERT IGNORE。
-- 旧テーブルは (series_id, role_code, valid_from) の 3 列複合 PK だが、新テーブルでは valid_from を
-- 持たず (series_id, role_code) UNIQUE 設計なので、同じ (series, role) で複数 valid_from がある場合は
-- valid_from が最も新しい行のみを採用する（業務的にも「最新の書式」を残すのが自然）。

SET @table_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_role_format_overrides'
);
SET @sql := IF(@table_exists = 1,
  'INSERT IGNORE INTO `role_templates` (`role_code`, `series_id`, `format_template`, `created_by`, `updated_by`)
     SELECT o.`role_code`, o.`series_id`, o.`format_template`, ''migration_h10'', ''migration_h10''
       FROM `series_role_format_overrides` o
       JOIN (
         SELECT `series_id`, `role_code`, MAX(`valid_from`) AS max_vf
           FROM `series_role_format_overrides`
          GROUP BY `series_id`, `role_code`
       ) latest
         ON latest.`series_id` = o.`series_id`
        AND latest.`role_code` = o.`role_code`
        AND latest.`max_vf`    = o.`valid_from`',
  'SELECT ''series_role_format_overrides table already dropped, skipping migration'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------------
-- STEP 7: roles.default_format_template 列を DROP
-- -------------------------------------------------------------------

SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'roles' AND COLUMN_NAME = 'default_format_template'
);
SET @sql := IF(@col_exists = 1,
  'ALTER TABLE `roles` DROP COLUMN `default_format_template`',
  'SELECT ''roles.default_format_template already dropped'' AS info');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------------
-- STEP 8: series_role_format_overrides テーブルを DROP
-- -------------------------------------------------------------------

DROP TABLE IF EXISTS `series_role_format_overrides`;

-- -------------------------------------------------------------------
-- 完了
-- -------------------------------------------------------------------

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;

SELECT 'v1.2.0 H-10 migration completed.' AS status;
