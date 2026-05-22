-- =====================================================================
-- v1.4.0_split_amazon_asin.sql
--
-- Amazon ASIN 列を 2 列に分割するマイグレーション。
--   旧: amazon_asin VARCHAR(16) NULL
--   新: amazon_asin_cd      VARCHAR(16) NULL  ... 物理パッケージ（CD/BD/DVD）向け
--       amazon_asin_digital VARCHAR(16) NULL  ... デジタル音源（Amazon Music）向け
--
-- 旧 amazon_asin の値は一律「デジタル音源側」(amazon_asin_digital) に寄せる。
-- 物理側は新たに PA-API 連携や手入力で順次埋めていく運用とする。
--
-- 冪等性のため、各 ALTER は INFORMATION_SCHEMA で対象列の存在確認をしてから実行する。
-- 既に v1.4.0 を適用済みの環境で再実行しても安全に no-op となるよう作ってある。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 1. amazon_asin_cd 列を追加（未追加時のみ）
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'products'
    AND COLUMN_NAME  = 'amazon_asin_cd'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE products ADD COLUMN `amazon_asin_cd` VARCHAR(16) NULL AFTER `distributor_product_company_id`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. amazon_asin_digital 列を追加（未追加時のみ）
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'products'
    AND COLUMN_NAME  = 'amazon_asin_digital'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE products ADD COLUMN `amazon_asin_digital` VARCHAR(16) NULL AFTER `amazon_asin_cd`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. 旧 amazon_asin の値をデジタル側に移送（旧列が存在する場合のみ）
--    既存値がデジタル ASIN だった想定で寄せる。物理側は別途 PA-API 等で補完する。
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'products'
    AND COLUMN_NAME  = 'amazon_asin'
);
SET @sql := IF(@col_exists = 1,
  'UPDATE products
     SET amazon_asin_digital = amazon_asin
   WHERE amazon_asin IS NOT NULL
     AND amazon_asin <> ''''
     AND amazon_asin_digital IS NULL',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 4. 旧 amazon_asin 列を削除（存在する場合のみ）
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'products'
    AND COLUMN_NAME  = 'amazon_asin'
);
SET @sql := IF(@col_exists = 1,
  'ALTER TABLE products DROP COLUMN `amazon_asin`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

COMMIT;
