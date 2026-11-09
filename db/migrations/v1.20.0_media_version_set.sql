-- =====================================================================
-- v1.20.0_media_version_set.sql
--
-- クレジット（credits）とカード（credit_cards）の版 media_version を、そのクレジット・カードが出る媒体の組にする。
--   ENUM('BROADCAST','HOME_VIDEO') → SET('BROADCAST','BLURAY','STREAMING')
--   BROADCAST = 本放送、BLURAY = Blu-ray、STREAMING = 配信。NULL = 共通（通常）のまま。
--   これまでの HOME_VIDEO（Blu-ray・配信）は 'BLURAY,STREAMING' に移す。
--
-- 本放送だけ違う回（本放送 / Blu-ray・配信）に加えて、Blu-ray が本放送と同じで配信だけ違う回
-- （'BROADCAST,BLURAY' / 'STREAMING'）を持てるようにする。サイトは 2 つの版のカードの 1 組ごとに枠を作り、
-- 組の前の版を上・後ろの版を下に、版の表示名（「本放送」「Blu-ray・配信」「本放送・Blu-ray」「配信」など）を見出しにして描く。
-- 集計は組の後ろの版が本体（前の版のカードは、後ろの版のカードにいない人だけを数える）。
--
-- credits の一意制約に使う生成列 media_version_key は、組の値（最長 'BROADCAST,BLURAY,STREAMING'）が入るよう varchar(32) にする。
-- 生成列が media_version を参照しているので、索引と生成列をいったん外してから型を変え、付け直す。
--
-- 冪等性: 列の型（INFORMATION_SCHEMA.COLUMNS.DATA_TYPE）が enum のときだけ変える。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_media_version_set;
DELIMITER $$
CREATE PROCEDURE _v1200_media_version_set()
BEGIN
  -- カード
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_cards' AND COLUMN_NAME = 'media_version' AND DATA_TYPE = 'enum'
  ) THEN
    ALTER TABLE `credit_cards` MODIFY COLUMN `media_version` varchar(32) DEFAULT NULL;
    UPDATE `credit_cards` SET `media_version` = 'BLURAY,STREAMING' WHERE `media_version` = 'HOME_VIDEO';
    ALTER TABLE `credit_cards`
      MODIFY COLUMN `media_version` set('BROADCAST','BLURAY','STREAMING') DEFAULT NULL
        COMMENT 'カードの版（そのカードが出る媒体の組。BROADCAST=本放送、BLURAY=Blu-ray、STREAMING=配信、NULL=共通）';
  END IF;

  -- クレジット
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits' AND COLUMN_NAME = 'media_version' AND DATA_TYPE = 'enum'
  ) THEN
    ALTER TABLE `credits` DROP INDEX `uq_credit_episode_kind_version`, DROP INDEX `uq_credit_series_kind_version`;
    ALTER TABLE `credits` DROP COLUMN `media_version_key`;
    ALTER TABLE `credits` MODIFY COLUMN `media_version` varchar(32) DEFAULT NULL;
    UPDATE `credits` SET `media_version` = 'BLURAY,STREAMING' WHERE `media_version` = 'HOME_VIDEO';
    ALTER TABLE `credits`
      MODIFY COLUMN `media_version` set('BROADCAST','BLURAY','STREAMING') DEFAULT NULL
        COMMENT '版（そのクレジットが出る媒体の組。BROADCAST=本放送、BLURAY=Blu-ray、STREAMING=配信、NULL=共通）';
    ALTER TABLE `credits`
      ADD COLUMN `media_version_key` varchar(32) GENERATED ALWAYS AS (IFNULL(`media_version`, '')) STORED
        COMMENT '一意制約用（media_version の NULL を空文字に）'
        AFTER `media_version`;
    ALTER TABLE `credits`
      ADD UNIQUE KEY `uq_credit_series_kind_version`  (`series_id`, `credit_kind`, `media_version_key`),
      ADD UNIQUE KEY `uq_credit_episode_kind_version` (`episode_id`, `credit_kind`, `media_version_key`);
  END IF;
END$$
DELIMITER ;

CALL _v1200_media_version_set();
DROP PROCEDURE IF EXISTS _v1200_media_version_set;
