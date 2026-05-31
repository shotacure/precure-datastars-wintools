-- =====================================================================
-- v1.4.2_add_series_theme_songs.sql
--
-- series_theme_songs テーブルを新設する。
-- 映画系列（series_kinds.credit_attach_to='SERIES'）の主題歌・挿入歌を
-- シリーズ単位で持つための専用テーブル。構造は episode_theme_songs のミラーで、
-- episode_id を series_id に置き換えただけ。
--
-- 用途：
--   - 映画クレジットの「主題歌」「OP 主題歌」「挿入歌」ブロックを役職テンプレ DSL から
--     auto-expand する際の引き当て元
--   - エピソード単位ではなくシリーズ全体に紐付く意味論
--
-- usage_actuality（NORMAL / BROADCAST_NOT_CREDITED / CREDITED_NOT_BROADCAST）は
-- episode_theme_songs と同義（本編で流れたか / クレジット記載があるかの 3 区分）。
--
-- 冪等性：CREATE TABLE IF NOT EXISTS で重複適用に耐える。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `series_theme_songs` (
  `series_id`               int                                                  NOT NULL,
  `is_broadcast_only`       tinyint(1)                                           NOT NULL DEFAULT 0,
  `theme_kind`              enum('OP','ED','INSERT')                             NOT NULL,
  `seq`                     tinyint unsigned                                     NOT NULL DEFAULT '0',
  `usage_actuality`         enum('NORMAL','BROADCAST_NOT_CREDITED','CREDITED_NOT_BROADCAST') NOT NULL DEFAULT 'NORMAL',
  `song_recording_id`       int                                                  NOT NULL,
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`series_id`,`is_broadcast_only`,`theme_kind`,`seq`),
  KEY `ix_sts_song_recording` (`song_recording_id`),
  CONSTRAINT `fk_sts_series`         FOREIGN KEY (`series_id`)         REFERENCES `series`          (`series_id`)         ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_sts_song_recording` FOREIGN KEY (`song_recording_id`) REFERENCES `song_recordings` (`song_recording_id`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

COMMIT;
