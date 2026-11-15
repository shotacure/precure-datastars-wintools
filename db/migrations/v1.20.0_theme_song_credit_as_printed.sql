-- =====================================================================
-- v1.20.0_theme_song_credit_as_printed.sql
--
-- TV のクレジットの主題歌のカードを、画面の文字どおりに描けるようにする。
--
-- 1. role_templates に episode_id を足す（エピソード別のテンプレ）。
--    同じシリーズの中で、回によって画面の書式（項目の見出しなど）が違うときに、その回だけのテンプレを持つ。
--    解決は (role_code, episode_id) → (role_code, series_id) → (role_code, NULL) の順。
--    episode_id を持つ行の series_id は、そのエピソードのシリーズを入れる（一覧で探しやすくするため）。
--    UNIQUE を (role_code, series_id) から (role_code, series_id, episode_id) に広げる。
--
-- 2. song_recording_singers に cv_label を足す（声優の前に置く記号）。
--    「キャラ(CV:声優)」の "CV:" を、初出盤の表記どおりに持つ（例: "CV."）。NULL なら "CV:"。
--
-- 3. episode_theme_song_singers を作る（エピソードのクレジットに出た歌唱者）。
--    主題歌のカードの歌唱者が、使った録音の歌唱者（song_recording_singers）と違うときに、
--    画面に出た歌唱者を名義でつないで持つ。行の形は song_recording_singers と同じで、
--    episode_theme_songs の 1 行（episode_id, is_broadcast_only, theme_kind, seq）に連なる。
--    行がある主題歌は、クレジットのカードの歌（VOCALS）・コーラス（BACKING_VOCALS）をこちらで描く
--    （主題歌・挿入歌の一覧など、録音の情報として出す所は録音の歌唱者のまま）。
--
-- 冪等性: INFORMATION_SCHEMA で列・索引・表の有無を確かめてから ALTER / CREATE を発行する。
-- DDL なのでトランザクションには入れない。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_theme_song_credit_as_printed;
DELIMITER $$
CREATE PROCEDURE _v1200_theme_song_credit_as_printed()
BEGIN
  -- 1. role_templates.episode_id
  IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
       WHERE TABLE_SCHEMA = DATABASE()
         AND TABLE_NAME   = 'role_templates'
         AND COLUMN_NAME  = 'episode_id') = 0 THEN
    ALTER TABLE `role_templates`
      ADD COLUMN `episode_id` int NULL
        COMMENT 'エピソード別のテンプレのエピソード（NULL=シリーズ別または既定）。series_id はそのエピソードのシリーズ'
        AFTER `series_id`,
      ADD KEY `ix_role_templates_episode` (`episode_id`),
      ADD CONSTRAINT `fk_role_templates_episode` FOREIGN KEY (`episode_id`) REFERENCES `episodes` (`episode_id`) ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;
  IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
       WHERE TABLE_SCHEMA = DATABASE()
         AND TABLE_NAME   = 'role_templates'
         AND INDEX_NAME   = 'uk_role_templates_role_series') > 0 THEN
    ALTER TABLE `role_templates`
      DROP INDEX `uk_role_templates_role_series`,
      ADD UNIQUE KEY `uk_role_templates_role_series_episode` (`role_code`, `series_id`, `episode_id`);
  END IF;

  -- 2. song_recording_singers.cv_label
  IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
       WHERE TABLE_SCHEMA = DATABASE()
         AND TABLE_NAME   = 'song_recording_singers'
         AND COLUMN_NAME  = 'cv_label') = 0 THEN
    ALTER TABLE `song_recording_singers`
      ADD COLUMN `cv_label` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'CHARACTER_WITH_CV 行の声優の前に置く記号（初出盤の表記、例: CV.）。NULL なら CV:'
        AFTER `affiliation_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_theme_song_credit_as_printed();

DROP PROCEDURE IF EXISTS _v1200_theme_song_credit_as_printed;

-- 3. episode_theme_song_singers
CREATE TABLE IF NOT EXISTS `episode_theme_song_singers` (
  `episode_id`                int              NOT NULL,
  `is_broadcast_only`         tinyint(1)       NOT NULL DEFAULT 0,
  `theme_kind`                enum('OP','ED','INSERT') NOT NULL,
  `seq`                       tinyint unsigned NOT NULL,
  `role_code`                 varchar(64)      NOT NULL DEFAULT 'VOCALS',
  `singer_seq`                tinyint unsigned NOT NULL,
  `billing_kind`              enum('PERSON','CHARACTER_WITH_CV') NOT NULL,
  `person_alias_id`           int              DEFAULT NULL,
  `character_alias_id`        int              DEFAULT NULL,
  `voice_person_alias_id`     int              DEFAULT NULL,
  `slash_person_alias_id`     int              DEFAULT NULL,
  `slash_character_alias_id`  int              DEFAULT NULL,
  `slash_voice_person_alias_id` int            DEFAULT NULL,
  `preceding_separator`       varchar(8)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_text`          varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `cv_label`                  varchar(8)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`                     text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                varchar(64)      DEFAULT NULL,
  `updated_by`                varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`episode_id`, `is_broadcast_only`, `theme_kind`, `seq`, `role_code`, `singer_seq`),
  KEY `ix_etss_role`            (`role_code`),
  KEY `ix_etss_person`          (`person_alias_id`),
  KEY `ix_etss_character`       (`character_alias_id`),
  KEY `ix_etss_voice`           (`voice_person_alias_id`),
  KEY `ix_etss_slash_person`    (`slash_person_alias_id`),
  KEY `ix_etss_slash_character` (`slash_character_alias_id`),
  KEY `ix_etss_slash_voice`     (`slash_voice_person_alias_id`),
  CONSTRAINT `ck_etss_seq_pos` CHECK (`singer_seq` >= 1),
  CONSTRAINT `ck_etss_kind_columns` CHECK (
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
  ),
  CONSTRAINT `fk_etss_theme_song`      FOREIGN KEY (`episode_id`, `is_broadcast_only`, `theme_kind`, `seq`)
                                       REFERENCES `episode_theme_songs` (`episode_id`, `is_broadcast_only`, `theme_kind`, `seq`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_etss_role`            FOREIGN KEY (`role_code`)                REFERENCES `roles`             (`role_code`)         ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_etss_person`          FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_etss_character`       FOREIGN KEY (`character_alias_id`)       REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_etss_voice`           FOREIGN KEY (`voice_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_etss_slash_person`    FOREIGN KEY (`slash_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_etss_slash_character` FOREIGN KEY (`slash_character_alias_id`) REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_etss_slash_voice`     FOREIGN KEY (`slash_voice_person_alias_id`) REFERENCES `person_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
