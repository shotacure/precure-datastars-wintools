-- =====================================================================
-- v1.20.1_add_series_theme_song_size.sql
--
-- 映画などの作品単位の主題歌・挿入歌（series_theme_songs）に、その作品で流れたサイズを持てるようにする。
--   song_size_variant_code  VARCHAR(32) NULL   その作品で流れたサイズ（→ song_size_variants）。NULL なら指定なし。
--
-- 各話の主題歌（episode_theme_songs）には同じ欄があり、エピソード詳細・作品詳細の主題歌の一覧で
-- 流れたサイズの札と、そのサイズの配信音源の再生ボタンを出している。作品単位の主題歌には欄が無く、
-- 映画の作品詳細では「流れたサイズとは限らない音源」の扱いでしか出せなかった。
--
-- 冪等性: 列・索引・外部キーごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_add_series_theme_song_size;
DELIMITER $$
CREATE PROCEDURE _v1201_add_series_theme_song_size()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND COLUMN_NAME = 'song_size_variant_code'
  ) THEN
    ALTER TABLE `series_theme_songs`
      ADD COLUMN `song_size_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
        COMMENT 'その作品で流れたサイズ（→ song_size_variants）。NULL なら指定なし'
        AFTER `song_recording_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND INDEX_NAME = 'ix_sts_song_size'
  ) THEN
    ALTER TABLE `series_theme_songs` ADD KEY `ix_sts_song_size` (`song_size_variant_code`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND CONSTRAINT_NAME = 'fk_sts_song_size'
  ) THEN
    ALTER TABLE `series_theme_songs`
      ADD CONSTRAINT `fk_sts_song_size` FOREIGN KEY (`song_size_variant_code`)
        REFERENCES `song_size_variants` (`variant_code`) ON DELETE RESTRICT ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1201_add_series_theme_song_size();
DROP PROCEDURE IF EXISTS _v1201_add_series_theme_song_size;
