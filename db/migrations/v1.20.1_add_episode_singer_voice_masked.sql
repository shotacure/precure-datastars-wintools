-- =====================================================================
-- v1.20.1_add_episode_singer_voice_masked.sql
--
-- エピソードのクレジットに出た歌唱者（episode_theme_song_singers）に、声優の伏せ字を持てるようにする。
--   voice_masked_text  VARCHAR(64) NULL   「キャラ(CV:？？？)」のように声優名を伏せて出た画面の字。NULL なら伏せていない。
--
-- 声優の名義（voice_person_alias_id など）は正体の人につないだまま持ち、画面の再現にだけこの欄を使う。
-- サイトはクレジットの名前の伏せ字（credit_block_entries.person_masked_text）と同じく
-- 「伏せ字 (正名義)」の形で出す。
--
-- 冪等性: INFORMATION_SCHEMA で列の有無を確かめてから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1201_add_episode_singer_voice_masked;
DELIMITER $$
CREATE PROCEDURE _v1201_add_episode_singer_voice_masked()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_song_singers' AND COLUMN_NAME = 'voice_masked_text'
  ) THEN
    ALTER TABLE `episode_theme_song_singers`
      ADD COLUMN `voice_masked_text` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '声優名を伏せて出た画面の字（「？？？」など）。NULL なら伏せていない。声優の名義は正体の人につないだまま持つ'
        AFTER `slash_voice_person_alias_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1201_add_episode_singer_voice_masked();
DROP PROCEDURE IF EXISTS _v1201_add_episode_singer_voice_masked;
