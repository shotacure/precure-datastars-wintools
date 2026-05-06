-- =============================================================================
-- Migration: v1.2.1 — series テーブルに hide_storyboard_role 列を追加
-- =============================================================================
-- 背景:
--   初期のプリキュアシリーズ（『ふたりはプリキュア』〜『スマイルプリキュア！』までを想定）の
--   エンディングクレジットでは「絵コンテ」と「演出」が独立した役職として並列表記されず、
--   実質的に一体のクレジット行として扱われていた。たとえば同一人物が両方を兼ねた回では
--   「（絵コンテ・）演出 大塚 隆史」のようにまとめて記載され、別人物だった回でも
--   「演出 西尾 大介 （絵コンテ）／大塚 隆史 （演出）」のように 1 ブロック内で
--   並列表示される慣習があった。
--
--   v1.2.1 ではこの表示慣習をプレビューレンダラ側の専用ロジックで再現する。
--   本マイグレーションでは、「絵コンテ」役職を独立表示せず「演出」と融合表示するか
--   をシリーズ単位で制御するためのブール列 hide_storyboard_role を追加する。
--
--   既存シリーズはすべて DEFAULT 0 で導入し、運用者が後からシリーズ編集画面で
--   ON にする運用とする（過去シリーズの一括有効化は移行作業と切り分け）。
--
-- 本スクリプトは INFORMATION_SCHEMA で列の存在を確認してから ALTER する冪等形式。
-- 何度実行しても同じ結果になる。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- -----------------------------------------------------------------------------
-- STEP 1: hide_storyboard_role 列の ADD
-- -----------------------------------------------------------------------------
SET @has_col = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'series'
    AND COLUMN_NAME  = 'hide_storyboard_role'
);

SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `series` ADD COLUMN `hide_storyboard_role` TINYINT(1) NOT NULL DEFAULT 0 COMMENT ''絵コンテ役職を独立表示せず演出と融合表示するか（v1.2.1 追加。プレビュー描画専用フラグ）'' AFTER `font_subtitle`',
  'SELECT ''series.hide_storyboard_role already exists. skipping ADD COLUMN.'' AS msg');

PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

SELECT 'v1.2.1 migration completed: series.hide_storyboard_role added.' AS status;
