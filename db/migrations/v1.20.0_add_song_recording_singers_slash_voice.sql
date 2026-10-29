-- =====================================================================
-- v1.20.0_add_song_recording_singers_slash_voice.sql
--
-- 歌唱者連名（song_recording_singers）のキャラ歌唱の行（CHARACTER_WITH_CV）に、
-- 2 人目の声優を持たせる。1 つのキャラを姿によって 2 人の声優が演じる歌唱
-- （例: 「ハリハム・ハリー(CV:野田順子／福島 潤)」）を、2 人とも名義として持つため。
--
--   song_recording_singers + 1 列:
--     slash_voice_person_alias_id  2 人目の声優の名義（→ person_aliases、任意）。
--                                  表示は「キャラ(CV:声優1 / 声優2)」。
--   + 索引 ix_srs_slash_voice、外部キー fk_srs_slash_voice（ON DELETE RESTRICT）
--   CHECK ck_srs_kind_columns を張り替え:
--     PERSON の行では slash_voice_person_alias_id を NULL に限定する
--     （CHARACTER_WITH_CV の行でだけ使える）。
--
-- 冪等性: 列・索引・外部キー・CHECK の状態を INFORMATION_SCHEMA で確かめてから追加 / 張り替えする。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_srs_slash_voice;
DELIMITER $$
CREATE PROCEDURE _v1191_add_srs_slash_voice()
BEGIN
  -- 列
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'song_recording_singers'
       AND COLUMN_NAME  = 'slash_voice_person_alias_id'
  ) THEN
    ALTER TABLE `song_recording_singers`
      ADD COLUMN `slash_voice_person_alias_id` int DEFAULT NULL
        COMMENT 'CHARACTER_WITH_CV の行の 2 人目の声優の名義（→ person_aliases）。PERSON の行では NULL'
        AFTER `slash_character_alias_id`;
  END IF;

  -- 索引
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'song_recording_singers'
       AND INDEX_NAME   = 'ix_srs_slash_voice'
  ) THEN
    ALTER TABLE `song_recording_singers`
      ADD KEY `ix_srs_slash_voice` (`slash_voice_person_alias_id`);
  END IF;

  -- 外部キー
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA    = DATABASE()
       AND TABLE_NAME      = 'song_recording_singers'
       AND CONSTRAINT_NAME = 'fk_srs_slash_voice'
  ) THEN
    ALTER TABLE `song_recording_singers`
      ADD CONSTRAINT `fk_srs_slash_voice` FOREIGN KEY (`slash_voice_person_alias_id`)
        REFERENCES `person_aliases` (`alias_id`)
        ON DELETE RESTRICT ON UPDATE NO ACTION;
  END IF;

  -- CHECK の張り替え：定義に slash_voice_person_alias_id を含まない旧版なら DROP → ADD する。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
     WHERE CONSTRAINT_SCHEMA = DATABASE()
       AND CONSTRAINT_NAME   = 'ck_srs_kind_columns'
       AND CHECK_CLAUSE LIKE '%slash_voice_person_alias_id%'
  ) THEN
    ALTER TABLE `song_recording_singers`
      DROP CHECK `ck_srs_kind_columns`,
      ADD CONSTRAINT `ck_srs_kind_columns` CHECK (
           (`billing_kind` = 'PERSON'
              AND `person_alias_id`       IS NOT NULL
              AND `character_alias_id`    IS NULL
              AND `voice_person_alias_id` IS NULL
              AND `slash_character_alias_id` IS NULL
              AND `slash_voice_person_alias_id` IS NULL)
        OR (`billing_kind` = 'CHARACTER_WITH_CV'
              AND `character_alias_id`    IS NOT NULL
              AND `voice_person_alias_id` IS NOT NULL
              AND `person_alias_id`       IS NULL
              AND `slash_person_alias_id` IS NULL)
      );
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_srs_slash_voice();
DROP PROCEDURE IF EXISTS _v1191_add_srs_slash_voice;

SELECT 'v1.20.0 migration completed: song_recording_singers.slash_voice_person_alias_id' AS final_status;
