-- =====================================================================
-- v1.20.0_add_entry_same_layout.sql
--
-- 声の出演で、前の行と同じ役名・同じ声優が続くときの画面の組み方を持てるようにする。
--   same_character_layout  ENUM('REPEAT','TOP','MIDDLE') NULL
--   same_person_layout     ENUM('REPEAT','TOP','MIDDLE') NULL
--
-- 用途：同じブロックで直前の CHARACTER_VOICE と役名（または声優）が同じ行に、画面での出し方を入れる。
--   REPEAT = 毎行書く / TOP = まとまりの最初の行に 1 回だけ書き、以降の行は空き / MIDDLE = まとまりの縦の真ん中に 1 回だけ書く。
-- まとまりの最初の行と、前の行と続かない行は NULL。描き方はこの値だけで決め、文字が同じかどうかでは決めない。
-- CHARACTER_VOICE のエントリにだけ付ける（CHECK 制約で担保）。
--
-- 冪等性: INFORMATION_SCHEMA で列・制約の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_entry_same_layout;
DELIMITER $$
CREATE PROCEDURE _v1200_add_entry_same_layout()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'same_character_layout'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `same_character_layout` enum('REPEAT','TOP','MIDDLE') DEFAULT NULL
        COMMENT '前の行と同じ役名のときの画面の組み方（REPEAT=毎行書く / TOP=最初の行に 1 回 / MIDDLE=縦の真ん中に 1 回）。前の行と続かない行は NULL'
        AFTER `character_display_text`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'same_person_layout'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `same_person_layout` enum('REPEAT','TOP','MIDDLE') DEFAULT NULL
        COMMENT '前の行と同じ声優のときの画面の組み方（REPEAT=毎行書く / TOP=最初の行に 1 回 / MIDDLE=縦の真ん中に 1 回）。前の行と続かない行は NULL'
        AFTER `same_character_layout`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND CONSTRAINT_NAME = 'ck_entry_same_layout_voice'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD CONSTRAINT `ck_entry_same_layout_voice`
        CHECK ((`entry_kind` = 'CHARACTER_VOICE') OR (`same_character_layout` IS NULL AND `same_person_layout` IS NULL));
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_entry_same_layout();
DROP PROCEDURE IF EXISTS _v1200_add_entry_same_layout;
