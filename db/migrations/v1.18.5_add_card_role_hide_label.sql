-- =====================================================================
-- v1.18.5_add_card_role_hide_label.sql
--
-- クレジットの役職（credit_card_roles）に、その行だけ役職名を出さない印を持てるようにする。
--   hide_role_label  TINYINT NOT NULL DEFAULT 0   1 ならクレジットの表示でその行の役職名を出さず、中身（会社・人物）だけを出す。
--                                                   データ（role_code）はそのままなので、集計・関与は変わらない。
--
-- 用途：画面で 1 つの見出しの下に会社ごとのスタッフが続くとき（例：「CG制作協力」の下に 2 社）、
-- 2 社目の見出しの役職の行に立てて、見出しが 2 度出ないようにする。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1185_add_card_role_hide_label;
DELIMITER $$
CREATE PROCEDURE _v1185_add_card_role_hide_label()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'hide_role_label'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `hide_role_label` tinyint NOT NULL DEFAULT 0
        COMMENT '1 ならクレジットの表示でこの行の役職名を出さない（中身だけ出す）'
        AFTER `join_separator`;
  END IF;
END$$
DELIMITER ;

CALL _v1185_add_card_role_hide_label();
DROP PROCEDURE IF EXISTS _v1185_add_card_role_hide_label;
