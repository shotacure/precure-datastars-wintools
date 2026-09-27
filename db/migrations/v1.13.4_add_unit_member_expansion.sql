-- =====================================================================
-- v1.13.4_add_unit_member_expansion.sql
--
-- ユニット名義で歌唱された録音で、ユニット名の後ろにメンバーを括弧書きで展開して表示できるようにする。
-- 例: 歌：ぷりきゅあ5（夢原のぞみ/キュアドリーム(CV:三瓶由布子)、夏木りん/キュアルージュ(CV:竹内順子)、…）
--
--   person_alias_members + 1 列:
--     member_slash_character_alias_id  CHARACTER メンバーの「/」で並べるもう一方の名義（→ character_aliases、任意）。
--                                      変身前と変身後を並べる表記の後者。声優は member_voice_person_alias_id を共有する
--   CHECK ck_pam_kind_columns を張り替え:
--     PERSON メンバーでは member_slash_character_alias_id を NULL に限定する。
--
--   song_recording_singers + 1 列:
--     expand_unit_members  PERSON 行の名義がユニット名義のとき、表示でメンバーを展開するか（既定 0）。
--                          同じユニットでも歌の行では展開し、コーラスの行では名前だけ出す、のように行ごとに選ぶ。
--
-- 冪等性: 列・インデックス・FK・CHECK の状態を確認してから追加 / 張り替えする。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1134_unit_member_expansion;
DELIMITER $$
CREATE PROCEDURE _v1134_unit_member_expansion()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'person_alias_members'
                    AND COLUMN_NAME = 'member_slash_character_alias_id') THEN
    ALTER TABLE `person_alias_members`
      ADD COLUMN `member_slash_character_alias_id` int DEFAULT NULL
        COMMENT 'CHARACTER メンバーの「/」で並べるもう一方の名義（→ character_aliases）。PERSON メンバーでは NULL'
        AFTER `member_voice_person_alias_id`,
      ADD KEY `ix_pam_member_slash_character` (`member_slash_character_alias_id`);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'person_alias_members'
                    AND CONSTRAINT_NAME = 'fk_pam_slash_character') THEN
    ALTER TABLE `person_alias_members`
      ADD CONSTRAINT `fk_pam_slash_character` FOREIGN KEY (`member_slash_character_alias_id`)
        REFERENCES `character_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE NO ACTION;
  END IF;

  -- CHECK の張り替え：定義に member_slash_character_alias_id を含まない旧版なら DROP → ADD する。
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
                  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_pam_kind_columns'
                    AND CHECK_CLAUSE LIKE '%member_slash_character_alias_id%') THEN
    ALTER TABLE `person_alias_members`
      DROP CHECK `ck_pam_kind_columns`,
      ADD CONSTRAINT `ck_pam_kind_columns` CHECK (
           (`member_kind` = 'PERSON'    AND `member_person_alias_id`    IS NOT NULL AND `member_character_alias_id` IS NULL
                                        AND `member_voice_person_alias_id` IS NULL AND `member_slash_character_alias_id` IS NULL)
        OR (`member_kind` = 'CHARACTER' AND `member_character_alias_id` IS NOT NULL AND `member_person_alias_id`    IS NULL)
      );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'song_recording_singers'
                    AND COLUMN_NAME = 'expand_unit_members') THEN
    ALTER TABLE `song_recording_singers`
      ADD COLUMN `expand_unit_members` tinyint(1) NOT NULL DEFAULT 0
        COMMENT 'PERSON 行の名義がユニット名義のとき、表示でメンバーを括弧書きで展開するか（1=展開）'
        AFTER `affiliation_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1134_unit_member_expansion();
DROP PROCEDURE IF EXISTS _v1134_unit_member_expansion;
