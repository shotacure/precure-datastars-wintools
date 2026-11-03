-- =====================================================================
-- v1.20.0_add_theme_song_size.sql
--
-- エピソードの主題歌・挿入歌の紐付け（episode_theme_songs）に、その回で流れたサイズを持てるようにする。
--   song_size_variant_code  VARCHAR(32) NULL   → song_size_variants.variant_code
--                                              NULL ならサイズの指定なし（エピソード詳細にサイズを出さない）。
--
-- エピソード詳細の主題歌・挿入歌の一覧で、曲名の後ろにサイズの名前（song_size_variants.name_ja）を添え、
-- 再生ボタンは同じサイズの配信音源があればそれを鳴らす。
-- シリーズ詳細は録音ごとにまとめるので、サイズが違っても 1 行のまま（サイズは出さない）。
-- クレジットのカード（役職テンプレの描画）は画面の表記どおりなので、この列を使わない。
--
-- 冪等性: 列・外部キーごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_theme_song_size;
DELIMITER $$
CREATE PROCEDURE _v1200_add_theme_song_size()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs' AND COLUMN_NAME = 'song_size_variant_code'
  ) THEN
    ALTER TABLE `episode_theme_songs`
      ADD COLUMN `song_size_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
        COMMENT 'その回で流れたサイズ（→ song_size_variants）。NULL なら指定なし'
        AFTER `song_recording_id`,
      ADD KEY `ix_ets_song_size` (`song_size_variant_code`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs' AND CONSTRAINT_NAME = 'fk_ets_song_size'
  ) THEN
    ALTER TABLE `episode_theme_songs`
      ADD CONSTRAINT `fk_ets_song_size` FOREIGN KEY (`song_size_variant_code`)
        REFERENCES `song_size_variants` (`variant_code`) ON DELETE RESTRICT ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_theme_song_size();
DROP PROCEDURE IF EXISTS _v1200_add_theme_song_size;
