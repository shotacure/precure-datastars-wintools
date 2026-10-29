-- =====================================================================
-- v1.19.1_add_bgm_cue_source_parts.sql
--
-- 劇伴の曲が使っている曲（歌・曲（著作物））を持てるようにする。
--   1. musical_works：曲（著作物）。歌でも劇伴でもない、JASRAC に登録された曲だけを入れる
--      （例：メドレーの劇伴の中にだけ出てくる曲で、どの盤にも単独では入っていないもの）。
--      歌は songs、劇伴は bgm_cues で持ち、ここには入れない。
--        work_id            曲の ID（AUTO_INCREMENT）
--        title / title_kana 曲名とその読み
--        jasrac_work_code   JASRAC の作品コード（例 242-0576-1）。重複不可、NULL 可
--        iswc               ISWC（例 T-927.548.991-3）。重複不可、NULL 可
--   2. musical_work_credits：曲（著作物）の作家の連名。列は bgm_cue_credits と同じ流儀で、
--      役職は song_credits と同じ役職コード（LYRICS / COMPOSITION / ARRANGEMENT など）。
--   3. bgm_cue_source_parts：劇伴の曲（bgm_cues）が使っている曲を、曲の中で流れる順に持つ対応表
--      （1 行 = 使われている 1 曲）。source_song_id（歌）と source_work_id（曲（著作物））のどちらか一方だけを入れる。
--      M 番号の付け替え・劇伴の曲の削除には bgm_cue_credits と同じく CASCADE で付いていく。
--      使われている歌・曲（著作物）は、ここから参照されている間は消せない（RESTRICT。
--      MySQL では CHECK に使う列の外部キーに CASCADE / SET NULL を付けられないため）。
--
-- 冪等性: 表は CREATE TABLE IF NOT EXISTS。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `musical_works` (
  `work_id`             int              NOT NULL AUTO_INCREMENT,
  `title`               varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_kana`          varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `jasrac_work_code`    varchar(16)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL COMMENT 'JASRAC の作品コード（例 242-0576-1）',
  `iswc`                varchar(20)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL COMMENT 'ISWC（例 T-927.548.991-3）',
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  `is_deleted`          tinyint          NOT NULL DEFAULT '0',
  PRIMARY KEY (`work_id`),
  UNIQUE KEY `uq_musical_works_jasrac` (`jasrac_work_code`),
  UNIQUE KEY `uq_musical_works_iswc` (`iswc`),
  KEY `ix_musical_works_title` (`title`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `musical_work_credits` (
  `work_id`             int              NOT NULL,
  `credit_role`         varchar(64)      NOT NULL,
  `credit_seq`          tinyint unsigned NOT NULL,
  `person_alias_id`     int              NOT NULL,
  `preceding_separator` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`work_id`, `credit_role`, `credit_seq`),
  KEY `ix_musical_work_credits_alias` (`person_alias_id`),
  KEY `ix_musical_work_credits_role` (`credit_role`),
  CONSTRAINT `ck_musical_work_credits_seq_pos` CHECK (`credit_seq` >= 1),
  CONSTRAINT `fk_musical_work_credits_work`  FOREIGN KEY (`work_id`)         REFERENCES `musical_works`  (`work_id`)   ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_musical_work_credits_alias` FOREIGN KEY (`person_alias_id`) REFERENCES `person_aliases` (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_musical_work_credits_role`  FOREIGN KEY (`credit_role`)     REFERENCES `roles`          (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `bgm_cue_source_parts` (
  `series_id`           int              NOT NULL,
  `m_no_detail`         varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `part_seq`            tinyint unsigned NOT NULL,
  `source_song_id`      int              DEFAULT NULL,
  `source_work_id`      int              DEFAULT NULL,
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`series_id`, `m_no_detail`, `part_seq`),
  KEY `ix_bgm_cue_source_parts_song` (`source_song_id`),
  KEY `ix_bgm_cue_source_parts_work` (`source_work_id`),
  CONSTRAINT `ck_bgm_cue_source_parts_seq_pos` CHECK (`part_seq` >= 1),
  CONSTRAINT `ck_bgm_cue_source_parts_one_source` CHECK (
       (`source_song_id` IS NOT NULL AND `source_work_id` IS NULL)
    OR (`source_song_id` IS NULL     AND `source_work_id` IS NOT NULL)
  ),
  CONSTRAINT `fk_bgm_cue_source_parts_cue`  FOREIGN KEY (`series_id`, `m_no_detail`) REFERENCES `bgm_cues`      (`series_id`, `m_no_detail`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_bgm_cue_source_parts_song` FOREIGN KEY (`source_song_id`)           REFERENCES `songs`         (`song_id`)                  ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_bgm_cue_source_parts_work` FOREIGN KEY (`source_work_id`)           REFERENCES `musical_works` (`work_id`)                  ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
