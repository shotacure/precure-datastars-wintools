-- ===========================================================================
-- v1.3.6_migration_products_cover_image_cache
--
-- 目的:
--   商品（products）にジャケット画像キャッシュ列を追加する。画像実体は保存せず、
--   提供元 CDN の URL のみを保持するホットリンク運用とする。
--     - cover_image_url        画像 URL（フェーズ 1 は iTunes 由来の Apple CDN URL）
--     - cover_image_source     取得元（'apple' 等。PA-API 開通後は 'amazon' も想定）
--     - cover_image_fetched_at 取得日時（再取得＝鮮度判定に使用）
--
-- 配置:
--   spotify_album_id の直後（外部プラットフォーム ID 群の隣）に意味的に配置する。
--   AFTER の依存関係を保つため cover_image_url → cover_image_source →
--   cover_image_fetched_at の順に実行する。
--
-- 冪等性:
--   MySQL は ALTER TABLE ... ADD COLUMN IF NOT EXISTS をサポートしない
--   （IF NOT EXISTS は MariaDB の拡張）。そのため INFORMATION_SCHEMA.COLUMNS で
--   各列の存在を確認し、未存在のときだけ ALTER を動的実行する。何度再実行しても
--   既存列はスキップされるため安全に素通りする。スキーマ名は DATABASE() で解決し、
--   ハードコードしない。
--
-- 前提バージョン: v1.3.6（Directory.Build.props）
-- ===========================================================================

-- cover_image_url --------------------------------------------------------------
SELECT COUNT(*) INTO @col_exists
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE()
   AND TABLE_NAME   = 'products'
   AND COLUMN_NAME  = 'cover_image_url';

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `products` ADD COLUMN `cover_image_url` varchar(512) DEFAULT NULL AFTER `spotify_album_id`',
  'SELECT 1');
PREPARE st FROM @ddl;
EXECUTE st;
DEALLOCATE PREPARE st;

-- cover_image_source -----------------------------------------------------------
SELECT COUNT(*) INTO @col_exists
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE()
   AND TABLE_NAME   = 'products'
   AND COLUMN_NAME  = 'cover_image_source';

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `products` ADD COLUMN `cover_image_source` varchar(16) DEFAULT NULL AFTER `cover_image_url`',
  'SELECT 1');
PREPARE st FROM @ddl;
EXECUTE st;
DEALLOCATE PREPARE st;

-- cover_image_fetched_at -------------------------------------------------------
SELECT COUNT(*) INTO @col_exists
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE()
   AND TABLE_NAME   = 'products'
   AND COLUMN_NAME  = 'cover_image_fetched_at';

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `products` ADD COLUMN `cover_image_fetched_at` datetime DEFAULT NULL AFTER `cover_image_source`',
  'SELECT 1');
PREPARE st FROM @ddl;
EXECUTE st;
DEALLOCATE PREPARE st;
