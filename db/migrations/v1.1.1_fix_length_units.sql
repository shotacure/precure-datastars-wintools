-- =============================================================================
-- Migration: v1.1.1 追加差分（長さ単位の是正）
-- =============================================================================
-- 背景:
--   v1.1.0 までは discs.total_length_frames を CD-DA の 1/75 秒単位で定義しており、
--   BD/DVD についても「秒 × 75」で換算して同じ列に詰め込んでいた。同様に num_chapters も
--   「BD/DVD 用、CD も便宜的に使用可」として CD では total_tracks と同じ値を冗長に格納していた。
--
--   しかしこれは意味論の混乱を招く:
--     - CD-DA には「チャプター」という概念が存在しない（トラックしかない）
--     - BD/DVD は本来 ms 精度で尺を扱えるのに、CD-DA の 1/75秒（≒13.3ms）に丸められてしまう
--
--   v1.1.1 で以下のように整理する:
--     - discs.total_length_ms (BIGINT UNSIGNED) を新設し、BD/DVD はこちらに ms 精度で格納する
--     - discs.total_length_frames は CD-DA 専用とし、BD/DVD では NULL
--     - discs.num_chapters は BD/DVD 専用とし、CD-DA では NULL
--     - discs.total_tracks は CD-DA 専用（BD/DVD では NULL）
--
-- 適用対象:
--   v1.1.1 の先行マイグレーション (v1.1.1_move_series_id_to_disc.sql) 適用済みの
--   precure_datastars データベース。本スクリプトはその後に流す想定。
--
-- 安全性:
--   - 本スクリプトは冪等。2 回目以降の実行では INFORMATION_SCHEMA で各ステップの適用状態を
--     確認してスキップする。
--   - BD/DVD 既存行の total_length_frames → total_length_ms 換算は整数演算で行うため
--     最大 13ms 程度の丸め誤差が出る（元々 CD-DA フレームに丸めた時点で既に失われていた精度）。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_fix_length_units.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;
SET FOREIGN_KEY_CHECKS = 0;
SET SQL_SAFE_UPDATES   = 0;

-- -----------------------------------------------------------------------------
-- STEP 1: discs.total_length_ms 列を追加（既に存在していればスキップ）
-- -----------------------------------------------------------------------------
-- 位置は total_length_frames の直後、num_chapters の前に置くと意味論が読み取りやすい。
-- BIGINT UNSIGNED にしておくのは、長尺 BD（例 4 時間 = 14,400,000ms）でも
-- INT UNSIGNED の上限（約 4.29 × 10^9 ms）は余裕だが、将来の拡張を見越して BIGINT を採用する。
SET @has_total_length_ms = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'discs'
     AND COLUMN_NAME  = 'total_length_ms'
);
SET @stmt = IF(@has_total_length_ms > 0,
  'DO 0',
  'ALTER TABLE `discs`
     ADD COLUMN `total_length_ms` bigint unsigned DEFAULT NULL AFTER `total_length_frames`'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 2: total_length_ms に対する CHECK 制約を追加（既に存在していればスキップ）
-- -----------------------------------------------------------------------------
SET @has_ck_ms = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA    = DATABASE()
     AND TABLE_NAME      = 'discs'
     AND CONSTRAINT_NAME = 'ck_discs_total_length_ms_nonneg'
);
SET @stmt = IF(@has_ck_ms > 0,
  'DO 0',
  'ALTER TABLE `discs`
     ADD CONSTRAINT `ck_discs_total_length_ms_nonneg`
     CHECK (((`total_length_ms` is null) or (`total_length_ms` >= 0)))'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 3: BD/DVD 既存行のデータ変換
-- -----------------------------------------------------------------------------
-- v1.1.0 の BD/DVD 行は total_length_frames に「秒 × 75」で換算した値が入っている。
-- これを ms 精度に戻して total_length_ms に格納し、total_length_frames は NULL に落とす。
-- 換算式: ms = frames * 1000 / 75 （整数演算、最大 13ms 程度の誤差）
--
-- CD-DA の「チャプター概念がない」点の是正もここでまとめて行う:
--   - CD / CD_ROM: num_chapters を NULL に（旧仕様では total_tracks と同値を冗長格納していた）
--   - BD / DVD: total_tracks を NULL に（もともと CD-DA 専用列）
UPDATE `discs`
   SET `total_length_ms`     = CAST(`total_length_frames` AS UNSIGNED) * 1000 DIV 75,
       `total_length_frames` = NULL
 WHERE `media_format` IN ('BD', 'DVD')
   AND `total_length_frames` IS NOT NULL
   AND `total_length_ms`     IS NULL; -- 既に ms 側へ移し終えた行は触らない（冪等性）

UPDATE `discs`
   SET `total_tracks` = NULL
 WHERE `media_format` IN ('BD', 'DVD')
   AND `total_tracks` IS NOT NULL;

UPDATE `discs`
   SET `num_chapters` = NULL
 WHERE `media_format` IN ('CD', 'CD_ROM')
   AND `num_chapters` IS NOT NULL;

-- -----------------------------------------------------------------------------
-- 後片付け
-- -----------------------------------------------------------------------------
SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;

-- Migration v1.1.1 (length units fix) completed
