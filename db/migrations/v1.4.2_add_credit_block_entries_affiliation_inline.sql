-- =====================================================================
-- v1.4.2_add_credit_block_entries_affiliation_inline.sql
--
-- credit_block_entries に「所属表記のインライン / 別行レイアウト」フラグを追加。
-- 入力時の表現を round-trip 保持するための per-entry 表示ヒント。
--
--   1 (既定): インライン表示「名前 (所属)」（既存全データはこの状態）
--   0       : 別行表示「名前 / (所属)」（人名の下に括弧書き）
--
-- パース時：
--   - `名前 (所属)` 同一行 → 1
--   - `名前` の次の行 `(所属)` 単独行吸収 → 0
-- エンコード時：1 ならインライン、0 なら別行で書き戻し（ラウンドトリップ性確保）。
-- 描画時：1 なら従来通り `名前 (所属)`、0 なら `名前<br>(所属)`。
-- =====================================================================

START TRANSACTION;

DROP PROCEDURE IF EXISTS _v142_add_col_if_missing_aff;
DELIMITER $$
CREATE PROCEDURE _v142_add_col_if_missing_aff(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_col;
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

CALL _v142_add_col_if_missing_aff(
  'credit_block_entries',
  'affiliation_inline',
  "TINYINT(1) NOT NULL DEFAULT 1 COMMENT '所属表記のインライン (1=名前 (所属)) / 別行 (0=名前\\n(所属)) レイアウトフラグ。入力時の表現を round-trip 保持するための表示ヒント。' AFTER `affiliation_text`"
);

DROP PROCEDURE _v142_add_col_if_missing_aff;

COMMIT;
