-- =====================================================================
-- v1.18.0_add_series_subtitle_telop.sql
--
-- エピソード詳細ページのサブタイトルのテロップ画像（白い字・黒いフチ・影の透過 PNG）の組み方を、
-- シリーズごとに series に持てるようにする。親字の書体は既存の font_subtitle を使う。
--
--   subtitle_kerning               字間の組み方。PROPORTIONAL＝書体の詰め情報（なければ字面）で詰める / MONO＝ベタ組み。NULL なら PROPORTIONAL
--   subtitle_letter_spacing_em     親字の字と字のあいだに足す空き（字の大きさに対する比、負なら詰める）。NULL なら 0
--   subtitle_ruby_letter_spacing_em 振り仮名の字と字のあいだに足す空き（振り仮名の大きさに対する比）。NULL なら 0
--   font_subtitle_ruby             振り仮名の書体名（インストール済み書体の名前）。NULL なら親字と同じ書体
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
-- 冪等性: 列は INFORMATION_SCHEMA で存在を確かめてから足す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1180_series_subtitle_telop;
DELIMITER $$
CREATE PROCEDURE _v1180_series_subtitle_telop()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'font_subtitle_ruby') THEN
    ALTER TABLE `series`
      ADD COLUMN `font_subtitle_ruby` varchar(64) DEFAULT NULL COMMENT 'サブタイトルの振り仮名の書体（NULL は親字と同じ）' AFTER `font_subtitle`,
      ADD COLUMN `subtitle_ruby_size_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名の大きさ（親字比、NULL は 0.300）' AFTER `font_subtitle_ruby`,
      ADD COLUMN `subtitle_ruby_raise_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名のベースラインの高さ（親字比、NULL は 1.040）' AFTER `subtitle_ruby_size_ratio`,
      ADD COLUMN `subtitle_ruby_oblique_deg` decimal(4,1) DEFAULT NULL COMMENT '振り仮名の斜体の角度（度、NULL は 0）' AFTER `subtitle_ruby_raise_ratio`,
      ADD COLUMN `subtitle_line_gap_ratio` decimal(4,3) DEFAULT NULL COMMENT '行と行のあいだの空き（親字比、NULL は既定）' AFTER `subtitle_ruby_oblique_deg`,
      ADD COLUMN `subtitle_ruby_overhang_ratio` decimal(4,3) DEFAULT NULL COMMENT '振り仮名が隣の字へはみ出せる最大幅（片側・親字比、NULL は振り仮名 1 字分）' AFTER `subtitle_line_gap_ratio`,
      ADD COLUMN `subtitle_ruby_line_edge` varchar(16) DEFAULT NULL COMMENT '行の端の振り仮名の扱い ALIGN / OVERHANG（NULL は行頭そろえ・行末はみ出し）' AFTER `subtitle_ruby_overhang_ratio`;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'subtitle_ruby_grouping') THEN
    ALTER TABLE `series`
      ADD COLUMN `subtitle_ruby_grouping` varchar(16) DEFAULT NULL COMMENT '振り仮名の置き方 MONO / JUKUGO / SPREAD（NULL は MONO）' AFTER `subtitle_ruby_line_edge`;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'subtitle_kerning') THEN
    ALTER TABLE `series`
      ADD COLUMN `subtitle_kerning` varchar(16) DEFAULT NULL COMMENT '字間の組み方 PROPORTIONAL / MONO（NULL は PROPORTIONAL）' AFTER `font_subtitle`,
      ADD COLUMN `subtitle_letter_spacing_em` decimal(4,3) DEFAULT NULL COMMENT '親字の字間に足す空き（字の大きさ比、NULL は 0）' AFTER `subtitle_kerning`,
      ADD COLUMN `subtitle_ruby_letter_spacing_em` decimal(4,3) DEFAULT NULL COMMENT '振り仮名の字間に足す空き（振り仮名の大きさ比、NULL は 0）' AFTER `subtitle_letter_spacing_em`;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'subtitle_line_gap_ratio_3') THEN
    ALTER TABLE `series`
      ADD COLUMN `subtitle_line_gap_ratio_3` decimal(4,3) DEFAULT NULL COMMENT '3 行以上のときの行間（親字比、NULL は subtitle_line_gap_ratio と同じ）' AFTER `subtitle_line_gap_ratio`;
  END IF;
END$$
DELIMITER ;

CALL _v1180_series_subtitle_telop();
DROP PROCEDURE IF EXISTS _v1180_series_subtitle_telop;
