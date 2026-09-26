-- =====================================================================
-- v1.12.10_add_unit_member_voice.sql
--
-- ユニット名義の構成メンバー（person_alias_members）に、キャラクターメンバーを演じる
-- 声優の名義を持たせる。
--
-- ユニット名義（例: 「Splash Stars」= 咲・舞・フラッピ・チョッピ）で歌唱された録音を、
-- サイト上でメンバーのキャラクターと、その声優の双方から辿れるようにするため。
--
--   person_alias_members + 1 列:
--     member_voice_person_alias_id  CHARACTER メンバーの声優名義（→ person_aliases、任意）
--   CHECK ck_pam_kind_columns を張り替え:
--     PERSON メンバーでは member_voice_person_alias_id を NULL に限定する。
--
-- 冪等性: 列・FK・CHECK の状態を確認してから追加 / 張り替えする。
-- =====================================================================

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'person_alias_members'
       AND COLUMN_NAME  = 'member_voice_person_alias_id'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `person_alias_members`
        ADD COLUMN `member_voice_person_alias_id` int DEFAULT NULL
            COMMENT ''CHARACTER メンバーを演じる声優の名義（→ person_aliases）。PERSON メンバーでは NULL''
            AFTER `member_character_alias_id`,
        ADD KEY `ix_pam_member_voice` (`member_voice_person_alias_id`);',
    'SELECT ''person_alias_members.member_voice_person_alias_id already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @fk_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA    = DATABASE()
       AND TABLE_NAME      = 'person_alias_members'
       AND CONSTRAINT_NAME = 'fk_pam_voice'
);
SET @ddl := IF(@fk_exists = 0,
    'ALTER TABLE `person_alias_members`
        ADD CONSTRAINT `fk_pam_voice` FOREIGN KEY (`member_voice_person_alias_id`)
            REFERENCES `person_aliases` (`alias_id`)
            ON DELETE RESTRICT ON UPDATE NO ACTION;',
    'SELECT ''fk_pam_voice already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- CHECK の張り替え：定義に member_voice_person_alias_id を含まない旧版なら DROP → ADD する。
SET @ck_is_new := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
     WHERE CONSTRAINT_SCHEMA = DATABASE()
       AND CONSTRAINT_NAME   = 'ck_pam_kind_columns'
       AND CHECK_CLAUSE LIKE '%member_voice_person_alias_id%'
);
SET @ddl := IF(@ck_is_new = 0,
    'ALTER TABLE `person_alias_members`
        DROP CHECK `ck_pam_kind_columns`,
        ADD CONSTRAINT `ck_pam_kind_columns` CHECK (
             (`member_kind` = ''PERSON''    AND `member_person_alias_id`    IS NOT NULL AND `member_character_alias_id` IS NULL AND `member_voice_person_alias_id` IS NULL)
          OR (`member_kind` = ''CHARACTER'' AND `member_character_alias_id` IS NOT NULL AND `member_person_alias_id`    IS NULL)
        );',
    'SELECT ''ck_pam_kind_columns already includes member_voice_person_alias_id, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
