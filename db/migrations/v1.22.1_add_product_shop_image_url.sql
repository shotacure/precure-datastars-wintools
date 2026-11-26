-- =====================================================================
-- v1.22.1_add_product_shop_image_url.sql
--
-- 音楽商品の販売中の状態（product_shop_states）に、販売中の商品の枠に出す画像の URL の列を足す。
-- 枠の画像は 52px で出すので、Amazon の API が返す小さい画像（medium。160px）を使う。
-- 商品詳細で大きく出す表紙（products.cover_image_url_*。large 500px）とは別に持つ。
--   product_shop_states
--     image_url   販売中の商品の枠に出す画像の URL（AmazonSync の --shop-check が確かめるたびに取り直す）
--
-- 冪等性: INFORMATION_SCHEMA.COLUMNS で列の有無を見てから ALTER する。行の中身は変えない（次の点検で入る）。
-- =====================================================================

SET @col_exists := (
  SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'product_shop_states'
     AND COLUMN_NAME  = 'image_url');

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `product_shop_states` ADD COLUMN `image_url` varchar(512) DEFAULT NULL COMMENT ''販売中の商品の枠に出す画像の URL（Amazon の小さい画像 medium。確かめるたびに取り直す）'' AFTER `availability_type`',
  'SELECT ''product_shop_states.image_url は既にあります'' AS note');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
