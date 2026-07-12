-- =====================================================================
-- v1.5.14_add_episode_special_trailer_url.sql
--
-- episodes に「特別予告（本放送時）」の YouTube URL 列 youtube_special_trailer_url を
-- 追加する。既存の youtube_trailer_url（次回予告）とは別枠で、本放送で流れた特別予告を
-- エピソード詳細ページに埋め込むために使う。既存 URL 列と同じ varchar(1024) NULL 許可。
--
-- 冪等性: 列の存在を INFORMATION_SCHEMA で確認してから ALTER ADD COLUMN する。
-- 既存マイグレーション（v1.4.3_add_external_urls.sql / v1.5.11_add_part_type_singleton.sql）の
-- スタイルを踏襲。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 列が無いときだけ ADD COLUMN するプロシージャ。
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v1514_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1514_add_col_if_missing(
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

-- youtube_trailer_url の直後に追加する。
CALL _v1514_add_col_if_missing(
  'episodes',
  'youtube_special_trailer_url',
  "varchar(1024) DEFAULT NULL COMMENT '特別予告（本放送時）の YouTube 動画 URL' AFTER `youtube_trailer_url`");

DROP PROCEDURE _v1514_add_col_if_missing;

COMMIT;
