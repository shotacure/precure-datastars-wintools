-- =============================================================================
-- Migration: v1.1.0 -> v1.1.1 series_id の所在を products から discs へ移設
-- =============================================================================
-- 背景:
--   v1.1.0 までは「所属シリーズ」は商品 (products) 単位で持っていたが、
--   1 商品内に複数シリーズのディスクが混在するケース（シリーズ合同盤、特典等）
--   の表現ができず、また「シリーズごとに 1 枚だけディスクがある」といった
--   構造と噛み合わない状態だった。本来シリーズ所属はディスク (discs) の属性
--   であるべきなので、v1.1.1 で所在を移し替える。
--
-- 適用対象:
--   v1.1.0 運用中の `precure_datastars` データベース（v1.1.0 マイグレーション
--   適用済みで、products テーブルに series_id 列が存在する環境）。
--
-- 変更内容:
--   1. discs に series_id 列 + FK + インデックスを追加（NULL 許容）
--   2. discs.series_id に products.series_id の値をコピー（product_catalog_no で JOIN）
--   3. products から series_id 列 + FK + インデックスを削除
--
-- 安全性:
--   - 本スクリプトは **1 回限りの ALTER を前提** とする。既に discs.series_id が
--     存在する環境（= 2 回目以降の実行）では各ステップを INFORMATION_SCHEMA で
--     確認してスキップするため、冪等に流せる。
--   - products.series_id → discs.series_id の値コピーは、1 products 対 N discs
--     の N 枚すべてに同じ series_id が乗る。v1.1.0 時点の運用では「1 商品 = 1
--     シリーズ」だったため、情報損失は発生しない。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_move_series_id_to_disc.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;
SET FOREIGN_KEY_CHECKS = 0;  -- 既存データに対する ALTER で一時的に無効化
SET SQL_SAFE_UPDATES   = 0;  -- WHERE 条件がサブクエリのみの UPDATE を Error 1175 から守る

-- -----------------------------------------------------------------------------
-- STEP 1: discs に series_id 列を追加（既に存在していればスキップ）
-- -----------------------------------------------------------------------------
-- 追加位置は title_en の直後（v1.1.1 版の schema.sql と列順を揃える）。
-- NULL 許容。NULL はオールスターズ（複数シリーズ合同）扱い。
SET @has_discs_series_id = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'discs'
     AND COLUMN_NAME  = 'series_id'
);
SET @stmt = IF(@has_discs_series_id > 0,
  'DO 0',
  'ALTER TABLE `discs`
     ADD COLUMN `series_id` int DEFAULT NULL AFTER `title_en`'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 2: discs.series_id に対するインデックスを追加（既に存在していればスキップ）
-- -----------------------------------------------------------------------------
SET @has_ix_discs_series = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'discs'
     AND INDEX_NAME   = 'ix_discs_series'
);
SET @stmt = IF(@has_ix_discs_series > 0,
  'DO 0',
  'ALTER TABLE `discs` ADD KEY `ix_discs_series` (`series_id`)'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 3: discs.series_id に対する FK を追加（既に存在していればスキップ）
-- -----------------------------------------------------------------------------
-- 親 series が削除された場合、所属解除（NULL=オールスターズ扱い）で耐える。
-- series_id の更新は CASCADE（主キー変更に追従）。
SET @has_fk_discs_series = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA    = DATABASE()
     AND TABLE_NAME      = 'discs'
     AND CONSTRAINT_NAME = 'fk_discs_series'
);
SET @stmt = IF(@has_fk_discs_series > 0,
  'DO 0',
  'ALTER TABLE `discs`
     ADD CONSTRAINT `fk_discs_series`
     FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`)
     ON DELETE SET NULL ON UPDATE CASCADE'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 4: products.series_id の値を discs.series_id にコピー
-- -----------------------------------------------------------------------------
-- products テーブルにまだ series_id が残っているときのみ実行する。
-- 既に products.series_id を落としたあと（= 2 回目以降の実行）はスキップ。
-- discs.series_id に既に非 NULL 値が入っている行は上書きしない（最新値優先）。
SET @has_products_series_id = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'series_id'
);
SET @stmt = IF(@has_products_series_id = 0,
  'DO 0',
  'UPDATE `discs` d
     JOIN `products` p ON p.product_catalog_no = d.product_catalog_no
      SET d.series_id = p.series_id
    WHERE d.series_id IS NULL'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 5: products から series_id 関連オブジェクトを撤去
-- -----------------------------------------------------------------------------
-- 5-a: FK 制約を先に落とす（FK を残したまま列を DROP すると Error 3780）
SET @has_fk_products_series = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA    = DATABASE()
     AND TABLE_NAME      = 'products'
     AND CONSTRAINT_NAME = 'fk_products_series'
);
SET @stmt = IF(@has_fk_products_series = 0,
  'DO 0',
  'ALTER TABLE `products` DROP FOREIGN KEY `fk_products_series`'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 5-b: インデックスを落とす（FK のインデックスは FK 削除後に落とせる）
SET @has_ix_products_series = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND INDEX_NAME   = 'ix_products_series'
);
SET @stmt = IF(@has_ix_products_series = 0,
  'DO 0',
  'ALTER TABLE `products` DROP INDEX `ix_products_series`'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 5-c: 列そのものを落とす
SET @stmt = IF(@has_products_series_id = 0,
  'DO 0',
  'ALTER TABLE `products` DROP COLUMN `series_id`'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- 後片付け
-- -----------------------------------------------------------------------------
SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;

-- Migration v1.1.1 completed
