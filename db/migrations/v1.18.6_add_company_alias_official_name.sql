-- =====================================================================
-- v1.18.6_add_company_alias_official_name.sql
--
-- 企業の名義（company_aliases）に「正式名（社名）の名義」の印を持てるようにする。
--   is_official_name  TINYINT NOT NULL DEFAULT 0
--
-- 用途：名義はクレジットの表記なので、正式名（「株式会社マーベラスAQL」）と通称（「マーベラスAQL」）・
-- 略称（「MMV」）が同じ並びに混ざる。正式名の名義に印を付け、有効期間（valid_from / valid_to）と
-- 前身・後継（predecessor_alias_id / successor_alias_id）でたどって、企業詳細の「商号履歴」に出す。
-- クレジットに一度も出ていない昔の正式名も、この印を付けた名義として持つ。
-- companies.name は、正式名の名義のうちいちばん新しいものにそろえる（運用で合わせる）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_add_company_alias_official_name;
DELIMITER $$
CREATE PROCEDURE _v1186_add_company_alias_official_name()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'company_aliases' AND COLUMN_NAME = 'is_official_name'
  ) THEN
    ALTER TABLE `company_aliases`
      ADD COLUMN `is_official_name` tinyint NOT NULL DEFAULT 0
        COMMENT '1 なら正式名（社名）の名義。企業詳細の商号履歴に出す'
        AFTER `name_en`;
  END IF;
END$$
DELIMITER ;

CALL _v1186_add_company_alias_official_name();
DROP PROCEDURE IF EXISTS _v1186_add_company_alias_official_name;
