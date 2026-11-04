-- =====================================================================
-- v1.20.0_add_product_amazon_ean.sql
--
-- 商品（products）に、Amazon の CD の ASIN から取った EAN（JAN、13 桁）を持てるようにする。
--   amazon_ean  VARCHAR(13) NULL   AmazonSync がジャケット画像と同じ GetItems（itemInfo.externalIds.eans）で取る。
--                                  Amazon から取った値なので、画像と同じく毎日の取り直し（cover_image_fetched_at の鮮度）で
--                                  入れ替え、長く据え置かない。取れなかった商品は NULL。
--
-- 用途：商品詳細の構造化データ（Product）の gtin13。盤の mcn（CD から読み取った JAN）があればそちらを使い、無いときにこの列を使う。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_product_amazon_ean;
DELIMITER $$
CREATE PROCEDURE _v1200_add_product_amazon_ean()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products' AND COLUMN_NAME = 'amazon_ean'
  ) THEN
    ALTER TABLE `products`
      ADD COLUMN `amazon_ean` varchar(13) DEFAULT NULL
        COMMENT 'Amazon の CD の ASIN から取った EAN（JAN）。画像と同じく毎日取り直す'
        AFTER `cover_image_fetched_at`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_product_amazon_ean();
DROP PROCEDURE IF EXISTS _v1200_add_product_amazon_ean;
