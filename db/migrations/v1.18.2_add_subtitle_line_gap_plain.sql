-- =====================================================================
-- v1.18.2_add_subtitle_line_gap_plain.sql
--
-- サブタイトルのテロップ画像で、下の行に振り仮名が無いときの行間を作品ごとに持つ列を series_subtitle_styles に足す。
-- 本編のテロップには、下の行に振り仮名があると行を広げる作品と、振り仮名の有無で行の位置が変わらない作品があるため、
-- 下の行に振り仮名があるとき（subtitle_line_gap_ratio）と無いとき（この列）の空きを別々に持つ。
--
--   subtitle_line_gap_ratio_plain  下の行に振り仮名が無いときの、上の行の字の下端から下の行の字の上端までの空き
--                                  （親字の大きさに対する比）。NULL なら subtitle_line_gap_ratio に振り仮名の段の高さを足した空き
--                                  （振り仮名の有無で行送りが変わらない）
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから足す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1182_subtitle_line_gap_plain;
DELIMITER $$
CREATE PROCEDURE _v1182_subtitle_line_gap_plain()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_subtitle_styles'
       AND COLUMN_NAME = 'subtitle_line_gap_ratio_plain'
  ) THEN
    ALTER TABLE `series_subtitle_styles`
      ADD COLUMN `subtitle_line_gap_ratio_plain` decimal(4,3) DEFAULT NULL
        COMMENT '下の行に振り仮名が無いときの行間（親字比、NULL は subtitle_line_gap_ratio＋振り仮名の段）'
        AFTER `subtitle_line_gap_ratio_3`;
  END IF;
END$$
DELIMITER ;

CALL _v1182_subtitle_line_gap_plain();
DROP PROCEDURE IF EXISTS _v1182_subtitle_line_gap_plain;
