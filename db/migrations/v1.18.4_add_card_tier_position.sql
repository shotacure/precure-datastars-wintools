-- =====================================================================
-- v1.18.4_add_card_tier_position.sql
--
-- クレジットのティア（credit_card_tiers）に、画面の上での位置を持てるようにする。
--   position_v  ENUM('T','M','B') NULL   縦の位置（T = 上、M = 中、B = 下）。
--   position_h  ENUM('L','C','R') NULL   横の位置（L = 左、C = 中央、R = 右）。
--
-- ティアは 1 枚のカードの中で横位置の違うまとまり（左の列・右の列・下の中央など）。tier_no は並び順だけで
-- 位置を表さないので、画面のどこにあったかを情報として残す。NULL は未確認。サイトのクレジットは今までどおり
-- ティアを縦に積んで出し、位置は表示に使わない。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_card_tier_position;
DELIMITER $$
CREATE PROCEDURE _v1184_add_card_tier_position()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_tiers' AND COLUMN_NAME = 'position_v'
  ) THEN
    ALTER TABLE `credit_card_tiers`
      ADD COLUMN `position_v` enum('T','M','B') DEFAULT NULL
        COMMENT '画面の縦の位置（T=上 / M=中 / B=下）。NULL は未確認'
        AFTER `tier_no`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_tiers' AND COLUMN_NAME = 'position_h'
  ) THEN
    ALTER TABLE `credit_card_tiers`
      ADD COLUMN `position_h` enum('L','C','R') DEFAULT NULL
        COMMENT '画面の横の位置（L=左 / C=中央 / R=右）。NULL は未確認'
        AFTER `position_v`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_card_tier_position();
DROP PROCEDURE IF EXISTS _v1184_add_card_tier_position;
