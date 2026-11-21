-- =====================================================================
-- v1.21.1_add_leading_company_display_text.sql
--
-- ブロックの筆頭の会社（credit_role_blocks.leading_company_alias_id / leading_company2_alias_id）に、
-- 画面に出た会社名の表記を持てるようにする。
--   leading_company_display_text   VARCHAR(255) NULL  筆頭の 1 社目の画面の表記
--   leading_company2_display_text  VARCHAR(255) NULL  筆頭の 2 社目の画面の表記
--
-- 名義と空白・記号だけが違う表記のときだけ入れ、NULL なら名義の名前で出す（credit_block_entries.company_display_text と同じ考え方）。
-- 表示は画面の表記、リンク先と集計は名義の会社のまま。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1211_add_leading_company_display_text;
DELIMITER $$
CREATE PROCEDURE _v1211_add_leading_company_display_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company_display_text'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company_display_text` varchar(255) DEFAULT NULL
        COMMENT '筆頭の 1 社目の画面に出た会社名の表記。名義と空白・記号だけが違うときだけ。NULL は名義の名前のまま'
        AFTER `leading_company_alias_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company2_display_text'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company2_display_text` varchar(255) DEFAULT NULL
        COMMENT '筆頭の 2 社目の画面に出た会社名の表記。名義と空白・記号だけが違うときだけ。NULL は名義の名前のまま'
        AFTER `leading_company2_alias_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1211_add_leading_company_display_text();
DROP PROCEDURE IF EXISTS _v1211_add_leading_company_display_text;
