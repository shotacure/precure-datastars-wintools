-- =====================================================================
-- v1.18.6_add_card_role_not_on_screen.sql
--
-- クレジットの役職（credit_card_roles）に、その行が画面にはクレジットされていないことを示す印を持てるようにする。
--   not_on_screen  TINYINT NOT NULL DEFAULT 0   1 ならその行は画面には出ていない（クレジット漏れ）。
--                                                データ（role_code・エントリ）は入れたまま集計・関与に数え、
--                                                クレジットの表示だけ括弧で包んで薄くし、画面に無かったことを示す。
--
-- 用途：毎回出る役職が、ある回だけ画面から抜けているとき（例：声の出演の「協力 東映東京撮影所」）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_add_card_role_not_on_screen;
DELIMITER $$
CREATE PROCEDURE _v1186_add_card_role_not_on_screen()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'not_on_screen'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `not_on_screen` tinyint NOT NULL DEFAULT 0
        COMMENT '1 ならこの行は画面にはクレジットされていない（データは入れて集計に数え、表示だけ括弧で示す）'
        AFTER `hide_role_label`;
  END IF;
END$$
DELIMITER ;

CALL _v1186_add_card_role_not_on_screen();
DROP PROCEDURE IF EXISTS _v1186_add_card_role_not_on_screen;
