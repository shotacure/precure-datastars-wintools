-- =====================================================================
-- v1.11.0_add_series_youtube_trailer_url.sql
--
-- series に「本予告」の YouTube URL 列 youtube_trailer_url を追加する。
-- シリーズ詳細ページで、登録があるシリーズだけ基本情報の直前に本予告を埋め込む。
-- 列自体はシリーズ共通だが、運用上の登録対象は映画作品を想定している。
-- 既存のエピソード側 URL 列（episodes.youtube_trailer_url）と同じ varchar(1024) NULL 許可。
--
-- 冪等性: 列の存在を INFORMATION_SCHEMA で確認してから ALTER ADD COLUMN する。
-- 既存マイグレーション（v1.5.14_add_episode_special_trailer_url.sql 等）のスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

DROP PROCEDURE IF EXISTS _v1110_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1110_add_col_if_missing(
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

-- 外部 URL 群の末尾（amazon_prime_video_asin の直後）に追加する。
CALL _v1110_add_col_if_missing(
  'series',
  'youtube_trailer_url',
  "varchar(1024) DEFAULT NULL COMMENT '本予告の YouTube 動画 URL' AFTER `amazon_prime_video_asin`");

DROP PROCEDURE _v1110_add_col_if_missing;

COMMIT;
