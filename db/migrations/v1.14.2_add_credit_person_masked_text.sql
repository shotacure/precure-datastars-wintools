-- =====================================================================
-- v1.14.2_add_credit_person_masked_text.sql
--
-- credit_block_entries に「画面で人物名の代わりに出た表記」の列を追加する。
--   person_masked_text  VARCHAR(32) NULL ... PERSON / CHARACTER_VOICE 人物側の伏せ字表記
--
-- 設計方針:
--   - 声優を伏せて「謎の少女　？」のようにクレジットされた回で、画面の表記（「？」）を残す。
--   - 名義（person_alias_id）は本来の人物に正しく紐付けたまま持つ。出演回数などの集計は通常どおり数える。
--   - NULL = 伏せ字なし。値があれば、表示は「伏せ字 (正名義)」の形にする。
--   - 誤記（person_misprint_text）と同じく、マスタを汚さないエントリ単位の補助情報。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在確認をしてから ALTER を発行する。
-- =====================================================================

START TRANSACTION;

SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'credit_block_entries'
    AND COLUMN_NAME  = 'person_masked_text'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE credit_block_entries
     ADD COLUMN `person_masked_text` VARCHAR(32)
       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks
       DEFAULT NULL
       COMMENT ''PERSON / CHARACTER_VOICE 人物名の代わりに画面に出た伏せ字表記（NULL=伏せ字なし）''
       AFTER `company_misprint_text`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

COMMIT;
