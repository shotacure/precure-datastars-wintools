-- =====================================================================
-- v1.20.0_add_entry_character_display_text.sql
--
-- クレジットの声の出演の役名を、画面の組み（改行）どおりに持てるようにする。
--   character_display_text  VARCHAR(255) NULL
--
-- 用途：1 つの役名が画面で 2 行以上に折れて出るクレジット（「てんびん座の」「プリンセス」など）。
-- キャラは 1 つのまま、画面の役名を改行（LF）ごと持ち、表示はこの文字を改行の位置で折って描く。NULL は名義の名前のまま。
-- CHARACTER_VOICE のエントリにだけ付け、改行を抜いた字はキャラの名義と同じにする（ヘルスチェックの D6 で見張る）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_entry_character_display_text;
DELIMITER $$
CREATE PROCEDURE _v1200_add_entry_character_display_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'character_display_text'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `character_display_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '画面の組みどおりの役名（改行入り）。役名が画面で折れているときだけ。改行を抜いた字はキャラの名義と同じ。NULL は名義の名前のまま'
        AFTER `character_misprint_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_entry_character_display_text();
DROP PROCEDURE IF EXISTS _v1200_add_entry_character_display_text;
