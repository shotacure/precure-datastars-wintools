-- =====================================================================
-- v1.20.1_series_shop_items.sql
--
-- 作品ごとの「販売中の商品」を持つ表を作る。その作品を主とするページ（作品詳細・各話の詳細）と
-- トップページは、この表の行のうち、Amazon で売られている行だけを小さな枠で出す。
-- 売られているかは AmazonSync（--shop-check）が Creators API で確かめ、行に書き戻す。
--   series_shop_items
--     shop_item_id        行の ID
--     series_id           作品（→ series.series_id）
--     asin                Amazon の ASIN
--     title               枠に出す商品名
--     media_label         媒体の札（Blu-ray・CD など）
--     product_catalog_no  音楽商品なら、その商品（→ products.product_catalog_no）。商品詳細へのリンクに使う
--     release_date        発売日（予約受付中の行に添える）
--     display_order       並び順（小さいほど先）
--     is_enabled          0 にすると、売られていても出さない
--     is_buyable          売られているか（1＝出す・0＝出さない・NULL＝まだ確かめていない）
--     availability_type   Amazon が返した在庫の種別（IN_STOCK・PREORDER など）
--     image_url           商品画像の URL（Amazon のもの。確かめるたびに取り直す）
--     checked_at          最後に確かめた時刻
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。行は入れない。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_shop_items` (
  `shop_item_id`       int               NOT NULL AUTO_INCREMENT,
  `series_id`          int               NOT NULL COMMENT '作品',
  `asin`               varchar(16)       NOT NULL COMMENT 'Amazon の ASIN',
  `title`              varchar(255)      CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '枠に出す商品名',
  `media_label`        varchar(32)       NOT NULL COMMENT '媒体の札（Blu-ray・CD など）',
  `product_catalog_no` varchar(32)       CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL COMMENT '音楽商品なら、その商品。商品詳細へのリンクに使う',
  `release_date`       date              DEFAULT NULL COMMENT '発売日（予約受付中の行に添える）',
  `display_order`      smallint unsigned NOT NULL COMMENT '並び順（小さいほど先）',
  `is_enabled`         tinyint(1)        NOT NULL DEFAULT '1' COMMENT '0 にすると、売られていても出さない',
  `is_buyable`         tinyint(1)        DEFAULT NULL COMMENT '売られているか（1＝出す・0＝出さない・NULL＝まだ確かめていない）',
  `availability_type`  varchar(32)       DEFAULT NULL COMMENT 'Amazon が返した在庫の種別（IN_STOCK・PREORDER など）',
  `image_url`          varchar(512)      DEFAULT NULL COMMENT '商品画像の URL（Amazon のもの。確かめるたびに取り直す）',
  `checked_at`         datetime          DEFAULT NULL COMMENT '最後に確かめた時刻',
  `created_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`         varchar(64) DEFAULT NULL,
  `updated_by`         varchar(64) DEFAULT NULL,
  PRIMARY KEY (`shop_item_id`),
  UNIQUE KEY `uq_series_shop_items_series_asin` (`series_id`, `asin`),
  KEY `ix_series_shop_items_product` (`product_catalog_no`),
  CONSTRAINT `fk_series_shop_items_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_shop_items_product` FOREIGN KEY (`product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='作品ごとの販売中の商品（作品詳細・各話の詳細・トップページの枠）';
