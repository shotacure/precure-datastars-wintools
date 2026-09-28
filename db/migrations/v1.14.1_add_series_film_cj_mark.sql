-- =====================================================================
-- v1.14.1_add_series_film_cj_mark.sql
--
-- series に「CJ マークあり」の列 film_cj_mark を追加し、映画のタイトルカード（役職 TITLE）の
-- 既定テンプレで、タイトルの下の左に小さな印「CJ」、右に映倫審査番号を出す。
--
-- CJ マークは、映画のタイトルカードに映倫マークと並んで付くことがある作品単位の表示。
-- 審査番号（film_rating_no）と同じくシリーズマスタに持ち、クレジット階層には持たない。
-- テンプレでは {CJ_MARK}（印が付く作品では「CJ」、付かない作品では空）で参照し、
-- {?CJ_MARK}...{/?CJ_MARK} で付く作品だけ印を出す。
--
-- 冪等性: 列は INFORMATION_SCHEMA で存在確認してから ALTER ADD COLUMN、テンプレは
-- v1.13.2 の既定のままのときだけ書き換える。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1141_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1141_add_col_if_missing(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_col
  ) THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

-- 映倫審査番号（film_rating_no）の直後に追加する。
CALL _v1141_add_col_if_missing(
  'series',
  'film_cj_mark',
  "tinyint(1) NOT NULL DEFAULT 0 COMMENT 'タイトルカードに CJ マークが付くか' AFTER `film_rating_no`");

DROP PROCEDURE _v1141_add_col_if_missing;

-- タイトルカードの既定テンプレを、タイトルと同じ幅の透明な箱の左下に CJ マークの印、右下に映倫審査番号を
-- 寄せる並べ方に置き換える。書き換えるのは既定テンプレ（series_id IS NULL）が v1.13.2 のままのときだけ
-- （シリーズ別に上書きしたテンプレや、手で直したテンプレには触れない）。
UPDATE role_templates
   SET format_template = '<span class="film-title-card"><strong>『{SERIES_TITLE}』</strong><span class="film-title-marks">{?CJ_MARK}<span class="cj-mark" title="CJマーク">{CJ_MARK}</span>{/?CJ_MARK}{?FILM_RATING_NO}<span class="film-rating">(映倫 {FILM_RATING_NO})</span>{/?FILM_RATING_NO}</span></span>',
       notes = '映画のタイトルカード。シリーズ名（series.title）の下に、左寄せで CJ マークの印（series.film_cj_mark）、右寄せで映倫審査番号（series.film_rating_no）を出す。',
       updated_by = 'migration'
 WHERE role_code = 'TITLE' AND series_id IS NULL
   AND format_template = '<strong>『{SERIES_TITLE}』</strong>{?FILM_RATING_NO}
(映倫 {FILM_RATING_NO}){/?FILM_RATING_NO}';
