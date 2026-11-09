-- =====================================================================
-- v1.20.0_add_card_media_version.sql
--
-- クレジットのカード（credit_cards）に版を持てるようにする。
--   media_version  ENUM('BROADCAST','HOME_VIDEO') NULL
--                  BROADCAST = 本放送、HOME_VIDEO = Blu-ray・配信、NULL = 共通（通常）。
--
-- 本放送と Blu-ray・配信でクレジットの一部のカードが違う回は、クレジットを 1 本のまま、
-- 同じカードは共通（NULL）で 1 回だけ持ち、違うところは本放送のカードと Blu-ray・配信のカードを
-- 隣どうしに並べる。サイトは版の付いたカードが続くところを 1 つの枠にまとめ、上に本放送・下に Blu-ray・配信を描く。
-- 集計は共通と Blu-ray・配信のカードだけで数え、本放送のカードは表示だけに使う。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_card_media_version;
DELIMITER $$
CREATE PROCEDURE _v1200_add_card_media_version()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_cards' AND COLUMN_NAME = 'media_version'
  ) THEN
    ALTER TABLE `credit_cards`
      ADD COLUMN `media_version` enum('BROADCAST','HOME_VIDEO') DEFAULT NULL
        COMMENT 'カードの版（BROADCAST=本放送、HOME_VIDEO=Blu-ray・配信、NULL=共通）'
        AFTER `presentation`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_card_media_version();
DROP PROCEDURE IF EXISTS _v1200_add_card_media_version;
