-- =====================================================================
-- v1.20.0_add_episode_part_supplement.sql
--
-- エピソードのパートに「補足事項」の列を足す。
--   supplement  VARCHAR(255) NULL
--
-- 用途：パートの中身の添え書き（「OA時提供サイドスーパー」・コーナー名など）。サイトのフォーマットの表の
-- 「補足事項」の列に出す。備考（notes）はサイトに出さない欄とし、放送の記録の道具が書く「【本放送未確認】」の
-- 印だけを置く。
--
-- 列を作ったときに限り、それまで備考に入っていた添え書きを補足事項へ移して備考を空にする
-- （「【本放送未確認】」を含む備考は印なので移さない）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER と移し替えを発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_episode_part_supplement;
DELIMITER $$
CREATE PROCEDURE _v1200_add_episode_part_supplement()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_parts' AND COLUMN_NAME = 'supplement'
  ) THEN
    ALTER TABLE `episode_parts`
      ADD COLUMN `supplement` varchar(255) DEFAULT NULL
        COMMENT '補足事項。パートの中身の添え書き（提供サイドスーパー・コーナー名など）で、サイトのフォーマットの表に出す'
        AFTER `vod_length`;

    UPDATE `episode_parts`
       SET `supplement` = `notes`, `notes` = NULL
     WHERE `notes` IS NOT NULL AND `notes` <> ''
       AND `notes` NOT LIKE '%【本放送未確認】%';
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_episode_part_supplement();
DROP PROCEDURE IF EXISTS _v1200_add_episode_part_supplement;
