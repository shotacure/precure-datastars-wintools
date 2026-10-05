-- =====================================================================
-- v1.18.4_add_card_role_misprint.sql
--
-- クレジットの役職（credit_card_roles）に、画面の役職名の誤記を持てるようにする。
--   role_misprint_text  VARCHAR(64) NULL   画面に出た役職名の誤記（例:「デジタル特殊効果」が「デジタル特種効果」と出た）。
--                                          NULL なら誤記なし。正しい表記は role_label_text か役職名（roles.name_ja）。
--
-- クレジットの表示では、誤記を取り消し線で 1 行目に、正しい表記を 2 行目に改行して出す
-- （役職名の欄は幅が狭いので、名前の誤記のように横に並べず正誤で改行する）。集計は role_code のまま。
--
-- 冪等性: 列の存在を INFORMATION_SCHEMA で確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_card_role_misprint;
DELIMITER $$
CREATE PROCEDURE _v1184_add_card_role_misprint()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'role_misprint_text'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `role_misprint_text` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '画面に出た役職名の誤記。NULL なら誤記なし。表示は誤記（取り消し線）と正しい表記を改行して並べる'
        AFTER `role_label_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_card_role_misprint();
DROP PROCEDURE IF EXISTS _v1184_add_card_role_misprint;
