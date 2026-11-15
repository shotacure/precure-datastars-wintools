-- =====================================================================
-- v1.20.1_add_provisional_credit_flag.sql
--
-- 仮クレジットのフラグ（episodes.is_provisional_credit / series.is_provisional_credit）を足す。
-- 1 のスコープ（回・作品）の credits は、正式なクレジット入力までの仮のもの。
--   - ページにはクレジットとして出さない（エピソード詳細・作品詳細のクレジット欄）
--   - 人物・会社・キャラのページ、統計、記念日には数えない
--   - 各話の札とメインスタッフの表示にだけ使う
-- 正式入力のときは、そのスコープの仮の credits を消してフラグを 0 に戻す。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を見てから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_provisional_credit_flag;
DELIMITER $$
CREATE PROCEDURE _v1201_provisional_credit_flag()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episodes' AND COLUMN_NAME = 'is_provisional_credit'
  ) THEN
    ALTER TABLE `episodes`
      ADD COLUMN `is_provisional_credit` tinyint NOT NULL DEFAULT 0
        COMMENT '1=この回の credits は正式入力までの仮（ページに出さず、各話の札・メインスタッフにだけ使う）' AFTER `notes`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'is_provisional_credit'
  ) THEN
    ALTER TABLE `series`
      ADD COLUMN `is_provisional_credit` tinyint NOT NULL DEFAULT 0
        COMMENT '1=この作品の credits は正式入力までの仮（ページに出さず、メインスタッフにだけ使う）' AFTER `storyboard_display`;
  END IF;
END$$
DELIMITER ;

CALL _v1201_provisional_credit_flag();
DROP PROCEDURE IF EXISTS _v1201_provisional_credit_flag;
