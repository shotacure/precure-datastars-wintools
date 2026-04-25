-- =============================================================================
-- Migration: v1.1.2 -> v1.1.3 劇伴キューに仮 M 番号フラグを追加
-- =============================================================================
-- bgm_cues テーブルに is_temp_m_no カラムを追加し、
-- 既存の m_no_detail が "_temp_" で始まる行には 1 をセットする。
--
-- 背景:
--   M 番号が不明な劇伴音源は、従来 "_temp_034108" のようにダミー番号を
--   m_no_detail に入れて採番している。この値がそのまま閲覧 UI や Web 公開に
--   出ると都合が悪いため、「内部管理用の仮番号である」ことをフラグで明示し、
--   閲覧側では代替表示（例:「(番号不明)」）に差し替えられるようにする。
--   マスタメンテナンス画面では引き続き素の m_no_detail を見せて、採番や
--   実番号へのリネームを行えるようにする。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.3_add_bgm_temp_flag.sql
--
-- 本スクリプトは INFORMATION_SCHEMA で列の存在確認を行ってから ALTER する冪等形式。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- -----------------------------------------------------------------------------
-- STEP 1: bgm_cues.is_temp_m_no 列の追加
--   既存環境では列が未追加のため、COLUMNS ビューで確認してから ALTER する。
--   既定値は 0（正規 M 番号）。NOT NULL。
-- -----------------------------------------------------------------------------
SET @has_col = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'bgm_cues'
    AND COLUMN_NAME  = 'is_temp_m_no'
);

SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `bgm_cues`
     ADD COLUMN `is_temp_m_no` tinyint NOT NULL DEFAULT 0
     AFTER `notes`',
  'SELECT ''is_temp_m_no already exists on bgm_cues. skipping ADD COLUMN.'' AS msg');

PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 2: 既存「_temp_」プレフィックス行の is_temp_m_no を 1 に更新
--   新規カラムは DEFAULT 0 で入っているため、仮番号運用だった行を 1 に上げる。
--   列追加後に 1 回だけ流せばよい（再実行しても結果は変わらず冪等）。
--
--   MySQL Workbench の Safe Update Mode（SQL_SAFE_UPDATES=1）下では、
--   m_no_detail LIKE '_temp_%' のような前方一致は「キー列を使った WHERE では無い」
--   と判定されて Error 1175 で拒否される。
--   そのため本ステップに限り、現在の SQL_SAFE_UPDATES 値を退避してから一時的に
--   無効化し、UPDATE 完了後に元の値へ確実に戻す。
--   セッション変数のため、他セッションや後続スクリプトには影響しない。
-- -----------------------------------------------------------------------------
SET @sql_safe_updates_orig = @@SQL_SAFE_UPDATES;
SET SQL_SAFE_UPDATES = 0;

UPDATE `bgm_cues`
   SET `is_temp_m_no` = 1
 WHERE `m_no_detail` LIKE '\\_temp\\_%' ESCAPE '\\'
   AND `is_temp_m_no` = 0;

-- 退避していた元の Safe Update Mode 設定を復元（冪等性・副作用なしを担保）
SET SQL_SAFE_UPDATES = @sql_safe_updates_orig;

-- -----------------------------------------------------------------------------
-- STEP 3: 確認用サマリ（参考出力）
--   本マイグレーション適用後に仮番号フラグが立っている件数をざっくり表示する。
-- -----------------------------------------------------------------------------
SELECT
  COUNT(*)                                          AS total_bgm_cues,
  SUM(CASE WHEN `is_temp_m_no` = 1 THEN 1 ELSE 0 END) AS temp_numbered_rows
FROM `bgm_cues`
WHERE `is_deleted` = 0;

-- Migration v1.1.3 completed
