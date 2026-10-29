-- =====================================================================
-- v1.20.0_characters_has_own_page.sql
--
-- characters に「個別ページを持つか」の印 has_own_page を足す。
--   1 = 個別ページ（/characters/{名前}/）を持つ
--   0 = 個別ページを持たず、最初に登場した作品の「その他のキャラクター」のページ
--       （/characters/guests/{作品}/）にまとめる
-- サイトはこの印だけで決める（種別・歌唱・家族関係・登場回数は見ない）。既定は 1。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから ALTER を発行する。
-- 列の追加は DDL なのでトランザクションには入れない。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1191_add_col_if_missing(
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

CALL _v1191_add_col_if_missing('characters', 'has_own_page',
  "TINYINT(1) NOT NULL DEFAULT 1 COMMENT '個別ページを持つか（1=個別ページ、0=作品ごとの「その他のキャラクター」のページにまとめる）' AFTER `wikipedia_url`");

DROP PROCEDURE IF EXISTS _v1191_add_col_if_missing;
