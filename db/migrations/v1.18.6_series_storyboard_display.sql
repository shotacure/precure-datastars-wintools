-- =====================================================================
-- v1.18.6_series_storyboard_display.sql
--
-- 絵コンテ（STORYBOARD）と演出（EPISODE_DIRECTOR）のクレジットの出し方を、作品ごとに 3 通りから選べるようにする。
--   storyboard_display  ENUM NOT NULL DEFAULT 'SEPARATE'
--     SEPARATE           絵コンテと演出をそれぞれの役職名で出す（映画など）
--     MERGE              いつも「演出」にまとめる。同じ人なら「(絵コンテ・)演出 ｜ 名前」、
--                        違う人なら「演出」の欄に「A (絵コンテ)」「B (演出)」と並べる（TV の無印〜ドキドキ!）
--     MERGE_SAME_PERSON  同じ人のときだけ「(絵コンテ・)演出 ｜ 名前」にまとめ、違う人なら
--                        それぞれの役職名で出す（TV のハピネスチャージ以降）
--
-- 2 値の hide_storyboard_role（1 = まとめる）を置き換える。1 の作品は MERGE に移してから列を消す。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER / UPDATE を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_series_storyboard_display;
DELIMITER $$
CREATE PROCEDURE _v1186_series_storyboard_display()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'storyboard_display'
  ) THEN
    ALTER TABLE `series`
      ADD COLUMN `storyboard_display` enum('SEPARATE','MERGE','MERGE_SAME_PERSON') NOT NULL DEFAULT 'SEPARATE'
        COMMENT '絵コンテと演出の出し方（SEPARATE=役職名ごと / MERGE=いつも演出にまとめる / MERGE_SAME_PERSON=同じ人のときだけまとめる）'
        AFTER `vod_intro`;
  END IF;

  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'hide_storyboard_role'
  ) THEN
    UPDATE `series` SET `storyboard_display` = 'MERGE' WHERE `hide_storyboard_role` = 1;
    ALTER TABLE `series` DROP COLUMN `hide_storyboard_role`;
  END IF;
END$$
DELIMITER ;

CALL _v1186_series_storyboard_display();
DROP PROCEDURE IF EXISTS _v1186_series_storyboard_display;
