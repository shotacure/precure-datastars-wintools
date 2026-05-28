-- v1.4.2: Apple Music / Spotify 関連を全廃するためのマイグレーション。
--   Amazon Creators API 一本に運用を絞った結果、iTunes Lookup フォールバック経路と
--   Apple Music / Spotify アルバムリンク経路をすべて撤去する。本マイグレーションは以下 3 段で構成：
--     1) `cover_image_source = 'apple'` の行についてジャケ画像 3 列（url / source / fetched_at）を
--        NULL クリアする。Apple CDN へのホットリンクで運用していた画像は元 URL がアサインされても
--        attribution 表示の根拠（apple 由来）が消えるため、フィールド一括クリアで「未取得」状態に戻す。
--        後段で Catalog の「ジャケット画像取得（未取得のみ）」を再実行することで、Amazon ASIN が
--        振られた商品は amazon_cd / amazon_digital 経由で復活する。
--     2) `products.apple_album_id` カラム自体を DROP COLUMN で完全撤去する。
--     3) `products.spotify_album_id` カラムも同様に DROP COLUMN で完全撤去する
--        （実データは現時点で 0 件のため、UPDATE 事前クリアは不要）。
--   2) 3) はいずれも INFORMATION_SCHEMA.COLUMNS 経由の冪等化ガード付きで、同一スクリプトの再実行でも
--   安全に空動作する。

-- ── 1) 'apple' 由来カバー画像のクリア ──
UPDATE `products`
   SET `cover_image_url`        = NULL,
       `cover_image_source`     = NULL,
       `cover_image_fetched_at` = NULL
 WHERE `cover_image_source` = 'apple';

-- ── 2) apple_album_id カラム DROP（冪等化） ──
DROP PROCEDURE IF EXISTS _tmp_drop_apple_album_id;
DELIMITER //
CREATE PROCEDURE _tmp_drop_apple_album_id()
BEGIN
    IF EXISTS (
        SELECT 1
          FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE()
           AND TABLE_NAME   = 'products'
           AND COLUMN_NAME  = 'apple_album_id'
    ) THEN
        ALTER TABLE `products` DROP COLUMN `apple_album_id`;
    END IF;
END //
DELIMITER ;
CALL _tmp_drop_apple_album_id();
DROP PROCEDURE _tmp_drop_apple_album_id;

-- ── 3) spotify_album_id カラム DROP（冪等化） ──
DROP PROCEDURE IF EXISTS _tmp_drop_spotify_album_id;
DELIMITER //
CREATE PROCEDURE _tmp_drop_spotify_album_id()
BEGIN
    IF EXISTS (
        SELECT 1
          FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE()
           AND TABLE_NAME   = 'products'
           AND COLUMN_NAME  = 'spotify_album_id'
    ) THEN
        ALTER TABLE `products` DROP COLUMN `spotify_album_id`;
    END IF;
END //
DELIMITER ;
CALL _tmp_drop_spotify_album_id();
DROP PROCEDURE _tmp_drop_spotify_album_id;
