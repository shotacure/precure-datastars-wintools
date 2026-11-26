-- =====================================================================
-- v1.21.1_add_series_uses.sql
--
-- 話を持たない作品（映画など）の中の使用曲の記録を、作品ごとに持つ表（series_uses）を足し、
-- 作品ごとに「使用曲の記録をサイトに出すか」の印（series.is_uses_published）を足す。
--   series_uses                  話ごとの episode_uses と同じ中身を、作品（series_id）と使用順で持つ。
--                                パートは無いので、位置は作品の本編の頭からのミリ秒（offset_ms）で持つ
--   series.is_uses_published     TINYINT NOT NULL DEFAULT 0   1 の作品だけ、使用曲の記録をサイトに出す
--
-- 使用曲の記録は、確かめ終える前の段階でも入れておける。既存の作品は 0（出さない）で始まる。
--
-- 冪等性: 表は CREATE TABLE IF NOT EXISTS、列は INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_uses` (
  `series_id`         int                NOT NULL                COMMENT '対象の作品（→ series.series_id）。話を持たない作品（映画など）',
  `use_order`         smallint unsigned  NOT NULL                COMMENT '作品の中の使用順（1 始まり。本編の頭からの位置の順）',
  `sub_order`         tinyint unsigned   NOT NULL DEFAULT 0      COMMENT '同 use_order 内のサブ順（メドレー時の連続曲の細分）',

  `content_kind_code` varchar(32)        CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'OTHER'
                                                                COMMENT '内容種別（→ track_content_kinds.kind_code、SONG/BGM/DRAMA/RADIO/JINGLE/OTHER）',

  `song_recording_id`      int           DEFAULT NULL            COMMENT 'SONG 時：→ song_recordings.song_recording_id',
  `song_size_variant_code` varchar(32)   CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'SONG 時：歌のサイズ違い（→ song_size_variants.variant_code）',
  `song_part_variant_code` varchar(32)   CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'SONG 時：歌のパート違い（→ song_part_variants.variant_code）',

  `bgm_series_id`     int                DEFAULT NULL            COMMENT 'BGM 時：→ bgm_cues.series_id（複合 FK 第 1 列）',
  `bgm_m_no_detail`   varchar(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'BGM 時：→ bgm_cues.m_no_detail（複合 FK 第 2 列）',

  `use_title_override` varchar(255)      CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
                                                                COMMENT '内容種別がテキスト系のときの表示文字列。歌・劇伴では使わない',

  `scene_label`       varchar(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
                                                                COMMENT '使用シーンの説明',
  `duration_seconds`  smallint unsigned  DEFAULT NULL            COMMENT '使用尺（秒）',
  `offset_ms`         int unsigned       DEFAULT NULL            COMMENT '作品の本編の頭から使用が始まるまでの位置（ミリ秒）。分からなければ NULL',
  `duration_ms`       int unsigned       DEFAULT NULL            COMMENT '使用尺（ミリ秒）。分からなければ NULL。duration_seconds は秒の値',
  `notes`             varchar(1024)      DEFAULT NULL            COMMENT '備考',
  `is_listed`         tinyint            NOT NULL DEFAULT 1      COMMENT '1=使用曲の一覧に載せる行、0=記録だけして一覧には載せない行',

  `created_at`        timestamp          NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp          NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`        varchar(64)        DEFAULT NULL,
  `updated_by`        varchar(64)        DEFAULT NULL,

  PRIMARY KEY (`series_id`, `use_order`, `sub_order`),
  KEY `ix_series_uses_content_kind` (`content_kind_code`),
  KEY `ix_series_uses_song_recording` (`song_recording_id`),
  KEY `ix_series_uses_song_size` (`song_size_variant_code`),
  KEY `ix_series_uses_song_part` (`song_part_variant_code`),
  KEY `ix_series_uses_bgm_ref` (`bgm_series_id`, `bgm_m_no_detail`),

  CONSTRAINT `fk_series_uses_series`
    FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_uses_content_kind`
    FOREIGN KEY (`content_kind_code`) REFERENCES `track_content_kinds` (`kind_code`),
  CONSTRAINT `fk_series_uses_song_recording`
    FOREIGN KEY (`song_recording_id`) REFERENCES `song_recordings` (`song_recording_id`)
    ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_series_uses_song_size`
    FOREIGN KEY (`song_size_variant_code`) REFERENCES `song_size_variants` (`variant_code`),
  CONSTRAINT `fk_series_uses_song_part`
    FOREIGN KEY (`song_part_variant_code`) REFERENCES `song_part_variants` (`variant_code`),
  CONSTRAINT `fk_series_uses_bgm_cue`
    FOREIGN KEY (`bgm_series_id`, `bgm_m_no_detail`) REFERENCES `bgm_cues` (`series_id`, `m_no_detail`)
    ON DELETE SET NULL ON UPDATE CASCADE,

  CONSTRAINT `ck_series_uses_use_order_pos`
    CHECK ((`use_order` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS _v1211_add_series_uses;
DELIMITER $$
CREATE PROCEDURE _v1211_add_series_uses()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'is_uses_published'
  ) THEN
    ALTER TABLE `series`
      ADD COLUMN `is_uses_published` tinyint NOT NULL DEFAULT 0
        COMMENT '1=この作品の使用曲の記録（series_uses）をサイトに出す。0 の作品は、行があっても出さない'
        AFTER `is_provisional_credit`;
  END IF;
END$$
DELIMITER ;

CALL _v1211_add_series_uses();
DROP PROCEDURE _v1211_add_series_uses;

SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE()
   AND (TABLE_NAME = 'series_uses' OR (TABLE_NAME = 'series' AND COLUMN_NAME = 'is_uses_published'))
 ORDER BY TABLE_NAME, ORDINAL_POSITION;
