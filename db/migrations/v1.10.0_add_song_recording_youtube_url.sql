-- =====================================================================
-- v1.10.0_add_song_recording_youtube_url.sql
--
-- song_recordings に公式 YouTube 動画 URL 列 youtube_url を追加する。
-- 楽曲詳細ページの録音セクションと、シリーズ詳細の「主題歌・挿入歌」カードに
-- 動画を埋め込むために使う。episodes.youtube_trailer_url と同じ varchar(1024) NULL 許可。
--
-- 冪等性: 列の存在を INFORMATION_SCHEMA で確認してから ALTER ADD COLUMN する。
-- 既存マイグレーション（v1.5.14_add_episode_special_trailer_url.sql）のスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 列が無いときだけ ADD COLUMN するプロシージャ。
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v1100_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1100_add_col_if_missing(
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

-- music_class_code の直後（notes の直前）に追加する。
CALL _v1100_add_col_if_missing(
  'song_recordings',
  'youtube_url',
  "varchar(1024) DEFAULT NULL COMMENT '公式 YouTube 動画 URL' AFTER `music_class_code`");

DROP PROCEDURE _v1100_add_col_if_missing;

COMMIT;
