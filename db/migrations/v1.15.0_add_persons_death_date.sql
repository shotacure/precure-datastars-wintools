-- =====================================================================
-- v1.15.0_add_persons_death_date.sql
--
-- 人物の没年月日を持つ列を persons に追加する。
--   death_year   smallint unsigned NULL  没年（西暦）。存命・不明は NULL
--   death_month  tinyint unsigned  NULL  没月（1-12）。年だけ分かっている人は NULL
--   death_day    tinyint unsigned  NULL  没日（1-31）。年月だけ分かっている人は NULL
--
-- 誕生日（birth_year / birth_month / birth_day）と同じく年・月・日を別の列で持ち、
-- 「2016年」「2016年10月」のように一部だけ分かっている没年月日も表せるようにする。
-- 没年がある人物は、サイトの人物詳細に没年月日を出し、トップの「今日の記念日」で誕生日に年齢を添えない。
--
-- 制約:
--   ck_persons_death_month           : NULL もしくは 1-12
--   ck_persons_death_day             : NULL もしくは 1-31
--   ck_persons_death_day_needs_month : 日があるなら月必須
--   ck_persons_death_month_needs_year: 月があるなら年必須
--
-- 冪等性: 列・制約ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1150_add_persons_death_date;
DELIMITER $$
CREATE PROCEDURE _v1150_add_persons_death_date()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'death_year'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `death_year` smallint unsigned DEFAULT NULL
        COMMENT '没年（西暦）。存命・不明は NULL'
        AFTER `birth_day`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'death_month'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `death_month` tinyint unsigned DEFAULT NULL
        COMMENT '没月（1-12）'
        AFTER `death_year`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'death_day'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `death_day` tinyint unsigned DEFAULT NULL
        COMMENT '没日（1-31）'
        AFTER `death_month`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
       AND CONSTRAINT_NAME = 'ck_persons_death_month' AND CONSTRAINT_TYPE = 'CHECK'
  ) THEN
    ALTER TABLE `persons`
      ADD CONSTRAINT `ck_persons_death_month` CHECK (`death_month` IS NULL OR (`death_month` BETWEEN 1 AND 12));
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
       AND CONSTRAINT_NAME = 'ck_persons_death_day' AND CONSTRAINT_TYPE = 'CHECK'
  ) THEN
    ALTER TABLE `persons`
      ADD CONSTRAINT `ck_persons_death_day` CHECK (`death_day` IS NULL OR (`death_day` BETWEEN 1 AND 31));
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
       AND CONSTRAINT_NAME = 'ck_persons_death_day_needs_month' AND CONSTRAINT_TYPE = 'CHECK'
  ) THEN
    ALTER TABLE `persons`
      ADD CONSTRAINT `ck_persons_death_day_needs_month` CHECK (`death_day` IS NULL OR `death_month` IS NOT NULL);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
       AND CONSTRAINT_NAME = 'ck_persons_death_month_needs_year' AND CONSTRAINT_TYPE = 'CHECK'
  ) THEN
    ALTER TABLE `persons`
      ADD CONSTRAINT `ck_persons_death_month_needs_year` CHECK (`death_month` IS NULL OR `death_year` IS NOT NULL);
  END IF;
END$$
DELIMITER ;

CALL _v1150_add_persons_death_date();
DROP PROCEDURE IF EXISTS _v1150_add_persons_death_date;
