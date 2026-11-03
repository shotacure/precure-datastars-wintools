-- =====================================================================
-- v1.20.0_add_credit_media_version.sql
--
-- クレジット本体（credits）に版を持てるようにする。
--   media_version      ENUM('BROADCAST','HOME_VIDEO') NULL
--                      BROADCAST = 本放送、HOME_VIDEO = Blu-ray・配信、NULL = 共通（通常）。
--   media_version_key  VARCHAR(16) 生成列（media_version の NULL を空文字にしたもの）。一意制約に使う。
--
-- 本放送と Blu-ray・配信でクレジットのフォーマットが丸ごと違う作品（オトナシリーズ）は、
-- 同じ回・同じ種類（OP/ED）のクレジットを版ごとに 2 本並べて持つ。
-- 一意制約 (series_id, credit_kind)・(episode_id, credit_kind) を、版を含めた
-- (series_id, credit_kind, media_version_key)・(episode_id, credit_kind, media_version_key) に付け替える。
-- 生成列を使うのは、MySQL の UNIQUE が NULL どうしを衝突させず、共通（NULL）の OP が 2 本作れてしまうのを防ぐため。
-- SiteBuilder の集計は共通と Blu-ray・配信版だけで数え、本放送版は表示だけに使う。
-- 一部の差し替え（ロゴバージョン違い等）は従来どおりエントリ単位（credit_block_entries.is_broadcast_only）で持つ。
--
-- 冪等性: 列・索引ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_credit_media_version;
DELIMITER $$
CREATE PROCEDURE _v1200_add_credit_media_version()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND COLUMN_NAME = 'media_version'
  ) THEN
    ALTER TABLE `credits`
      ADD COLUMN `media_version` enum('BROADCAST','HOME_VIDEO') DEFAULT NULL
        COMMENT '版（BROADCAST=本放送、HOME_VIDEO=Blu-ray・配信、NULL=共通）'
        AFTER `credit_kind`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND COLUMN_NAME = 'media_version_key'
  ) THEN
    ALTER TABLE `credits`
      ADD COLUMN `media_version_key` varchar(16) GENERATED ALWAYS AS (IFNULL(`media_version`, '')) STORED
        COMMENT '一意制約用（media_version の NULL を空文字に）'
        AFTER `media_version`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND INDEX_NAME = 'uq_credit_episode_kind_version'
  ) THEN
    ALTER TABLE `credits`
      ADD UNIQUE KEY `uq_credit_episode_kind_version` (`episode_id`, `credit_kind`, `media_version_key`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND INDEX_NAME = 'uq_credit_series_kind_version'
  ) THEN
    ALTER TABLE `credits`
      ADD UNIQUE KEY `uq_credit_series_kind_version` (`series_id`, `credit_kind`, `media_version_key`);
  END IF;

  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND INDEX_NAME = 'uq_credit_episode_kind'
  ) THEN
    ALTER TABLE `credits` DROP INDEX `uq_credit_episode_kind`;
  END IF;

  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND INDEX_NAME = 'uq_credit_series_kind'
  ) THEN
    ALTER TABLE `credits` DROP INDEX `uq_credit_series_kind`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_credit_media_version();
DROP PROCEDURE IF EXISTS _v1200_add_credit_media_version;
