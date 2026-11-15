-- =====================================================================
-- v1.20.1_add_entry_character_unit.sql
--
-- 声の出演の役名の後ろに、そのキャラが属するユニットの名前を画面どおりに出せるようにする。
-- （「ヤギ（サファリア）」「リーダー（アンティアンズ）」の「サファリア」「アンティアンズ」）
--
-- credit_block_entries に列を足す（parallel_small の後ろ）。
--   character_unit_alias_id  INT NULL  → person_aliases.alias_id
--     役名の後ろに小さいカッコで出すユニットの名義。ユニットはキャラのユニットと同じく、persons 行を持たない名義
--     （person_alias_members にキャラと声優の組をメンバーとして持つ）。NULL は何も出さない。
--     CHARACTER_VOICE のエントリにだけ付ける。外部キーに ON DELETE SET NULL を持つ列は MySQL で CHECK 制約に使えないので、
--     この決まりは入力の側（gen_movie.py・Catalog の一括入力）で守る（affiliation_person_alias_id と同じ）。
--
-- 冪等性: INFORMATION_SCHEMA で列・キー・制約の有無を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_add_entry_character_unit;
DELIMITER $$
CREATE PROCEDURE _v1201_add_entry_character_unit()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'character_unit_alias_id'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `character_unit_alias_id` int DEFAULT NULL
        COMMENT '声の出演の役名の後ろに小さいカッコで出すキャラのユニットの名義（person_aliases）。NULL は出さない'
        AFTER `parallel_small`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND INDEX_NAME = 'ix_be_character_unit'
  ) THEN
    ALTER TABLE `credit_block_entries` ADD KEY `ix_be_character_unit` (`character_unit_alias_id`);
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND CONSTRAINT_NAME = 'fk_be_character_unit_alias'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD CONSTRAINT `fk_be_character_unit_alias` FOREIGN KEY (`character_unit_alias_id`)
        REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1201_add_entry_character_unit();
DROP PROCEDURE IF EXISTS _v1201_add_entry_character_unit;

SELECT 'v1.20.1 migration completed: credit_block_entries += character_unit_alias_id' AS final_status;
