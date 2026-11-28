-- =====================================================================
-- v1.22.1_add_episode_part_videos_heading.sql
--
-- パートに結び付けた YouTube の動画（episode_part_videos）に、見出しと添え書きの列を足す。
-- 本放送ではコーナーが別の告知に差し替わった回の動画を、その告知のパートに結び付けて、
-- コーナーの名前の見出しと「本放送では〜が流れました」の 1 行を添えて出すために使う。
--   episode_part_videos
--     heading_text  見出し（空ならパートの添え書き）
--     note_text     動画の下に添える 1 行（空なら添えない）
--
-- 冪等性: INFORMATION_SCHEMA.COLUMNS で列の有無を見てから ALTER する。行の中身は変えない。
-- =====================================================================

SET @col_exists := (
  SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'episode_part_videos'
     AND COLUMN_NAME  = 'heading_text');

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `episode_part_videos` ADD COLUMN `heading_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT ''見出し（空ならパートの添え書き）'' AFTER `title_text`',
  'SELECT ''episode_part_videos.heading_text は既にあります'' AS note');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @col_exists := (
  SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'episode_part_videos'
     AND COLUMN_NAME  = 'note_text');

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `episode_part_videos` ADD COLUMN `note_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT ''動画の下に添える 1 行（空なら添えない）'' AFTER `heading_text`',
  'SELECT ''episode_part_videos.note_text は既にあります'' AS note');

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
