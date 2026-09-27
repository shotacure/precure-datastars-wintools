-- =====================================================================
-- v1.13.2_add_series_film_rating_no.sql
--
-- (1) series に映倫審査番号の列 film_rating_no を追加し、映画のタイトルカードを表す役職 TITLE と
--     その既定テンプレを登録する。
-- (2) credit_cards にカードの見せ方 presentation（CARDS / ROLL）を追加する。映画の ED のように
--     「声の出演はカード → スタッフはロール → 著作権表記はまたカード」と 1 つのクレジットの中で
--     切り替わるため、見せ方をカード単位で持つ。既存のロールのクレジット（credits.presentation='ROLL'）は
--     そのカードを ROLL にする。
-- (3) 著作権表記のカードを表す役職 COPYRIGHT（役職名は出さない）と既定テンプレを登録する。
--     1 行目に © 表記（TEXT エントリ）、2 行目に権利者の会社（COMPANY エントリ）を「/」区切りで出す。
-- (4) roles.role_format_kind に NOTICE（表記のみの役職）を足し、TITLE と COPYRIGHT をそれにする。
--     クレジットに表示はするが、並ぶ企業・人物の関与には数えず、役職詳細ページも作らない。
--
-- 映画のクレジットには、作品タイトル（と映倫の審査番号）だけを出すカードがある。タイトルは
-- series.title、審査番号は series.film_rating_no に持ち、クレジット階層にはカードの位置を示す
-- 役職 TITLE だけを置く（エントリは持たない）。表示は役職テンプレ（role_templates）で
-- {SERIES_TITLE} と {FILM_RATING_NO} を展開する。役職名は画面に出さない（hide_role_name_in_credit=1）。
--
-- 冪等性: 列は INFORMATION_SCHEMA で存在確認してから ALTER ADD COLUMN、役職は INSERT IGNORE、
-- テンプレは NOT EXISTS（series_id が NULL の行は UNIQUE で重複を弾けないため）。
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

-- カードの見せ方。card_seq の直後に追加する。
CALL _v1132_add_col_if_missing(
  'credit_cards',
  'presentation',
  "enum('CARDS','ROLL') NOT NULL DEFAULT 'CARDS' COMMENT 'カードの見せ方（CARDS=カード / ROLL=ロール）' AFTER `card_seq`");

DROP PROCEDURE _v1132_add_col_if_missing;

-- 映画のタイトルカードの位置を示す役職（エントリを持たず、テンプレでシリーズ名と映倫番号を出す）。
INSERT IGNORE INTO roles (role_code, name_ja, name_en, role_format_kind, display_order, hide_role_name_in_credit, created_by, updated_by)
VALUES ('TITLE', 'タイトル', 'Title', 'NORMAL', 5, 1, 'shota', 'shota');

INSERT INTO role_templates (role_code, series_id, format_template, notes, created_by, updated_by)
SELECT 'TITLE', NULL, '<strong>『{SERIES_TITLE}』</strong>{?FILM_RATING_NO}
(映倫 {FILM_RATING_NO}){/?FILM_RATING_NO}',
        '映画のタイトルカード。シリーズ名（series.title）と映倫審査番号（series.film_rating_no）を出す。', 'shota', 'shota'
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM role_templates WHERE role_code = 'TITLE' AND series_id IS NULL);

-- 既存のロールのクレジットは、そのカードを ROLL にする。
UPDATE credit_cards c JOIN credits cr ON cr.credit_id = c.credit_id
   SET c.presentation = 'ROLL'
 WHERE cr.presentation = 'ROLL' AND c.presentation = 'CARDS';

-- 著作権表記のカード。1 ブロックに © 表記（TEXT）と権利者の会社（COMPANY）を並べる。
INSERT IGNORE INTO roles (role_code, name_ja, name_en, role_format_kind, display_order, hide_role_name_in_credit, created_by, updated_by)
VALUES ('COPYRIGHT', '著作権表記', 'Copyright', 'NORMAL', 2000, 1, 'shota', 'shota');

INSERT INTO role_templates (role_code, series_id, format_template, notes, created_by, updated_by)
SELECT 'COPYRIGHT', NULL, '{#BLOCKS}{TEXTS}{?COMPANIES}
{COMPANIES:sep="/",wrap=""}{/?COMPANIES}{/BLOCKS}',
        '著作権表記のカード。© 表記（TEXT エントリ）と権利者の会社（COMPANY エントリ、「/」区切り）を 2 行で出す。', 'shota', 'shota'
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM role_templates WHERE role_code = 'COPYRIGHT' AND series_id IS NULL);

-- 表記のみの役職の書式区分 NOTICE を足す（enum の変更。列定義が既に NOTICE を含むなら何もしない）。
SET @has_notice := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'roles' AND COLUMN_NAME = 'role_format_kind'
                       AND COLUMN_TYPE LIKE '%''NOTICE''%');
SET @sql := IF(@has_notice = 0,
  "ALTER TABLE `roles` MODIFY COLUMN `role_format_kind` enum('NORMAL','SERIAL','THEME_SONG','VOICE_CAST','COMPANY_ONLY','LOGO_ONLY','NOTICE') NOT NULL DEFAULT 'NORMAL'",
  'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE roles SET role_format_kind = 'NOTICE', updated_by = 'shota' WHERE role_code IN ('TITLE', 'COPYRIGHT');

COMMIT;
