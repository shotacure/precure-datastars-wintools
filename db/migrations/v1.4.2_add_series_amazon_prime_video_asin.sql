-- v1.4.2: series に Amazon Prime Video の ASIN 列を新設し、既存の配信 URL から抽出した ASIN を投入する。
--   従来 `series.amazon_prime_distribution_url` には Amazon 生成のフル URL や amzn.to 短縮リンクを
--   そのまま保存していたが、短縮リンクは旧アソシエイトタグ（shotacure00-22）で焼かれている等の問題があった。
--   ASIN だけを保持しておけば、SiteBuilder 側で現行タグ（AmazonAssociateTag）を付けて
--   `/gp/video/detail/{ASIN}?tag=...` を生成でき、タグの一元管理ができる。
--   本マイグレーションは「列追加 + 既存 URL を解決して得た ASIN の一括投入」まで。
--   既存 `amazon_prime_distribution_url` 列は当面そのまま残す（描画切替・列削除は別タスク）。
--
--   ASIN は既存 URL（フル URL は直抽出、amzn.to 短縮はリダイレクト解決）から取得した値をハードコードする。
--   series_id をキーにした CASE 一括 UPDATE。対象外（URL 未登録）の series は触らない。

-- ── 1) 列追加（冪等化） ──
DROP PROCEDURE IF EXISTS _tmp_add_series_prime_asin;
DELIMITER //
CREATE PROCEDURE _tmp_add_series_prime_asin()
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE()
           AND TABLE_NAME   = 'series'
           AND COLUMN_NAME  = 'amazon_prime_video_asin'
    ) THEN
        ALTER TABLE `series`
            ADD COLUMN `amazon_prime_video_asin` varchar(16) DEFAULT NULL
            AFTER `amazon_prime_distribution_url`;
    END IF;
END //
DELIMITER ;
CALL _tmp_add_series_prime_asin();
DROP PROCEDURE _tmp_add_series_prime_asin;

-- ── 2) 既存 URL から解決した ASIN を投入 ──
UPDATE `series`
SET `amazon_prime_video_asin` = CASE `series_id`
    WHEN 1  THEN 'B0FPM629BK'
    WHEN 2  THEN 'B010MFQJCA'
    WHEN 3  THEN 'B06ZZDCY8W'
    WHEN 4  THEN 'B06ZZNZ2MH'
    WHEN 5  THEN 'B010ECXLUE'
    WHEN 6  THEN 'B071CLMCWL'
    WHEN 7  THEN 'B00ZZ0O28G'
    WHEN 8  THEN 'B071CLL6TL'
    WHEN 9  THEN 'B00ZZ0OC86'
    WHEN 10 THEN 'B06ZY7V367'
    WHEN 12 THEN 'B00TYYIOFG'
    WHEN 13 THEN 'B06ZY7V1RF'
    WHEN 14 THEN 'B06ZYJFNZC'
    WHEN 15 THEN 'B00TYV8N46'
    WHEN 16 THEN 'B071D5SRZQ'
    WHEN 17 THEN 'B071DGFYNS'
    WHEN 18 THEN 'B00TXUF0QC'
    WHEN 19 THEN 'B071CW99J4'
    WHEN 21 THEN 'B071CW52K1'
    WHEN 22 THEN 'B00TXUCXJY'
    WHEN 23 THEN 'B071DG4C2B'
    WHEN 24 THEN 'B071D5M2CB'
    WHEN 25 THEN 'B00TXUCFSS'
    WHEN 26 THEN 'B06ZXYH2Y2'
    WHEN 27 THEN 'B071CW8X48'
    WHEN 28 THEN 'B09HN83HVX'
    WHEN 29 THEN 'B071CLWGTS'
    WHEN 30 THEN 'B071D5NMTX'
    WHEN 31 THEN 'B09HNB2V62'
    WHEN 32 THEN 'B06ZZYF7GB'
    WHEN 33 THEN 'B06ZZ3YDYB'
    WHEN 34 THEN 'B06ZZ3YDYB'
    WHEN 35 THEN 'B06ZZ3YDYB'
    WHEN 36 THEN 'B06ZZ3YDYB'
    WHEN 37 THEN 'B09HNBKL7X'
    WHEN 38 THEN 'B071DP32L6'
    WHEN 39 THEN 'B07B3JVQJH'
    WHEN 40 THEN 'B07B3JVQJH'
    WHEN 41 THEN 'B09HN89MC1'
    WHEN 42 THEN 'B0FMCM3KXK'
    WHEN 43 THEN 'B07PS53F2D'
    WHEN 44 THEN 'B07PS53F2D'
    WHEN 45 THEN 'B09HN8PS23'
    WHEN 46 THEN 'B07PSC9ZJT'
    WHEN 47 THEN 'B0FKYZKW41'
    WHEN 48 THEN 'B09HN8WJZW'
    WHEN 49 THEN 'B0D9WY86R2'
    WHEN 50 THEN 'B08K86JDGW'
    WHEN 51 THEN 'B09HNBSWLV'
    WHEN 52 THEN 'B0FLKSXLH6'
    WHEN 53 THEN 'B0B8TF9FP7'
    WHEN 54 THEN 'B0FGBVSRWP'
    WHEN 55 THEN 'B0FGBVSRWP'
    WHEN 56 THEN 'B0B8QB8QBX'
    WHEN 57 THEN 'B09QG4TRS6'
    WHEN 58 THEN 'B0BX45HXVP'
    WHEN 59 THEN 'B0BX45HXVP'
    WHEN 60 THEN 'B0B8N564YV'
    WHEN 61 THEN 'B0D1VXNYBM'
    WHEN 62 THEN 'B0CG79QGG1'
    WHEN 63 THEN 'B0CTRS36XM'
    WHEN 64 THEN 'B0F9TTP5G6'
    WHEN 65 THEN 'B0DGX2ZPBW'
    WHEN 66 THEN 'B0DTJ8HY4H'
    WHEN 69 THEN 'B0GJ9242VN'
    ELSE `amazon_prime_video_asin`
END
WHERE `series_id` IN
    (1,2,3,4,5,6,7,8,9,10,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,30,31,32,
     33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,
     61,62,63,64,65,66,69);
