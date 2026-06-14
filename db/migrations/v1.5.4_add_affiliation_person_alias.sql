-- ===========================================================================
-- v1.5.4 マイグレーション: credit_block_entries に affiliation_person_alias_id 追加
--
--   クレジットエントリの「所属（小カッコ）」を、企業屋号(affiliation_company_alias_id) /
--   フリーテキスト(affiliation_text) に加えて「人物名義(ユニット等)」へも構造的に引き当て
--   られるようにする。表示はテキスト同様（リンクなし）だが、マスタを指すことで正規化・逆引き
--   を可能にする。affiliation_company_alias_id とは排他（どちらか一方のみ）。
--
--   冪等：列・索引・FK は INFORMATION_SCHEMA で存在チェックしてから追加。トリガは DROP→再作成。
--   再実行しても安全。
-- ===========================================================================

-- 1) 列 / 索引 / FK の追加（冪等）
DROP PROCEDURE IF EXISTS _mig_add_aff_person;
DELIMITER ;;
CREATE PROCEDURE _mig_add_aff_person()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
      AND COLUMN_NAME = 'affiliation_person_alias_id'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD COLUMN `affiliation_person_alias_id` int DEFAULT NULL AFTER `affiliation_company_alias_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
      AND INDEX_NAME = 'ix_be_aff_person'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD KEY `ix_be_aff_person` (`affiliation_person_alias_id`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
      AND CONSTRAINT_NAME = 'fk_be_aff_person_alias'
  ) THEN
    ALTER TABLE `credit_block_entries`
      ADD CONSTRAINT `fk_be_aff_person_alias`
        FOREIGN KEY (`affiliation_person_alias_id`) REFERENCES `person_aliases` (`alias_id`)
        ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END;;
DELIMITER ;
CALL _mig_add_aff_person();
DROP PROCEDURE _mig_add_aff_person;

-- 2) 整合性トリガを再作成（所属の構造参照を「企業屋号 / 人物名義のいずれか一方」に制限する
--    排他チェックを末尾に追加。それ以外のルールは従来どおり）。
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bi_consistency`;
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bu_consistency`;
DELIMITER ;;
CREATE TRIGGER `trg_credit_block_entries_bi_consistency`
BEFORE INSERT ON `credit_block_entries`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;

  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
  -- 所属の構造参照（企業屋号 / 人物名義）は最大 1 つ。両立は禁止（affiliation_text の併記は可）。
  IF NEW.affiliation_company_alias_id IS NOT NULL AND NEW.affiliation_person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: affiliation_company_alias_id and affiliation_person_alias_id are mutually exclusive';
  END IF;
END;;

CREATE TRIGGER `trg_credit_block_entries_bu_consistency`
BEFORE UPDATE ON `credit_block_entries`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;

  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
  -- 所属の構造参照（企業屋号 / 人物名義）は最大 1 つ。両立は禁止（affiliation_text の併記は可）。
  IF NEW.affiliation_company_alias_id IS NOT NULL AND NEW.affiliation_person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: affiliation_company_alias_id and affiliation_person_alias_id are mutually exclusive';
  END IF;
END;;
DELIMITER ;
