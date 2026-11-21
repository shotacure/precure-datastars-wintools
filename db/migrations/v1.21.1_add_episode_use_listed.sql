-- =====================================================================
-- v1.21.1_add_episode_use_listed.sql
--
-- 話の中の使用曲の記録（episode_uses）に、その行を使用曲の一覧に載せるかの印を足す。
--   episode_uses.is_listed   TINYINT NOT NULL DEFAULT 1   1=一覧に載せる行、0=記録だけして一覧には載せない行
--
-- 本編のパート（アバン・A パート・B パート・C パート・バトンタッチ・コーナー）の行は載せる。
-- OP・ED・予告などのパートの行は、記録はしておき、載せるかどうかを行ごとに選ぶ。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1211_add_episode_use_listed;
DELIMITER $$
CREATE PROCEDURE _v1211_add_episode_use_listed()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_uses' AND COLUMN_NAME = 'is_listed'
  ) THEN
    ALTER TABLE `episode_uses`
      ADD COLUMN `is_listed` tinyint NOT NULL DEFAULT 1
        COMMENT '1=使用曲の一覧に載せる行、0=記録だけして一覧には載せない行（OP・ED・予告などのパートの行は行ごとに選ぶ）'
        AFTER `is_broadcast_only`;
  END IF;
END$$
DELIMITER ;

CALL _v1211_add_episode_use_listed();
DROP PROCEDURE _v1211_add_episode_use_listed;

SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_uses' AND COLUMN_NAME = 'is_listed';
