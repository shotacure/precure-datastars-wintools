-- =====================================================================
-- v1.20.0_add_block_leading_text.sql
--
-- クレジットのブロックの筆頭を、会社のデータを作らずに文字だけで持てるようにする。
--   leading_text  VARCHAR(128) NULL
--
-- 用途：「いこーよ・アンバサダーのみなさん」のように、名前のまとまりの頭に団体の名前が出るが、
-- 会社・団体としてのデータは持たないもの。表示は筆頭の会社（leading_company_alias_id）と同じで、
-- 団体の名前を 1 行に出し、下の名前を字下げする。リンクは付けず、所属・集計にも数えない。
-- 筆頭の会社と同時には使わない（両方あるときは会社を出す）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_block_leading_text;
DELIMITER $$
CREATE PROCEDURE _v1200_add_block_leading_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_text'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_text` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'ブロックの筆頭の団体を文字だけで持つ（画面の表記どおり）。会社のデータは作らず、表示は筆頭の会社と同じく下の名前を字下げする'
        AFTER `leading_company_not_on_screen`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_block_leading_text();
DROP PROCEDURE IF EXISTS _v1200_add_block_leading_text;
