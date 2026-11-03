-- =====================================================================
-- v1.20.0_add_card_role_label_not_on_screen.sql
--
-- クレジットの役職（credit_card_roles）に、役職名だけが画面にはクレジットされていないことを示す印を持てるようにする。
--   role_label_not_on_screen  TINYINT NOT NULL DEFAULT 0   1 ならその行の役職名（見出し）だけが画面に出ていない。
--                                                           名前・中身は画面に出ている。データ（role_code・エントリ）は
--                                                           入れたまま集計・関与に数え、クレジットの表示では役職名だけを
--                                                           全角の括弧で包んで薄くし、名前はふつうに出す。
--
-- 用途：名前は画面に出ているが役職の見出しが無いとき（例：ED の原画の末尾に 1 人だけで出ている人が、実質は「料理作監」の担当）。
--
-- 行ごと画面に無い印 not_on_screen とは意味が重なるので、同じ行に両方は立てない（CHECK 制約 ck_card_role_not_on_screen_exclusive）。
--
-- 冪等性: INFORMATION_SCHEMA で列・制約の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_card_role_label_not_on_screen;
DELIMITER $$
CREATE PROCEDURE _v1200_add_card_role_label_not_on_screen()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'role_label_not_on_screen'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `role_label_not_on_screen` tinyint NOT NULL DEFAULT 0
        COMMENT '1 ならこの行の役職名だけが画面にはクレジットされていない（名前は画面にある。表示は役職名だけ括弧で示す）'
        AFTER `not_on_screen`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
       AND CONSTRAINT_NAME = 'ck_card_role_not_on_screen_exclusive' AND CONSTRAINT_TYPE = 'CHECK'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD CONSTRAINT `ck_card_role_not_on_screen_exclusive` CHECK (NOT (`not_on_screen` = 1 AND `role_label_not_on_screen` = 1));
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_card_role_label_not_on_screen();
DROP PROCEDURE IF EXISTS _v1200_add_card_role_label_not_on_screen;
