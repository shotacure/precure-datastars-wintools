-- =====================================================================
-- v1.20.0_add_precures_counts_only_when_transformed.sql
--
-- 作品単位の作品（映画など）のプリキュアの欄で、変身した作品だけプリキュアとして数えるプリキュアを示せるようにする。
--
-- precures に列を足す。
--   counts_only_when_transformed  TINYINT(1) NOT NULL DEFAULT 0
--     1 … クレジットの声の出演に出た作品のうち、その作品自身の series_precures に行がある作品（変身した作品）でだけ
--          プリキュアとして数える。行の無い作品では、どの名義で出ても数えない。
--          TV のレギュラーで、ある映画でだけ変身するプリキュア（キュアモフルン）に使う。
--     0 … クレジットに出た作品すべてで、プリキュアとして数える（キュアエコーなど）。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_precures_counts_only_when_transformed;
DELIMITER $$
CREATE PROCEDURE _v1191_add_precures_counts_only_when_transformed()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'precures' AND COLUMN_NAME = 'counts_only_when_transformed'
  ) THEN
    ALTER TABLE `precures`
      ADD COLUMN `counts_only_when_transformed` tinyint(1) NOT NULL DEFAULT '0'
        COMMENT '1 なら作品単位の作品のプリキュアの欄で、その作品の series_precures に行がある作品（変身した作品）でだけプリキュアとして数える'
        AFTER `alt_form_alias_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_precures_counts_only_when_transformed();
DROP PROCEDURE IF EXISTS _v1191_add_precures_counts_only_when_transformed;
