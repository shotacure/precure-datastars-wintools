-- =====================================================================
-- v1.18.0_add_series_subtitle_styles.sql
--
-- シリーズごとのサブタイトルの書体と、テロップ画像（エピソード詳細ページのサブタイトル欄・OGP カード）の組み方を、
-- series から切り出した series_subtitle_styles に持つ。series と 1 対 1（主キー series_id）。
-- 行の無いシリーズ・NULL の列は既定値で組む。
--
--   font_subtitle                  サブタイトルの親字の書体（本編テロップの書体。インストール済み書体の名前）。series から移す
--   subtitle_kerning               字間の組み方。PROPORTIONAL＝書体の詰め情報（なければ字面）で詰める / MONO＝ベタ組み。NULL なら PROPORTIONAL
--   subtitle_letter_spacing_em     親字の字と字のあいだに足す空き（字の大きさに対する比、負なら詰める）。NULL なら 0
--   subtitle_ruby_letter_spacing_em 振り仮名の字と字のあいだに足す空き（振り仮名の大きさに対する比）。NULL なら 0
--   font_subtitle_ruby             振り仮名の書体名。NULL なら親字と同じ書体
--   subtitle_ruby_size_ratio       振り仮名の大きさ（親字の大きさに対する比）。NULL なら 0.300
--   subtitle_ruby_raise_ratio      振り仮名のベースラインを親字のベースラインから上げる高さ（親字の大きさに対する比）。NULL なら 1.040
--   subtitle_ruby_oblique_deg      振り仮名にかける斜体の角度（度。右へ倒す）。NULL なら 0（倒さない）
--   subtitle_line_gap_ratio        行と行のあいだの空き（親字の大きさに対する比）。NULL なら振り仮名があれば 0.280、なければ 0.220
--   subtitle_line_gap_ratio_3      3 行以上のときの行間（親字の大きさに対する比）。NULL なら subtitle_line_gap_ratio と同じ
--   subtitle_ruby_overhang_ratio   親字より長い振り仮名が、振り仮名の無い隣の字へはみ出してよい最大の幅（片側、親字の大きさに対する比）。NULL なら振り仮名 1 字分
--   subtitle_ruby_line_edge        行の端の振り仮名の扱い。ALIGN＝行頭・行末とも外へ出さず内側へ寄せる / OVERHANG＝行頭・行末とも外へはみ出させる。
--                                  NULL なら行頭はそろえ、行末ははみ出させる
--   subtitle_ruby_grouping         振り仮名の置き方。MONO＝1 字ずつ（ルビの単位ごとに）親字の上に置く /
--                                  JUKUGO＝振り仮名のある字が続くところの読みをひと続きにして熟語全体の上に置く /
--                                  SPREAD＝その読みを熟語の幅に 1 字ずつ均等に空けて並べる。NULL なら MONO
--
-- 手順: テーブルを作る → series に同名の列があれば値を移す（どれかの値を持つシリーズだけ行を作る）→ series からその列を消す。
-- 冪等性: テーブルは IF NOT EXISTS、移しと削除は INFORMATION_SCHEMA で列の有無を確かめてから行う。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_subtitle_styles` (
  `series_id` int NOT NULL,
  `font_subtitle` varchar(64) DEFAULT NULL COMMENT 'サブタイトルの親字の書体（本編テロップの書体）',
  `subtitle_kerning` varchar(16) DEFAULT NULL COMMENT '字間の組み方 PROPORTIONAL / MONO（NULL は PROPORTIONAL）',
  `subtitle_letter_spacing_em` decimal(4,3) DEFAULT NULL COMMENT '親字の字間に足す空き（字の大きさ比、NULL は 0）',
  `subtitle_ruby_letter_spacing_em` decimal(4,3) DEFAULT NULL COMMENT '振り仮名の字間に足す空き（振り仮名の大きさ比、NULL は 0）',
  `font_subtitle_ruby` varchar(64) DEFAULT NULL COMMENT 'サブタイトルの振り仮名の書体（NULL は親字と同じ）',
  `subtitle_ruby_size_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名の大きさ（親字比、NULL は 0.300）',
  `subtitle_ruby_raise_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名のベースラインの高さ（親字比、NULL は 1.040）',
  `subtitle_ruby_oblique_deg` decimal(4,1) DEFAULT NULL COMMENT '振り仮名の斜体の角度（度、NULL は 0）',
  `subtitle_line_gap_ratio` decimal(4,3) DEFAULT NULL COMMENT '行と行のあいだの空き（親字比、NULL は既定）',
  `subtitle_line_gap_ratio_3` decimal(4,3) DEFAULT NULL COMMENT '3 行以上のときの行間（親字比、NULL は subtitle_line_gap_ratio と同じ）',
  `subtitle_ruby_overhang_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名が隣の字へはみ出せる最大幅（片側・親字比、NULL は振り仮名 1 字分）',
  `subtitle_ruby_line_edge` varchar(16) DEFAULT NULL COMMENT '行の端の振り仮名の扱い ALIGN / OVERHANG（NULL は行頭そろえ・行末はみ出し）',
  `subtitle_ruby_grouping` varchar(16) DEFAULT NULL COMMENT '振り仮名の置き方 MONO / JUKUGO / SPREAD（NULL は MONO）',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`series_id`),
  CONSTRAINT `fk_sss_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='シリーズごとのサブタイトルの書体とテロップ画像の組み方';

DROP PROCEDURE IF EXISTS _v1180_series_subtitle_styles;
DELIMITER $$
CREATE PROCEDURE _v1180_series_subtitle_styles()
BEGIN
  DECLARE col_name VARCHAR(64);
  DECLARE done INT DEFAULT 0;
  DECLARE cols CURSOR FOR
    SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series'
       AND COLUMN_NAME IN ('font_subtitle', 'subtitle_kerning', 'subtitle_letter_spacing_em', 'subtitle_ruby_letter_spacing_em', 'font_subtitle_ruby', 'subtitle_ruby_size_ratio', 'subtitle_ruby_raise_ratio', 'subtitle_ruby_oblique_deg', 'subtitle_line_gap_ratio', 'subtitle_line_gap_ratio_3', 'subtitle_ruby_overhang_ratio', 'subtitle_ruby_line_edge', 'subtitle_ruby_grouping');
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

  -- 値を持つシリーズの行を作り、series にある列ごとに値を写す。
  OPEN cols;
  copy_loop: LOOP
    FETCH cols INTO col_name;
    IF done THEN LEAVE copy_loop; END IF;
    SET @sql = CONCAT(
      'INSERT INTO series_subtitle_styles (series_id, ', col_name, ') ',
      'SELECT series_id, ', col_name, ' FROM series WHERE ', col_name, ' IS NOT NULL ',
      'ON DUPLICATE KEY UPDATE ', col_name, ' = VALUES(', col_name, ')');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END LOOP;
  CLOSE cols;

  -- series から列を消す。
  SET done = 0;
  OPEN cols;
  drop_loop: LOOP
    FETCH cols INTO col_name;
    IF done THEN LEAVE drop_loop; END IF;
    SET @sql = CONCAT('ALTER TABLE `series` DROP COLUMN `', col_name, '`');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END LOOP;
  CLOSE cols;
END$$
DELIMITER ;

CALL _v1180_series_subtitle_styles();
DROP PROCEDURE IF EXISTS _v1180_series_subtitle_styles;
