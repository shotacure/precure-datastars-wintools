-- =====================================================================
-- v1.21.1_series_related_links.sql
--
-- 作品ごとの関連記事（外のサイトの記事など）を持つ表を作る。
-- 作品詳細の「関連記事」の節に、手で入れたこの表の行と、その作品ページへリンクしているコラム（自動）を並べる。
-- この表は、コラムの自動の一覧では拾えない記事（precure.news の記事など、別のサイトのもの）を入れるのに使う。
--   series_related_links
--     link_id        行の ID
--     series_id      作品（→ series.series_id）
--     title          記事の題（表示どおり）
--     url            記事の URL
--     site_label     サイトの名前（precure.news など。題の脇に小さく出す）
--     published_on   記事の日付（題の脇に出す。無ければ出さない）
--     display_order  並び順（小さいほど先）
--     is_enabled     0 にすると出さない
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。行は入れない。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_related_links` (
  `link_id`       int               NOT NULL AUTO_INCREMENT,
  `series_id`     int               NOT NULL COMMENT '作品',
  `title`         varchar(255)      CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '記事の題（表示どおり）',
  `url`           varchar(512)      NOT NULL COMMENT '記事の URL',
  `site_label`    varchar(64)       NOT NULL COMMENT 'サイトの名前（precure.news など）',
  `published_on`  date              DEFAULT NULL COMMENT '記事の日付',
  `display_order` smallint unsigned NOT NULL COMMENT '並び順（小さいほど先）',
  `is_enabled`    tinyint(1)        NOT NULL DEFAULT '1' COMMENT '0 にすると出さない',
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64) DEFAULT NULL,
  `updated_by`    varchar(64) DEFAULT NULL,
  PRIMARY KEY (`link_id`),
  UNIQUE KEY `uq_series_related_links_series_url` (`series_id`, `url`),
  CONSTRAINT `fk_series_related_links_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='作品ごとの関連記事（作品詳細の「関連記事」の節）';
