-- =====================================================================
-- v1.18.6_add_entry_affiliation_logo.sql
--
-- クレジットのエントリ（credit_block_entries）の所属に、画面で使われたロゴの版を持てるようにする。
--   affiliation_logo_id  INT NULL  所属がロゴで出ているときの logos.logo_id（任意）。
--
-- 用途：TV の OP・ED では「（ABC）」「（ADK）」の所属が文字ではなく各社のロゴで出ていて、ロゴは時期で版が変わる
-- （ADK は 2014-06-01 から 2014 年ロゴ）。所属の会社名義（affiliation_company_alias_id）はそのまま持ち、
-- どの版のロゴで出ていたかだけを足す。サイトのクレジットと Catalog のプレビューの表示は変えない
-- （所属は今までどおり会社名のテキストリンクで出す）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_add_entry_affiliation_logo;
DELIMITER $$
CREATE PROCEDURE _v1186_add_entry_affiliation_logo()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'affiliation_logo_id'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `affiliation_logo_id` int DEFAULT NULL
        COMMENT '所属がロゴで出ているときのロゴ（logos.logo_id）。表示は所属の会社名のまま'
        AFTER `affiliation_person_alias_id`,
      ADD KEY `ix_be_aff_logo` (`affiliation_logo_id`),
      ADD CONSTRAINT `fk_be_aff_logo` FOREIGN KEY (`affiliation_logo_id`) REFERENCES `logos` (`logo_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1186_add_entry_affiliation_logo();
DROP PROCEDURE IF EXISTS _v1186_add_entry_affiliation_logo;
