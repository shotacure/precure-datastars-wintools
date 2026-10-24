-- =====================================================================
-- v1.19.1_add_bgm_cue_singers.sql
--
-- 劇伴の曲に歌い手（スキャットなど）を持てるようにする。
--   1. 役職 SCAT「スキャット」（歌い手の役職。歌の録音の歌唱者・劇伴の歌い手で使う）
--   2. bgm_cue_singers：劇伴の曲（bgm_cues）の歌い手の連名。列は song_recording_singers と同じ
--        billing_kind = PERSON           ：person_alias_id（ユニット名義なら expand_unit_members でメンバーを展開）
--        billing_kind = CHARACTER_WITH_CV：character_alias_id ＋ voice_person_alias_id（キャラ(CV:声優)）
--      M 番号の付け替え・劇伴の曲の削除には bgm_cue_credits と同じく CASCADE で付いていく。
--
-- 冪等性: 役職は INSERT IGNORE、表は CREATE TABLE IF NOT EXISTS。
-- =====================================================================

INSERT IGNORE INTO roles (role_code, name_ja, name_en, role_format_kind, music_credit_group, display_order, created_by, updated_by)
VALUES ('SCAT', 'スキャット', 'Scat', 'NORMAL', 'PERFORMANCE', 934, 'migration', 'migration');

CREATE TABLE IF NOT EXISTS `bgm_cue_singers` (
  `series_id`                 int              NOT NULL,
  `m_no_detail`               varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `role_code`                 varchar(64)      NOT NULL DEFAULT 'VOCALS',
  `singer_seq`                tinyint unsigned NOT NULL,
  `billing_kind`              enum('PERSON','CHARACTER_WITH_CV') NOT NULL,
  `person_alias_id`           int              DEFAULT NULL,
  `character_alias_id`        int              DEFAULT NULL,
  `voice_person_alias_id`     int              DEFAULT NULL,
  `slash_person_alias_id`     int              DEFAULT NULL,
  `slash_character_alias_id`  int              DEFAULT NULL,
  `preceding_separator`       varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_text`          varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `expand_unit_members`       tinyint(1)       NOT NULL DEFAULT 0,
  `notes`                     text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                varchar(64)      DEFAULT NULL,
  `updated_by`                varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`series_id`, `m_no_detail`, `role_code`, `singer_seq`),
  KEY `ix_bcs_role`            (`role_code`),
  KEY `ix_bcs_person`          (`person_alias_id`),
  KEY `ix_bcs_character`       (`character_alias_id`),
  KEY `ix_bcs_voice`           (`voice_person_alias_id`),
  KEY `ix_bcs_slash_person`    (`slash_person_alias_id`),
  KEY `ix_bcs_slash_character` (`slash_character_alias_id`),
  CONSTRAINT `ck_bcs_seq_pos` CHECK (`singer_seq` >= 1),
  CONSTRAINT `ck_bcs_kind_columns` CHECK (
       (`billing_kind` = 'PERSON'
          AND `person_alias_id`       IS NOT NULL
          AND `character_alias_id`    IS NULL
          AND `voice_person_alias_id` IS NULL
          AND `slash_character_alias_id` IS NULL)
    OR (`billing_kind` = 'CHARACTER_WITH_CV'
          AND `character_alias_id`    IS NOT NULL
          AND `voice_person_alias_id` IS NOT NULL
          AND `person_alias_id`       IS NULL
          AND `slash_person_alias_id` IS NULL)
  ),
  CONSTRAINT `fk_bcs_cue`              FOREIGN KEY (`series_id`, `m_no_detail`)  REFERENCES `bgm_cues`          (`series_id`, `m_no_detail`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_bcs_role`             FOREIGN KEY (`role_code`)                REFERENCES `roles`             (`role_code`)         ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_bcs_person`           FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_bcs_character`        FOREIGN KEY (`character_alias_id`)       REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_bcs_voice`            FOREIGN KEY (`voice_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_bcs_slash_person`     FOREIGN KEY (`slash_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_bcs_slash_character`  FOREIGN KEY (`slash_character_alias_id`) REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
