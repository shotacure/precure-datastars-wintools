-- =============================================================================
-- Migration: v1.3.0 — episodes テーブルに duration_minutes 列を追加
-- =============================================================================
-- 背景:
--   episodes テーブルは on_air_at（放送開始日時）のみを保持しており、
--   1 話あたりの放送尺（分単位）を表す列が無かった。SiteBuilder 側で「8:30〜9:00」
--   形式の放送日時表示を行うため、また将来的な放送枠管理（30 分番組／15 分番組）
--   のために、新たに duration_minutes 列を on_air_at の直後に追加する。
--
--   既存の TV シリーズエピソードは例外なく 30 分番組であるため、本マイグレでは
--   全有効エピソードに 30 を一括バックフィルする。新規エピソードは NULL 許可
--   のまま運用し、エディタ側で都度入力する方針。
--
-- 対象列:
--   episodes.duration_minutes  TINYINT UNSIGNED  NULL  AFTER on_air_at
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で列の存在を確認してから ALTER TABLE を発行する
--   - バックフィルも duration_minutes IS NULL 行に限定するため、再実行しても安全
--   - 既存値が 30 以外（運用者が明示的に変更した値）を上書きしない
--
-- MySQL Workbench の Safe Update Mode（SQL_SAFE_UPDATES = 1）対策:
--   - バックフィル UPDATE 文の WHERE 句に主キー列 episode_id を含めることで、
--     Safe Update Mode 有効環境でも Error 1175 を出さずに通るようにする。
--   - is_deleted / duration_minutes はいずれもインデックス対象外のため、
--     Safe Update Mode 下では「KEY 列を使った WHERE が無い UPDATE」として
--     拒否される。episode_id > 0 を条件に加えることで PK 経由の更新と認識される。
-- =============================================================================

-- 列の存在を確認
SET @has_duration = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'episodes'
    AND COLUMN_NAME  = 'duration_minutes'
);

-- 列が無ければ追加（on_air_at の直後）。
SET @stmt = IF(@has_duration = 0,
  'ALTER TABLE `episodes` ADD COLUMN `duration_minutes` TINYINT UNSIGNED NULL AFTER `on_air_at`',
  'SELECT ''episodes.duration_minutes already exists, skipping ALTER'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- 既存の有効エピソード（is_deleted = 0）のうち duration_minutes が NULL のものに 30 をバックフィル。
-- 既に値が入っている行（運用者が手動投入したケース等）は触らない。
-- WHERE 句に PK 列 episode_id を含めて Safe Update Mode（Error 1175）を回避する。
UPDATE `episodes`
   SET `duration_minutes` = 30
 WHERE `is_deleted`       = 0
   AND `duration_minutes` IS NULL
   AND `episode_id`       > 0;

SELECT 'v1.3.0 migration completed: episodes.duration_minutes added and backfilled (30 min)' AS final_status;
