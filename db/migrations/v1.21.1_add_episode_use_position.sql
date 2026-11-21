-- =====================================================================
-- v1.21.1_add_episode_use_position.sql
--
-- 話の中の使用曲の記録（episode_uses）に、パートの中での位置とミリ秒の尺を持てるようにし、
-- 話ごとに「使用曲の記録をサイトに出すか」の印（episodes.is_uses_published）を足す。
--   episode_uses.part_offset_ms   INT UNSIGNED NULL   パートの頭から使用が始まるまでの位置（ミリ秒）
--   episode_uses.duration_ms      INT UNSIGNED NULL   使用尺（ミリ秒）。duration_seconds は秒の値のまま残す
--   episodes.is_uses_published    TINYINT NOT NULL DEFAULT 0   1 の回だけ、使用曲の記録をサイトに出す
--
-- 使用曲の記録は、確かめ終える前の段階でも入れておける。サイトは印の付いた回だけを読むので、
-- 印の無い回の行は、あっても出ない。既存の回は 0（出さない）で始まる。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1211_add_episode_use_position;
DELIMITER $$
CREATE PROCEDURE _v1211_add_episode_use_position()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_uses' AND COLUMN_NAME = 'part_offset_ms'
  ) THEN
    ALTER TABLE `episode_uses`
      ADD COLUMN `part_offset_ms` int unsigned DEFAULT NULL
        COMMENT 'パートの頭から使用が始まるまでの位置（ミリ秒）。分からなければ NULL'
        AFTER `duration_seconds`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_uses' AND COLUMN_NAME = 'duration_ms'
  ) THEN
    ALTER TABLE `episode_uses`
      ADD COLUMN `duration_ms` int unsigned DEFAULT NULL
        COMMENT '使用尺（ミリ秒）。分からなければ NULL。duration_seconds は秒の値'
        AFTER `part_offset_ms`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episodes' AND COLUMN_NAME = 'is_uses_published'
  ) THEN
    ALTER TABLE `episodes`
      ADD COLUMN `is_uses_published` tinyint NOT NULL DEFAULT 0
        COMMENT '1=この回の使用曲の記録（episode_uses）をサイトに出す。0 の回は、行があっても出さない'
        AFTER `is_provisional_credit`;
  END IF;
END$$
DELIMITER ;

CALL _v1211_add_episode_use_position();
DROP PROCEDURE _v1211_add_episode_use_position;

SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE()
   AND ((TABLE_NAME = 'episode_uses' AND COLUMN_NAME IN ('part_offset_ms', 'duration_ms'))
     OR (TABLE_NAME = 'episodes' AND COLUMN_NAME = 'is_uses_published'));
