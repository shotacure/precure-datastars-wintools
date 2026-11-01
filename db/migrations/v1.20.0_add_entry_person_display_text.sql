-- =====================================================================
-- v1.20.0_add_entry_person_display_text.sql
--
-- クレジットに出た人物名の表記揺れを、名義を増やさずにエントリごとに持てるようにする。
--   person_display_text  VARCHAR(255) NULL
--
-- 用途：画面の人物名が、既存の名義と空白・中黒・記号の有無や位置だけ違うクレジット
-- （「ロメオ・アンニョンヌエボJr.」に対する画面の「ロメオ・アンニョンヌエボ Jr.」など）。
-- 名義（person_alias_id）は既存の名義につないだまま、画面の表記をここに持ち、表示は名義の名前の代わりにこの字を出す。
-- 誤記（person_misprint_text）とは別で、印は付けない。NULL は名義の名前のまま。
-- PERSON / CHARACTER_VOICE のエントリにだけ付け、空白・記号を除いた字は名義と同じにする（ヘルスチェックの D7 で見張る）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_entry_person_display_text;
DELIMITER $$
CREATE PROCEDURE _v1200_add_entry_person_display_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries' AND COLUMN_NAME = 'person_display_text'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `person_display_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '画面に出た人物名の表記揺れ。名義と空白・記号だけが違うときだけ。誤記ではない。NULL は名義の名前のまま'
        AFTER `person_misprint_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_entry_person_display_text();
DROP PROCEDURE IF EXISTS _v1200_add_entry_person_display_text;
