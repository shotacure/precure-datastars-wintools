-- v1.4.2: series.amazon_prime_distribution_url 列を撤去する。
--   Amazon Prime Video の配信リンクは amazon_prime_video_asin（動画 ASIN）＋現行アソシエイトタグから
--   SiteBuilder 側で /gp/video/detail/{ASIN}/?tag=... を生成する方式へ完全移行したため、
--   旧来のフル URL / amzn.to 短縮リンクを格納していた本列は不要になった
--   （短縮リンクには旧タグ shotacure00-22 が焼かれており、ASIN 化で正しいタグに統一済み）。
--   ASIN は v1.4.2_add_series_amazon_prime_video_asin.sql で既に amazon_prime_video_asin に移送済み。
--   INFORMATION_SCHEMA.COLUMNS 経由の冪等化ガード付きで、同一スクリプト再実行でも安全に空動作する。

DROP PROCEDURE IF EXISTS _tmp_drop_series_prime_url;
DELIMITER //
CREATE PROCEDURE _tmp_drop_series_prime_url()
BEGIN
    IF EXISTS (
        SELECT 1
          FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE()
           AND TABLE_NAME   = 'series'
           AND COLUMN_NAME  = 'amazon_prime_distribution_url'
    ) THEN
        ALTER TABLE `series` DROP COLUMN `amazon_prime_distribution_url`;
    END IF;
END //
DELIMITER ;
CALL _tmp_drop_series_prime_url();
DROP PROCEDURE _tmp_drop_series_prime_url;
