-- =====================================================================
-- v1.18.4_add_song_credit_role_label_text.sql
--
-- 歌の作家連名（song_credits）に、盤に印刷された役職の表記を持てるようにする。
--   role_label_text  VARCHAR(32) NULL   盤の役職の表記（「原詞」など）。役職名（roles.name_ja）と違うときだけ入れる。
--                                       その役職の連名のうち credit_seq がいちばん小さい行の値を使う（2 行目以降は NULL）。
--
-- サイトは曲・盤のトラック・主題歌の行など、その曲の作家を出す所の役職の表記をこの文字にする（リンク先は役職のまま）。
-- 人物ページ・役職ページ・統計などの集計は役職のまま。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_song_credit_role_label_text;
DELIMITER $$
CREATE PROCEDURE _v1184_add_song_credit_role_label_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'song_credits' AND COLUMN_NAME = 'role_label_text'
  ) THEN
    ALTER TABLE `song_credits`
      ADD COLUMN `role_label_text` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '盤の役職の表記（役職名と違うときだけ）。役職の連名の先頭行の値を使う'
        AFTER `person_alias_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_song_credit_role_label_text();
DROP PROCEDURE IF EXISTS _v1184_add_song_credit_role_label_text;
