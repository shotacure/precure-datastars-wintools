-- =====================================================================
-- v1.21.1_add_track_sub_range.sql
--
-- 1 トラックに複数の曲・版が入っているとき（枝番 sub_order の行を持つトラック）に、
-- それぞれがトラックのどこからどこまでかを持てるようにする。
--   sub_start_frames   INT UNSIGNED NULL   トラックの先頭から数えた開始位置（CD-DA のフレーム。1/75 秒）
--   sub_length_frames  INT UNSIGNED NULL   その曲・版の長さ（同じ単位）
--
-- 既存の start_lba・length_frames は、sub_order = 0 の行にトラック全体の位置と長さを持つ決まりのまま変えない
-- （盤の読み取りとの照合・盤の登録・商品ページの尺がこれを前提にしている）。
-- 新しい 2 列は sub_order = 0 の行にも枝番の行にも入れられる。分かっていなければ NULL。
-- 例：劇伴の 2 つの版を無音を挟んで 1 トラックに収めた盤で、前半・後半それぞれの尺を出す。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1211_add_track_sub_range;
DELIMITER $$
CREATE PROCEDURE _v1211_add_track_sub_range()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tracks' AND COLUMN_NAME = 'sub_start_frames'
  ) THEN
    ALTER TABLE `tracks`
      ADD COLUMN `sub_start_frames` int unsigned DEFAULT NULL
        COMMENT '1 トラックに複数の曲・版が入っているとき、この行の曲・版がトラックの先頭から何フレーム目で始まるか（1/75 秒）。分からなければ NULL'
        AFTER `length_frames`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tracks' AND COLUMN_NAME = 'sub_length_frames'
  ) THEN
    ALTER TABLE `tracks`
      ADD COLUMN `sub_length_frames` int unsigned DEFAULT NULL
        COMMENT '1 トラックに複数の曲・版が入っているとき、この行の曲・版の長さ（フレーム。1/75 秒）。分からなければ NULL'
        AFTER `sub_start_frames`;
  END IF;
END$$
DELIMITER ;

CALL _v1211_add_track_sub_range();
DROP PROCEDURE _v1211_add_track_sub_range;

SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_COMMENT
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tracks' AND COLUMN_NAME IN ('sub_start_frames', 'sub_length_frames');
