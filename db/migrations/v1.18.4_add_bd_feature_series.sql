-- =====================================================================
-- v1.18.4_add_bd_feature_series.sql
--
-- Blu-ray の本編（FEATURE）がどの作品のものかを、プレイリストとチャプターの単位で持てるようにする。
--
-- 映画の盤には、併映作品（series.parent_series_id で親の映画にぶら下がる COFEATURE / SEGMENT の作品）が
-- 「独立したプレイリストで入っている」ことも「本編と 1 本のプレイリストに上映順で続けて入っている」こともある。
-- 盤の作品（bd_discs.series_id）は親の映画のまま、
--   bd_playlists.series_id ... そのプレイリストの本編の作品（併映と続いているときは親の映画。3 本立ては親のまとまり）
--   bd_chapters.series_id  ... そのチャプターが当たる作品（併映と続いているときは、チャプターごとに本編か併映か）
-- を入れる。話（episodes）を持つ TV 作品の盤では NULL のまま。
--
-- 既に FEATURE にしてあるプレイリストとチャプターは、盤の作品で埋める。
--
-- 冪等性: 列と外部キーは INFORMATION_SCHEMA で見てから足す。埋めるのは NULL の行だけ。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_series_col;
DELIMITER $$
CREATE PROCEDURE _v1184_add_series_col(IN p_table VARCHAR(64), IN p_after VARCHAR(64), IN p_comment VARCHAR(255), IN p_fk VARCHAR(64), IN p_key VARCHAR(64))
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = 'series_id';
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `series_id` int DEFAULT NULL COMMENT ''', p_comment, ''' AFTER `', p_after, '`, ',
                       'ADD KEY `', p_key, '` (`series_id`), ',
                       'ADD CONSTRAINT `', p_fk, '` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_series_col('bd_playlists', 'episode_id',
  '作品単位の本編のプレイリストが収める作品（→ series。併映と続いているときは親の映画、3 本立ては親のまとまり）',
  'fk_bd_playlists_series', 'ix_bd_playlists_series');
CALL _v1184_add_series_col('bd_chapters', 'episode_seq',
  '作品単位の本編のチャプターが当たる作品（→ series。併映と続いているときは本編か併映か）',
  'fk_bd_chapters_series', 'ix_bd_chapters_series');

DROP PROCEDURE _v1184_add_series_col;

START TRANSACTION;

UPDATE `bd_playlists` p JOIN `bd_discs` d ON d.disc_id = p.disc_id
   SET p.series_id = d.series_id
 WHERE p.playlist_kind = 'FEATURE' AND p.series_id IS NULL AND d.series_id IS NOT NULL;

UPDATE `bd_chapters` c JOIN `bd_discs` d ON d.disc_id = c.disc_id
   SET c.series_id = d.series_id
 WHERE c.chapter_kind = 'FEATURE' AND c.series_id IS NULL AND d.series_id IS NOT NULL;

COMMIT;
