-- =============================================================================
-- v1.3.4 → v1.3.5 差分マイグレーション：precures に key_color（バッジ地色）を追加
-- =============================================================================
--
-- 背景:
--   シリーズ一覧 TV サブ行のプリキュア表示を、文字列連結から色付きバッジへ
--   刷新した。バッジの地色をプリキュア単位で持たせるため、プリキュア本体
--   マスタ precures に地色カラム key_color（#RRGGBB、NULL 可）を追加する。
--   サイト側は地色の相対輝度から文字色（暗/明グレー）を自動算出するため、
--   本カラムには地色のみを保持すればよい（文字色は保持しない）。
--
-- 変更内容:
--   1. precures.key_color 列を追加（char(7)、NULL 可、voice_actor_person_id の直後）
--   2. CHECK 制約 ck_precures_key_color を追加（NULL もしくは ^#[0-9A-Fa-f]{6}$）
--   3. 暫定地色の初期投入（precure_id=1 → #000000、precure_id=2 → #ffffff）
--      ※ key_color が未設定（NULL）の行に対してのみ適用し、既存値・手動設定値は
--        上書きしない（再実行・後続編集に対して非破壊）。
--
-- 適用対象:
--   v1.3.4 まで適用済みの precure_datastars データベース。
--
-- 冪等性:
--   - 列・制約の追加は INFORMATION_SCHEMA で存在を確認してから動的 SQL で
--     実行するため、適用済みの DB に再実行しても安全（存在時は DO 0 で素通り）。
--   - 暫定地色の UPDATE は key_color IS NULL 条件付きのため、再実行しても
--     既に設定済みの値を書き換えない。
--
-- 適用方法（v1.3.4 の DB に対して）:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.3.5_migration_precure_key_color.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;

-- ---------------------------------------------------------------------------
-- 1) precures.key_color 列の追加（未存在時のみ）
--    schema.sql と同じ物理位置（voice_actor_person_id の直後 = skin_color_h の直前）。
-- ---------------------------------------------------------------------------
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'precures'
    AND COLUMN_NAME  = 'key_color'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `precures` ADD COLUMN `key_color` char(7) DEFAULT NULL AFTER `voice_actor_person_id`',
  'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 2) CHECK 制約 ck_precures_key_color の追加（未存在時のみ）
--    NULL もしくは #RRGGBB 形式（16 進 6 桁）のみ許可する。
-- ---------------------------------------------------------------------------
SET @ck_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME        = 'precures'
    AND CONSTRAINT_NAME   = 'ck_precures_key_color'
    AND CONSTRAINT_TYPE   = 'CHECK'
);
SET @sql := IF(@ck_exists = 0,
  'ALTER TABLE `precures` ADD CONSTRAINT `ck_precures_key_color` CHECK (`key_color` IS NULL OR (`key_color` REGEXP ''^#[0-9A-Fa-f]{6}$''))',
  'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 3) 暫定地色の初期投入（未設定行のみ・非破壊）
--    本番運用で正式な地色が決まり次第、各プリキュアの key_color を更新する。
-- ---------------------------------------------------------------------------
UPDATE `precures` SET `key_color` = '#000000' WHERE `precure_id` = 1 AND `key_color` IS NULL;
UPDATE `precures` SET `key_color` = '#ffffff' WHERE `precure_id` = 2 AND `key_color` IS NULL;
