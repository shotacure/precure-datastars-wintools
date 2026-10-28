-- =====================================================================
-- v1.19.1_theme_song_seq_and_credited_title.sql
--
-- クレジットの主題歌の行の出し方を、回ごと・役職の行ごとに整えられるようにする。
--
-- 1. credit_card_roles に、主題歌ループが出す曲を劇中順の範囲で絞る値を持てるようにする。
--   theme_song_seq  VARCHAR(16) NULL   "N" か "N-M"。episode_theme_songs.seq / series_theme_songs.seq の範囲。
--                                      その役職の行で役職テンプレートの {#THEME_SONGS} ループ（旧 {THEME_SONGS} も）が出す曲を、
--                                      この範囲の曲だけに絞る。テンプレート側の seq= と両方あれば両方を満たす曲だけ。
--                                      NULL なら絞らない。主題歌の形式（roles.role_format_kind='THEME_SONG'）の役職でだけ意味を持つ。
--
--   用途：主題歌のカードが 2 枚に分かれ、挿入歌がカードごとに分かれて出る回（1 枚目に ED 主題歌と挿入歌 1 曲、
--   2 枚目に挿入歌 2 曲など）。役職テンプレートは役職ごとに共通なので、カードごとの範囲は役職の行で持つ。
--
-- 2. episode_theme_songs / series_theme_songs に、クレジットの画面に出た曲名の表記を持てるようにする。
--   title_credited_text  VARCHAR(255) NULL   クレジットの画面に出た曲名の表記（誤記ではない表記違い）。
--                                            入っていればクレジットの主題歌の行の曲名をこの表記で出す
--                                            （打ち消し線なし、リンク先は曲のまま）。NULL なら songs.title。
--                                            title_misprint_text（誤記）と両方あるときは誤記を優先し、この表記は使わない。
--
--   話のページの主題歌・挿入歌の一覧や曲のページは、クレジットの再現ではないので曲名（songs.title）のまま。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_theme_song_seq_and_credited_title;
DELIMITER $$
CREATE PROCEDURE _v1191_theme_song_seq_and_credited_title()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'theme_song_seq'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `theme_song_seq` varchar(16) DEFAULT NULL
        COMMENT '主題歌ループが出す曲の劇中順の範囲（"N" か "N-M"）。NULL なら絞らない'
        AFTER `not_on_screen`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs' AND COLUMN_NAME = 'title_credited_text'
  ) THEN
    ALTER TABLE `episode_theme_songs`
      ADD COLUMN `title_credited_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットの画面に出た曲名の表記（誤記ではない表記違い）。入っていればクレジットの行の曲名をこの表記で出す（打ち消し線なし、リンク先は曲のまま）'
        AFTER `title_misprint_text`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_theme_songs' AND COLUMN_NAME = 'title_credited_text'
  ) THEN
    ALTER TABLE `series_theme_songs`
      ADD COLUMN `title_credited_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットの画面に出た曲名の表記（誤記ではない表記違い）。入っていればクレジットの行の曲名をこの表記で出す（打ち消し線なし、リンク先は曲のまま）'
        AFTER `title_misprint_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_theme_song_seq_and_credited_title();
DROP PROCEDURE IF EXISTS _v1191_theme_song_seq_and_credited_title;
