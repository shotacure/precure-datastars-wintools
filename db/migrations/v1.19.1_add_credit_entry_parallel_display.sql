-- =====================================================================
-- v1.19.1_add_credit_entry_parallel_display.sql
--
-- クレジットの併記（parallel_with_entry_id）の描き方を、画面どおりに持てるようにする。
--
-- credit_block_entries に列を足す（parallel_with_entry_id の後ろ）。
--   parallel_prefix  VARCHAR(8) NULL
--     併記のエントリ（parallel_with_entry_id のある行）の名前の前に付く字。
--     「ダークマター／クマタ」の「／」、「野乃はな（キュアエール）」の「（」など。NULL は何も付けない。
--   parallel_suffix  VARCHAR(8) NULL
--     併記のエントリの名前の後ろに付く字。「（キュアエール）」の「）」など。NULL は何も付けない。
--   parallel_small   TINYINT(1) NOT NULL DEFAULT 0
--     1 … 併記のエントリ（前後の字を含む）を小さい字で描く。
--
-- 併記のエントリは独立した行にならず、相手（parallel_with_entry_id が指す、同じブロックの前のエントリ）の
-- 行に並べて描く。声の出演では役名の升に「相手の役名＋prefix＋役名＋suffix」を並べ、声優は 1 人だけ出す。
-- 名義は分けたまま持つので、キャラごとの集計には両方とも数える。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_credit_entry_parallel_display;
DELIMITER $$
CREATE PROCEDURE _v1191_add_credit_entry_parallel_display()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'parallel_prefix'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `parallel_prefix` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '併記のエントリの名前の前に付く字（「／」「（」など）'
        AFTER `parallel_with_entry_id`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'parallel_suffix'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `parallel_suffix` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '併記のエントリの名前の後ろに付く字（「）」など）'
        AFTER `parallel_prefix`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'parallel_small'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `parallel_small` tinyint(1) NOT NULL DEFAULT '0'
        COMMENT '1 なら併記のエントリ（前後の字を含む）を小さい字で描く'
        AFTER `parallel_suffix`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_credit_entry_parallel_display();
DROP PROCEDURE IF EXISTS _v1191_add_credit_entry_parallel_display;

SELECT 'v1.19.1 migration completed: credit_block_entries += parallel_prefix / parallel_suffix / parallel_small' AS final_status;
