-- =====================================================================
-- v1.16.1_add_block_heading.sql
--
-- クレジットのブロック（credit_role_blocks）の先頭に出す「見出し」を持てるようにする。
--   heading_series_id  INT NULL           見出しにする作品（series.series_id）。
--   heading_text       VARCHAR(128) NULL  見出しの文字（画面の表記どおり）。
--
-- 複数の作品のキャラクターが並ぶ映画の声の出演で、作品ごとのまとまりの頭に
-- 「フレッシュプリキュア！」のような作品名が出る（プリキュアオールスターズDX から）。
-- 作品を指すときは heading_series_id を使い、サイトでは作品ページへリンクする。
-- 画面の表記が作品の正式タイトルと違うときは heading_text に画面どおりの文字を入れる。
-- 作品ではない見出し（「特別出演」など）は heading_text だけを使う。
-- 作品の ID を振り直したときは追従し（ON UPDATE CASCADE）、作品を消したときは指定を外す（ON DELETE SET NULL）。
--
-- 冪等性: 列・外部キーごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1161_add_block_heading;
DELIMITER $$
CREATE PROCEDURE _v1161_add_block_heading()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'heading_series_id'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `heading_series_id` int DEFAULT NULL
        COMMENT 'ブロック先頭の見出しにする作品（series.series_id）。映画の声の出演の作品ごとのまとまりなど'
        AFTER `leading_company_alias_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'heading_text'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `heading_text` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'ブロック先頭の見出しの文字（画面の表記どおり）。NULL なら作品の正式タイトル'
        AFTER `heading_series_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND CONSTRAINT_NAME = 'fk_block_heading_series'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD CONSTRAINT `fk_block_heading_series` FOREIGN KEY (`heading_series_id`)
        REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1161_add_block_heading();
DROP PROCEDURE IF EXISTS _v1161_add_block_heading;
