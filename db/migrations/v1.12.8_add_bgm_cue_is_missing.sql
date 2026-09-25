-- =====================================================================
-- v1.12.8_add_bgm_cue_is_missing.sql
--
-- 劇伴（bgm_cues）に「欠番」フラグを追加する。
--
--   bgm_cues + 1 列:
--     is_missing  欠番フラグ。番号としては存在するが、音源が制作されていない。
--
-- 欠番の行は M 番号・セッション・セクション・並び順を持ち、メニューは判明していれば入れる
-- （メニューだけ決まって制作されなかった番号と、メニューも無い番号の両方がある）。
-- 作曲・編曲・尺は持たない。映画の movie_bgm_cues.is_missing と同じ意味。
-- 公開サイトでは欠番を曲数・バージョン数から除き、劇伴詳細ではグレーのカードで区別して出す。
--
-- 冪等性: 列の存在確認をしてから ALTER を発行する。
-- =====================================================================

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'bgm_cues'
       AND COLUMN_NAME  = 'is_missing'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `bgm_cues`
        ADD COLUMN `is_missing` tinyint NOT NULL DEFAULT 0
            COMMENT ''欠番フラグ（番号はあるが音源が制作されていない）''
            AFTER `is_temp_m_no`;',
    'SELECT ''bgm_cues.is_missing already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
