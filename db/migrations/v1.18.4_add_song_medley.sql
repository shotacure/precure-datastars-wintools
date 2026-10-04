-- =====================================================================
-- v1.18.4_add_song_medley.sql
--
-- メドレーの曲を持てるようにする。
--   1. 役職 MEDLEY_ARRANGEMENT「メドレー編曲」（曲のクレジット song_credits の作家の役職。編曲とは別に集計する）
--   2. song_medley_parts：メドレーの中の曲を順序付きで持つ対応表（1 行 = メドレーの中の 1 曲）
--        medley_song_id       メドレーの曲（songs.song_id）
--        part_seq             メドレーの中で何曲目か（1 始まり。同じ原曲が何度出てもよい）
--        source_song_id       原曲（songs.song_id）。版違いは表記どおりの版の曲を指す。原曲が DB に無いときは NULL
--      原曲の曲名・作詞・作曲・編曲は原曲（songs / song_credits）から引く。
--
-- 冪等性: 役職は INSERT IGNORE、表は CREATE TABLE IF NOT EXISTS。
-- =====================================================================

INSERT IGNORE INTO roles (role_code, name_ja, name_en, role_format_kind, music_credit_group, display_order, created_by, updated_by)
VALUES ('MEDLEY_ARRANGEMENT', 'メドレー編曲', 'Medley Arrangement', 'NORMAL', 'WRITING', 921, 'migration', 'migration');

CREATE TABLE IF NOT EXISTS `song_medley_parts` (
  `medley_song_id`      int              NOT NULL,
  `part_seq`            tinyint unsigned NOT NULL,
  `source_song_id`      int              DEFAULT NULL,
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`medley_song_id`, `part_seq`),
  KEY `ix_song_medley_parts_source` (`source_song_id`),
  CONSTRAINT `ck_song_medley_parts_seq_pos` CHECK (`part_seq` >= 1),
  CONSTRAINT `fk_song_medley_parts_medley` FOREIGN KEY (`medley_song_id`) REFERENCES `songs` (`song_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_song_medley_parts_source` FOREIGN KEY (`source_song_id`) REFERENCES `songs` (`song_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
