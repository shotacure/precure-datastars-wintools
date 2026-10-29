-- =====================================================================
-- v1.20.0_series_start_date_precision.sql
--
-- 作品の開始日がどこまで確かかを持たせる。放送・公開の時期が「2027 年」「2027 年 7 月」までしか
-- 発表されていない作品も、start_date（NOT NULL）に仮の日付を入れたうえで、サイトでは発表どおりの
-- 細かさで「2027年放送予定」「2027年7月公開予定」のように出せるようにする。
--   start_date_precision  ENUM NOT NULL DEFAULT 'DAY'
--     DAY    start_date は日付まで確か（これまでの作品はすべてこれ）
--     MONTH  年と月だけが確か（日は仮）
--     YEAR   年だけが確か（月日は仮）
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_series_start_date_precision;
DELIMITER $$
CREATE PROCEDURE _v1191_series_start_date_precision()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series' AND COLUMN_NAME = 'start_date_precision'
  ) THEN
    ALTER TABLE `series`
      ADD COLUMN `start_date_precision` enum('DAY','MONTH','YEAR') NOT NULL DEFAULT 'DAY'
        COMMENT '開始日の確かさ（DAY=日付まで / MONTH=年月まで、日は仮 / YEAR=年まで、月日は仮）'
        AFTER `start_date`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_series_start_date_precision();
DROP PROCEDURE IF EXISTS _v1191_series_start_date_precision;
