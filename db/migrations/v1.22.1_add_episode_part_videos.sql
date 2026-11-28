-- =====================================================================
-- v1.22.1_add_episode_part_videos.sql
--
-- 各話のパート（episode_parts の行）に、YouTube の動画を結び付ける表を新設する。
-- 毎話のコーナー（「プリキュア星座占い」など）を公式チャンネルが動画で上げているものを、エピソード詳細の
-- 特別予告の下に、パートの添え書き（supplement）を見出しにして出すために使う。
-- 1 つのパートに 2 本以上を持てる（本放送のものと別の ver.。video_seq の順で並べる）。
--   episode_part_videos
--     episode_id・episode_seq  動画を結ぶパート（episode_parts の主キー。パートを消す・番号を振り直すと追従する）
--     video_seq                パートの中の動画の順（1＝本放送のもの、2 からは別の ver.）
--     video_id                 YouTube の動画 ID（同じ動画を 2 つのパートに結ばない）
--     channel・title_text・published_at・duration_seconds  投稿したチャンネル・題・公開の時刻（日本時間）・長さ（秒）
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `episode_part_videos` (
  `episode_id`       int NOT NULL,
  `episode_seq`      tinyint unsigned NOT NULL COMMENT 'episode_parts の行（パートの順）',
  `video_seq`        tinyint unsigned NOT NULL DEFAULT '1' COMMENT 'パートの中の動画の順（1＝本放送のもの、2 からは別の ver.）',
  `video_id`         varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT 'YouTube の動画 ID',
  `channel`          enum('PETITCURE','PRECURE_OFFICIAL','TOEI_ANIMATION') NOT NULL COMMENT '投稿したチャンネル',
  `title_text`       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT 'YouTube の題（そのまま）',
  `published_at`     datetime DEFAULT NULL COMMENT '公開の時刻（日本時間）',
  `duration_seconds` int unsigned DEFAULT NULL COMMENT '長さ（秒）',
  `notes`            text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64) DEFAULT NULL,
  `updated_by`       varchar(64) DEFAULT NULL,
  `is_deleted`       tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`episode_id`,`episode_seq`,`video_seq`),
  UNIQUE KEY `uq_episode_part_videos_video` (`video_id`),
  CONSTRAINT `fk_episode_part_videos_part` FOREIGN KEY (`episode_id`, `episode_seq`) REFERENCES `episode_parts` (`episode_id`, `episode_seq`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_episode_part_videos_seq` CHECK ((`video_seq` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
