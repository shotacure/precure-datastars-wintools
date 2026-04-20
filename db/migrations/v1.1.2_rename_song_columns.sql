-- =============================================================================
-- v1.1.1 → v1.1.2 差分マイグレーション：songs テーブルのカラム名から
--                                       接頭辞 `original_` を撤去する
-- =============================================================================
--
-- 背景:
--   v1.1.0 で songs テーブルを新設した際、作詞者／作曲者の列名に接頭辞
--   `original_` を付けていた（当初は「カバー／派生アレンジでは別の作詞作曲者を
--    フィールド別に持たせる」案を検討していた名残）。その後、同一メロディでも
--   アレンジ違いなら別の songs 行として表現する設計に固まり、派生アレンジ側の
--   作詞作曲者フィールドを追加する必然性は消えた。現状 `original_` は意味を
--   なしていないため、素直に `lyricist_name` / `composer_name` へ改名する。
--   `arranger_name` / `arranger_name_kana` は元々 original_ 無しのため触らない。
--   結果として songs は `lyricist_name` / `composer_name` / `arranger_name` の
--   統一命名になる。
--
-- 対象列:
--   original_lyricist_name       → lyricist_name
--   original_lyricist_name_kana  → lyricist_name_kana
--   original_composer_name       → composer_name
--   original_composer_name_kana  → composer_name_kana
--
-- 実行方針:
--   INFORMATION_SCHEMA.COLUMNS で旧列の残存を確認してから ALTER TABLE
--   ... RENAME COLUMN を実行する（冪等: 再実行しても安全に DO 0 で飛ばす）。
--   MySQL 8.0 の RENAME COLUMN は列型を再指定する必要が無く、インデックスや
--   FK は自動追随するため、制約の張り直しは不要。
--
-- 前提環境: MySQL 8.0 以降 / precure_datastars 接続済み。
-- =============================================================================

SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;

SET FOREIGN_KEY_CHECKS = 0;
SET SQL_SAFE_UPDATES   = 0;

-- -----------------------------------------------------------------------------
-- STEP 1: original_lyricist_name → lyricist_name
-- -----------------------------------------------------------------------------
SET @has_old_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'songs'
     AND COLUMN_NAME  = 'original_lyricist_name'
);
SET @stmt = IF(@has_old_col > 0,
  'ALTER TABLE `songs` RENAME COLUMN `original_lyricist_name` TO `lyricist_name`',
  'DO 0'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 2: original_lyricist_name_kana → lyricist_name_kana
-- -----------------------------------------------------------------------------
SET @has_old_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'songs'
     AND COLUMN_NAME  = 'original_lyricist_name_kana'
);
SET @stmt = IF(@has_old_col > 0,
  'ALTER TABLE `songs` RENAME COLUMN `original_lyricist_name_kana` TO `lyricist_name_kana`',
  'DO 0'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 3: original_composer_name → composer_name
-- -----------------------------------------------------------------------------
SET @has_old_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'songs'
     AND COLUMN_NAME  = 'original_composer_name'
);
SET @stmt = IF(@has_old_col > 0,
  'ALTER TABLE `songs` RENAME COLUMN `original_composer_name` TO `composer_name`',
  'DO 0'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- STEP 4: original_composer_name_kana → composer_name_kana
-- -----------------------------------------------------------------------------
SET @has_old_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'songs'
     AND COLUMN_NAME  = 'original_composer_name_kana'
);
SET @stmt = IF(@has_old_col > 0,
  'ALTER TABLE `songs` RENAME COLUMN `original_composer_name_kana` TO `composer_name_kana`',
  'DO 0'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- -----------------------------------------------------------------------------
-- 後片付け
-- -----------------------------------------------------------------------------
SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;

-- 実行結果の確認用（コメントアウト。必要なら手で流す）:
--   SHOW CREATE TABLE songs\G
--   SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'songs'
--    ORDER BY ORDINAL_POSITION;
