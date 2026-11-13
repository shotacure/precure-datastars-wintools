-- =====================================================================
-- v1.20.1_product_shop_states.sql
--
-- 音楽商品が Amazon で売られているかの状態を持つ表を作る。
-- 「販売中の商品」の枠（作品詳細・各話の詳細・トップページ）は、作品に結び付く音楽商品のうち、
-- CD の ASIN があり、この表で売られているとされた商品を候補にする。
-- 状態は AmazonSync（--shop-check）が Creators API で確かめて書く。商品の表（products）には書かない
-- （点検のたびに商品の更新日時が動かないようにするため）。
--   product_shop_states
--     product_catalog_no  商品（→ products.product_catalog_no）
--     is_buyable          売られているか（1＝候補にする・0＝しない）
--     availability_type   Amazon が返した在庫の種別（IN_STOCK・PREORDER など）
--     checked_at          最後に確かめた時刻
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。行は入れない。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `product_shop_states` (
  `product_catalog_no` varchar(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '商品',
  `is_buyable`         tinyint(1)   NOT NULL COMMENT '売られているか（1＝候補にする・0＝しない）',
  `availability_type`  varchar(32)  DEFAULT NULL COMMENT 'Amazon が返した在庫の種別（IN_STOCK・PREORDER など）',
  `checked_at`         datetime     NOT NULL COMMENT '最後に確かめた時刻',
  PRIMARY KEY (`product_catalog_no`),
  CONSTRAINT `fk_product_shop_states_product` FOREIGN KEY (`product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='音楽商品が Amazon で売られているかの状態（販売中の商品の枠の候補）';
