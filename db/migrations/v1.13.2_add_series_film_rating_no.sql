-- =====================================================================
-- v1.13.2_add_series_film_rating_no.sql
--
-- series に映倫審査番号の列 film_rating_no を追加し、映画のタイトルカードを表す役職 TITLE と
-- その既定テンプレを登録する。
--
-- 映画のクレジットには、作品タイトル（と映倫の審査番号）だけを出すカードがある。タイトルは
-- series.title、審査番号は series.film_rating_no に持ち、クレジット階層にはカードの位置を示す
-- 役職 TITLE だけを置く（エントリは持たない）。表示は役職テンプレ（role_templates）で
-- {SERIES_TITLE} と {FILM_RATING_NO} を展開する。役職名は画面に出さない（hide_role_name_in_credit=1）。
--
-- 冪等性: 列は INFORMATION_SCHEMA で存在確認してから ALTER ADD COLUMN、役職・テンプレは INSERT IGNORE。
-- =====================================================================

START TRANSACTION;

DROP PROCEDURE IF EXISTS _v1132_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1132_add_col_if_missing(
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

-- 本予告 URL（youtube_trailer_url）の直後に追加する。
CALL _v1132_add_col_if_missing(
  'series',
  'film_rating_no',
  "varchar(16) DEFAULT NULL COMMENT '映倫審査番号（映画のタイトルカードに併記）' AFTER `youtube_trailer_url`");

DROP PROCEDURE _v1132_add_col_if_missing;

-- 映画のタイトルカードの位置を示す役職（エントリを持たず、テンプレでシリーズ名と映倫番号を出す）。
INSERT IGNORE INTO roles (role_code, name_ja, name_en, role_format_kind, display_order, hide_role_name_in_credit, created_by, updated_by)
VALUES ('TITLE', 'タイトル', 'Title', 'NORMAL', 5, 1, 'shota', 'shota');

INSERT IGNORE INTO role_templates (role_code, series_id, format_template, notes, created_by, updated_by)
VALUES ('TITLE', NULL, '<strong>『{SERIES_TITLE}』</strong>{?FILM_RATING_NO}
(映倫 {FILM_RATING_NO}){/?FILM_RATING_NO}',
        '映画のタイトルカード。シリーズ名（series.title）と映倫審査番号（series.film_rating_no）を出す。', 'shota', 'shota');

COMMIT;
