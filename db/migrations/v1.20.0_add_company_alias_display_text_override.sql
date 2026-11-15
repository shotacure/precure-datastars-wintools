-- =====================================================================
-- v1.20.0_add_company_alias_display_text_override.sql
--
-- 企業の名義（company_aliases）に、クレジットでの表記を名義の名前と別に持てるようにする。
--   display_text_override  VARCHAR(255) NULL
--
-- 用途：ロゴは書かれている文字の名義にぶら下げ、クレジットにはその名義の名前を出す。
-- 文字の無いロゴ（東映アニメーションの名義「プリキュア15周年」にぶら下がるプリキュア15周年のロゴなど）は、
-- 名義の名前のままではロゴだと分からないので、クレジットにだけ「(プリキュア15周年ロゴ)」のような表記を出す。
-- サイトのクレジットと Catalog のプレビューで、会社・ロゴのエントリに使う。リンク先・集計・企業詳細の名義の一覧は名義の名前のまま。
-- NULL は名義の名前で出す。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_company_alias_display_text_override;
DELIMITER $$
CREATE PROCEDURE _v1200_add_company_alias_display_text_override()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'company_aliases' AND COLUMN_NAME = 'display_text_override'
  ) THEN
    ALTER TABLE `company_aliases`
      ADD COLUMN `display_text_override` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'クレジットでの表記（会社・ロゴのエントリ）。NULL は名義の名前で出す'
        AFTER `is_official_name`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_company_alias_display_text_override();
DROP PROCEDURE IF EXISTS _v1200_add_company_alias_display_text_override;
