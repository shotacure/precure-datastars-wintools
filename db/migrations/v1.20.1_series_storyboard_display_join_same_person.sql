-- =====================================================================
-- v1.20.1_series_storyboard_display_join_same_person.sql
--
-- 絵コンテ（STORYBOARD）と演出（EPISODE_DIRECTOR）の出し方（series.storyboard_display）に
-- JOIN_SAME_PERSON を足す。
--   SEPARATE           絵コンテと演出をそれぞれの役職名で出す
--   MERGE              いつも「演出」にまとめる。顔ぶれが同じなら「(絵コンテ・)演出 ｜ 名前」、
--                      違えば「演出」の欄に「A (絵コンテ)」「B (演出)」と並べる（TV の無印〜ドキドキ!）
--   MERGE_SAME_PERSON  顔ぶれが同じときだけ「(絵コンテ・)演出 ｜ 名前」にまとめ、違えば
--                      それぞれの役職名で出す（TV のハピネスチャージ以降。兼業の回は画面に「演出」だけが出る）
--   JOIN_SAME_PERSON   顔ぶれが同じときだけ「絵コンテ・演出 ｜ 名前」にまとめ、違えば
--                      それぞれの役職名で出す（オトナシリーズ。兼業の回は画面に「絵コンテ・演出」が出る）
--
-- 冪等性: INFORMATION_SCHEMA で列の型を確認し、JOIN_SAME_PERSON が無いときだけ ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_series_storyboard_display_join;
DELIMITER $$
CREATE PROCEDURE _v1201_series_storyboard_display_join()
BEGIN
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'storyboard_display'
       AND COLUMN_TYPE NOT LIKE '%JOIN_SAME_PERSON%'
  ) THEN
    ALTER TABLE `series`
      MODIFY COLUMN `storyboard_display` enum('SEPARATE','MERGE','MERGE_SAME_PERSON','JOIN_SAME_PERSON') NOT NULL DEFAULT 'SEPARATE'
        COMMENT '絵コンテと演出の出し方（SEPARATE=役職名ごと / MERGE=いつも演出にまとめる / MERGE_SAME_PERSON=同じ顔ぶれのときだけ(絵コンテ・)演出にまとめる / JOIN_SAME_PERSON=同じ顔ぶれのときだけ絵コンテ・演出にまとめる）';
  END IF;
END$$
DELIMITER ;

CALL _v1201_series_storyboard_display_join();
DROP PROCEDURE IF EXISTS _v1201_series_storyboard_display_join;
