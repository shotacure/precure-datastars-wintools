-- =====================================================================
-- v1.20.0_add_theme_song_instrumental.sql
--
-- 主題歌・挿入歌の紐付け（episode_theme_songs / series_theme_songs）に、その回（その映画）では
-- インストゥルメンタルで使ったことを示す印を持てるようにする。
--   is_instrumental  TINYINT(1) NOT NULL DEFAULT 0   1 ならインストゥルメンタルで使った。
--                                                    紐付ける録音は元の歌の録音のまま（作詞・作曲・編曲・歌はその録音から出す）。
--
-- クレジットの曲名に「（インストゥルメンタル）」を添え、役職テンプレートでは
-- {?INSTRUMENTAL}…{/?INSTRUMENTAL} / {?!INSTRUMENTAL}…{/?!INSTRUMENTAL} で書き分ける
-- （画面が「作曲・編曲」を 1 行にし、「オリジナルボーカル曲」の見出しの下に歌を出す作品向け）。
-- 話のページの主題歌・挿入歌の一覧でも曲名に添える。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_theme_song_instrumental;
DELIMITER $$
CREATE PROCEDURE _v1191_add_theme_song_instrumental()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs' AND COLUMN_NAME = 'is_instrumental'
  ) THEN
    ALTER TABLE `episode_theme_songs`
      ADD COLUMN `is_instrumental` tinyint(1) NOT NULL DEFAULT 0
        AFTER `title_misprint_text`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND COLUMN_NAME = 'is_instrumental'
  ) THEN
    ALTER TABLE `series_theme_songs`
      ADD COLUMN `is_instrumental` tinyint(1) NOT NULL DEFAULT 0
        AFTER `title_misprint_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_theme_song_instrumental();
DROP PROCEDURE IF EXISTS _v1191_add_theme_song_instrumental;
