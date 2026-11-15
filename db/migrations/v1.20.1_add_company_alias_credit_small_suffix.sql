-- =====================================================================
-- v1.20.1_add_company_alias_credit_small_suffix.sql
--
-- 企業の名義（company_aliases）に、クレジットの表記の末尾のうち画面で小さめの字で出ている部分を持てるようにする。
--   credit_small_suffix  VARCHAR(64) NULL
--
-- 用途：名義「01(ゼロワン)」のように、画面では読みのカッコ書きが小さめの字で出ている会社がある。
-- サイトのクレジットでは、名前の末尾がこの字と一致するとき、手前の部分だけを会社へのリンクにし、
-- この部分をリンクの後ろに所属の表記と同じ小さめの字で出す。名義の名前は画面の表記のまま持つ。
-- NULL は名前全体をそのまま出す。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_add_company_alias_credit_small_suffix;
DELIMITER $$
CREATE PROCEDURE _v1201_add_company_alias_credit_small_suffix()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'company_aliases' AND COLUMN_NAME = 'credit_small_suffix'
  ) THEN
    ALTER TABLE `company_aliases`
      ADD COLUMN `credit_small_suffix` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットの表記の末尾のうち画面で小さめの字で出ている部分。NULL は名前全体をそのまま出す'
        AFTER `display_text_override`;
  END IF;
END$$
DELIMITER ;

CALL _v1201_add_company_alias_credit_small_suffix();
DROP PROCEDURE IF EXISTS _v1201_add_company_alias_credit_small_suffix;
