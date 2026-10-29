-- =====================================================================
-- v1.20.0_add_credit_entry_kind_blank.sql
--
-- credit_block_entries.entry_kind に BLANK（空きの升）を足す。
--
--   BLANK ... 名前も会社も持たない、ブロックの升目の 1 つ分の空き。
--             複数列のブロックで、画面の並びの途中に何も書かれていない升があるとき
--             （例：2 列の左の列だけが 1 行長く、右の列の同じ高さが空いている）に、
--             その位置を埋めて後ろのエントリの列をそろえる。
--             参照列（person_alias_id / character_alias_id / company_alias_id / logo_id / raw_text）は
--             すべて NULL のまま持つ（既存のトリガー trg_credit_block_entries_b{i,u}_consistency の
--             「その種別以外では持てない」の条件で、そのまま担保される）。
--             サイトのクレジットの表では空の升として描き、集計（人物・会社・役職の関わり）では数えない。
--
-- 冪等性: ENUM 定義の現状を INFORMATION_SCHEMA で確認してから ALTER する。既に適用済みなら何もしない。
-- =====================================================================

SET @needs_enum = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'credit_block_entries'
    AND COLUMN_NAME  = 'entry_kind'
    AND COLUMN_TYPE NOT LIKE '%BLANK%'
);
SET @stmt = IF(@needs_enum = 1,
  "ALTER TABLE `credit_block_entries`
     MODIFY COLUMN `entry_kind`
       ENUM('PERSON','CHARACTER_VOICE','COMPANY','LOGO','TEXT','BLANK') NOT NULL",
  "SELECT 'credit_block_entries.entry_kind already has BLANK, skipping ALTER' AS msg");
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.20.0 migration completed: credit_block_entries.entry_kind += BLANK' AS final_status;
