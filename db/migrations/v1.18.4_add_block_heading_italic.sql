-- =====================================================================
-- v1.18.4_add_block_heading_italic.sql
--
-- クレジットのブロック（credit_role_blocks）の見出しを、画面どおり斜体で出せるようにする。
--   heading_italic  TINYINT(1) NOT NULL DEFAULT 0  見出し（heading_series_id / heading_text）を斜体で出すか。
--
-- 映画のプリキュアオールスターズDX の声の出演では、作品ごとのまとまりの頭に出る作品名が斜体で表示される。
-- 斜体かどうかは作品ごとに画面で決まるので、ブロックごとに持つ。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_block_heading_italic;
DELIMITER $$
CREATE PROCEDURE _v1184_add_block_heading_italic()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'heading_italic'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `heading_italic` tinyint(1) NOT NULL DEFAULT 0
        COMMENT 'ブロック先頭の見出しを斜体で出すか（画面どおり）'
        AFTER `heading_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_block_heading_italic();
DROP PROCEDURE IF EXISTS _v1184_add_block_heading_italic;
