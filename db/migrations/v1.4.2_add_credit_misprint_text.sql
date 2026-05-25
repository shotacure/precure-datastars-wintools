-- =====================================================================
-- v1.4.2_add_credit_misprint_text.sql
--
-- credit_block_entries に「クレジット時の誤記」フリーテキスト列を 3 つ追加する。
--   person_misprint_text     VARCHAR(255) NULL  ... PERSON / CHARACTER_VOICE 人物側の誤記
--   character_misprint_text  VARCHAR(255) NULL  ... CHARACTER_VOICE キャラ側の誤記
--   company_misprint_text    VARCHAR(255) NULL  ... COMPANY / LOGO 屋号側の誤記
--
-- 設計方針:
--   - 「名義」とは別管理。誤記は「クレジット時の事故」なのでマスタを汚さない。
--   - NULL = 誤記なし。値があれば誤記あり（フラグは設けない）。
--   - 表示は「打ち消し線で誤記」+ 半角SP + 「正名義」を並べる運用。
--   - 整合性ルール（誤記列が許される EntryKind の制約）はトリガでなくアプリ層で担保する。
--     誤記は補助情報であり、誤った組み合わせで投入されても運用上は注意喚起レベルで足りる
--     （将来必要になればトリガを追加する判断余地は残す）。
--
-- 冪等性: INFORMATION_SCHEMA で各列の存在確認をしてから ALTER を発行する。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 1. person_misprint_text を追加
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'credit_block_entries'
    AND COLUMN_NAME  = 'person_misprint_text'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE credit_block_entries
     ADD COLUMN `person_misprint_text` VARCHAR(255)
       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks
       DEFAULT NULL
       COMMENT ''PERSON / CHARACTER_VOICE 人物側の誤記表記（NULL=誤記なし）''
       AFTER `raw_character_text`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. character_misprint_text を追加
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'credit_block_entries'
    AND COLUMN_NAME  = 'character_misprint_text'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE credit_block_entries
     ADD COLUMN `character_misprint_text` VARCHAR(255)
       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks
       DEFAULT NULL
       COMMENT ''CHARACTER_VOICE キャラ側の誤記表記（NULL=誤記なし）''
       AFTER `person_misprint_text`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. company_misprint_text を追加
-- ---------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'credit_block_entries'
    AND COLUMN_NAME  = 'company_misprint_text'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE credit_block_entries
     ADD COLUMN `company_misprint_text` VARCHAR(255)
       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks
       DEFAULT NULL
       COMMENT ''COMPANY / LOGO 屋号側の誤記表記（NULL=誤記なし）''
       AFTER `character_misprint_text`',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

COMMIT;
