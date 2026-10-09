-- =====================================================================
-- v1.18.4_add_theme_song_title_misprint.sql
--
-- 主題歌・挿入歌の紐付け（episode_theme_songs / series_theme_songs）に、クレジットの画面に出た曲名の誤記を持てるようにする。
--   title_misprint_text  VARCHAR(255) NULL   クレジットの画面に出た曲名の誤記（画面の曲名を丸ごと、文字もそのまま）。
--                                            NULL なら誤記なし。正しい曲名は songs.title。
--
-- クレジットの主題歌の行では、誤記を取り消し線で出してから正しい曲名を続ける。
-- 話のページの主題歌・挿入歌の一覧や曲のページは、クレジットの再現ではないので正しい曲名だけを出す。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_theme_song_title_misprint;
DELIMITER $$
CREATE PROCEDURE _v1184_add_theme_song_title_misprint()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs' AND COLUMN_NAME = 'title_misprint_text'
  ) THEN
    ALTER TABLE `episode_theme_songs`
      ADD COLUMN `title_misprint_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットの画面に出た曲名の誤記（画面どおり）。NULL なら誤記なし'
        AFTER `song_recording_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND COLUMN_NAME = 'title_misprint_text'
  ) THEN
    ALTER TABLE `series_theme_songs`
      ADD COLUMN `title_misprint_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットの画面に出た曲名の誤記（画面どおり）。NULL なら誤記なし'
        AFTER `song_recording_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_theme_song_title_misprint();
DROP PROCEDURE IF EXISTS _v1184_add_theme_song_title_misprint;
