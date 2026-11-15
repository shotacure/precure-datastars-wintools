-- =====================================================================
-- v1.20.0_add_block_leading_company2.sql
--
-- クレジットのブロックの筆頭に、会社を 2 社まで置けるようにする。
--   leading_company2_alias_id  INT NULL          筆頭の 2 社目の会社名義（company_aliases.alias_id）
--   leading_company_separator  VARCHAR(16) NULL  2 社の間に出す字（画面の表記どおり。例 "/"）
--   leading_company_suffix     VARCHAR(64) NULL  筆頭の会社名の後ろに付く字（画面の表記どおり。例 " 撮影班"）
--
-- 用途：「スタジオトゥインクル/AMGA 撮影班」のように、名前のまとまりの頭に 2 社が連名で出て、
-- 後ろに字が付くもの。筆頭の行は「社名1 + 間の字 + 社名2 + 後ろの字」を 1 行に出し、社名はそれぞれ
-- 会社のページへリンクする。2 社目も 1 社目（leading_company_alias_id）と同じく所属・集計に数える。
-- 後ろの字は 2 社目が無い（1 社だけの）ときも使える。
-- 2 社目の外部キーは leading_company_alias_id と同じく、名義が消えたら NULL にする。
--
-- 冪等性: INFORMATION_SCHEMA で列・外部キーの存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_block_leading_company2;
DELIMITER $$
CREATE PROCEDURE _v1200_add_block_leading_company2()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company2_alias_id'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company2_alias_id` int DEFAULT NULL
        COMMENT '筆頭の 2 社目の会社名義（company_aliases.alias_id）。2 社が連名で出るときの 2 社目。所属・集計に数える'
        AFTER `leading_company_not_on_screen`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company_separator'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company_separator` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '筆頭の 2 社の間に出す字（画面の表記どおり。例 "/"）'
        AFTER `leading_company2_alias_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company_suffix'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company_suffix` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '筆頭の会社名の後ろに付く字（画面の表記どおり。例 " 撮影班"）。1 社だけのときも使える'
        AFTER `leading_company_separator`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND INDEX_NAME = 'ix_block_lead_company2'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD KEY `ix_block_lead_company2` (`leading_company2_alias_id`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND CONSTRAINT_NAME = 'fk_block_lead_company2'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD CONSTRAINT `fk_block_lead_company2` FOREIGN KEY (`leading_company2_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_block_leading_company2();
DROP PROCEDURE IF EXISTS _v1200_add_block_leading_company2;
