-- =====================================================================
-- v1.20.0_add_more_external_urls.sql
--
-- 人物・企業の外部リンクに 6 列を足す。
--   persons    + 6 列: link_collection_url / x_sub_url / bluesky_url / blog_url / pixiv_url / portfolio_url
--   companies  + 6 列: 同上 6 列
--
--   link_collection_url  リンク集のページ（lit.link・linktr.ee など）
--   x_sub_url      2 つ目の X (Twitter) アカウント（言語違い・個人と仕事の別など）
--   bluesky_url    Bluesky プロフィール
--   blog_url       ブログ（note・tumblr など）
--   pixiv_url      pixiv のユーザーページ
--   portfolio_url  作品集のページ（ArtStation・Behance・carrd など）
--
-- 全列とも VARCHAR(1024) NULL。詳細ページの末尾「外部リンク」セクションに並ぶ。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_url_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1200_add_url_col_if_missing(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_col;
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

-- ---------------------------------------------------------------------
-- persons + 6 列
-- ---------------------------------------------------------------------
CALL _v1200_add_url_col_if_missing('persons', 'link_collection_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'リンク集のページ URL（lit.link・linktr.ee など）' AFTER `affiliation_url`");
CALL _v1200_add_url_col_if_missing('persons', 'x_sub_url',     "VARCHAR(1024) DEFAULT NULL COMMENT '2 つ目の X (Twitter) プロフィール URL（言語違いなど）' AFTER `x_url`");
CALL _v1200_add_url_col_if_missing('persons', 'bluesky_url',   "VARCHAR(1024) DEFAULT NULL COMMENT 'Bluesky プロフィール URL' AFTER `x_sub_url`");
CALL _v1200_add_url_col_if_missing('persons', 'blog_url',      "VARCHAR(1024) DEFAULT NULL COMMENT 'ブログ URL（note・tumblr など）' AFTER `youtube_url`");
CALL _v1200_add_url_col_if_missing('persons', 'pixiv_url',     "VARCHAR(1024) DEFAULT NULL COMMENT 'pixiv ユーザーページ URL' AFTER `blog_url`");
CALL _v1200_add_url_col_if_missing('persons', 'portfolio_url', "VARCHAR(1024) DEFAULT NULL COMMENT '作品集のページ URL（ArtStation・Behance・carrd など）' AFTER `pixiv_url`");

-- ---------------------------------------------------------------------
-- companies + 6 列
-- ---------------------------------------------------------------------
CALL _v1200_add_url_col_if_missing('companies', 'link_collection_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'リンク集のページ URL（lit.link・linktr.ee など）' AFTER `official_url`");
CALL _v1200_add_url_col_if_missing('companies', 'x_sub_url',     "VARCHAR(1024) DEFAULT NULL COMMENT '2 つ目の X (Twitter) アカウント URL（言語違いなど）' AFTER `x_url`");
CALL _v1200_add_url_col_if_missing('companies', 'bluesky_url',   "VARCHAR(1024) DEFAULT NULL COMMENT 'Bluesky アカウント URL' AFTER `x_sub_url`");
CALL _v1200_add_url_col_if_missing('companies', 'blog_url',      "VARCHAR(1024) DEFAULT NULL COMMENT 'ブログ URL（note など）' AFTER `youtube_url`");
CALL _v1200_add_url_col_if_missing('companies', 'pixiv_url',     "VARCHAR(1024) DEFAULT NULL COMMENT 'pixiv ユーザーページ URL' AFTER `blog_url`");
CALL _v1200_add_url_col_if_missing('companies', 'portfolio_url', "VARCHAR(1024) DEFAULT NULL COMMENT '作品集のページ URL（ArtStation・Behance など）' AFTER `pixiv_url`");

DROP PROCEDURE IF EXISTS _v1200_add_url_col_if_missing;
