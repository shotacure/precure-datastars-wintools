-- =====================================================================
-- v1.20.0_add_digital_isrc.sql
--
-- 配信版の ISRC を、CD のトラックに添えて持てるようにする。
-- 同じ盤でも、配信では ISRC が振り直されていることがある
-- （CD で別の盤と番号が重なっていた盤が、配信では直されているなど）。
-- 値は Deezer の公開 API から PrecureDataStars.DeezerIsrcSync で取り込む。
--
--   products + 1 列:
--     deezer_album_id          照らした配信アルバムの Deezer の album id。照合できなかった商品は NULL
--
--   tracks + 2 列:
--     digital_isrc             配信版の ISRC（12 文字）。CD と同じ値でも入れる
--     digital_isrc_checked_at  配信版の ISRC を確かめた時刻。NULL は未確認
--
-- digital_isrc は sub_order = 0 の親行にだけ持つ（isrc と同じ）。
-- CD と同じ値でも入れるのは、「確かめて同じだった」と「まだ見ていない」を分けるため。
-- 配信にだけある曲や配信限定の盤は、CD のトラックが無いのでこの列では表せない。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1191_add_col_if_missing(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_col;
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_col_if_missing('products', 'deezer_album_id', "BIGINT DEFAULT NULL COMMENT '配信版の ISRC を照らした Deezer の album id。照合できなかった商品は NULL' AFTER `youtube_art_track_checked_at`");

CALL _v1191_add_col_if_missing('tracks', 'digital_isrc',            "CHAR(12) DEFAULT NULL COMMENT '配信版の ISRC。CD と同じ値でも入れる。sub_order = 0 の親行にだけ持つ' AFTER `youtube_checked_at`");
CALL _v1191_add_col_if_missing('tracks', 'digital_isrc_checked_at', "DATETIME DEFAULT NULL COMMENT '配信版の ISRC を確かめた時刻。NULL は未確認' AFTER `digital_isrc`");

DROP PROCEDURE IF EXISTS _v1191_add_col_if_missing;
