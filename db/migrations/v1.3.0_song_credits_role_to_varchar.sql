
-- =============================================================================
-- Migration: v1.3.0 — song_credits.credit_role を enum → varchar(32) + roles FK 化
-- =============================================================================
-- 背景:
--   song_credits.credit_role は enum('LYRICIST','COMPOSER','ARRANGER') で
--   3 値固定だった。stage 16 で「クレジットの役職識別を roles マスタに統一する」
--   方針に基づき、varchar(32) + roles.role_code への外部キー参照に変更する。
--   既存値はリネーム：
--     LYRICIST → LYRICS
--     COMPOSER → COMPOSITION
--     ARRANGER → ARRANGEMENT
--
-- 冪等性:
--   何度流しても結果が同じになるように作ってある。
--   各 ALTER / UPDATE は INFORMATION_SCHEMA で現状を判定し、必要なときだけ実行する。
--
-- Safe Update Mode 対策:
--   Workbench は既定で SQL_SAFE_UPDATES = 1 になっており、KEY 列を使った WHERE 句が
--   無い UPDATE を Error 1175 で拒否する。本マイグレーションの UPDATE は CASE 式で
--   全行を更新する性質上、その判定基準を満たせない。本セッション内に限り
--   SQL_SAFE_UPDATES = 0 にしてからマイグレーションを開始する（接続を切ればフラグも
--   セッション諸共消えるので、Workbench のグローバル設定には影響しない）。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- ─────────────────────────────────────────────────────────────────────
-- 0. 本セッション中だけ Safe Update Mode をオフにする。
--    PREPARE で組み立てた UPDATE もこの設定の影響下に入る。
-- ─────────────────────────────────────────────────────────────────────
SET SESSION SQL_SAFE_UPDATES = 0;

-- ─────────────────────────────────────────────────────────────────────
-- (1) 一時的な作業列 credit_role_code を追加。既に存在すれば DROP して作り直す。
--     これは「中身が古い enum 値の作業列が残ってしまっている」状態からの
--     リカバリにも対応するため。enum 値のままだと UPDATE の CASE 文で参照する
--     `credit_role` がもう存在しない可能性があるので、思い切って毎回作り直す。
-- ─────────────────────────────────────────────────────────────────────

-- 1-a. 残っていれば DROP（無ければスキップ）。
SET @work_col_exists = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND COLUMN_NAME = 'credit_role_code'
);
SET @stmt = IF(@work_col_exists > 0,
  'ALTER TABLE `song_credits` DROP COLUMN `credit_role_code`',
  'SELECT ''skip step 1-a: credit_role_code does not exist'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- 1-b. credit_role が enum のまま（=未移行）なら作業列を追加。
--      既に varchar 化されている（=移行済み）なら何もしない。
SET @old_role_is_enum = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND COLUMN_NAME = 'credit_role'
    AND COLUMN_TYPE LIKE 'enum%'
);
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `song_credits`
     ADD COLUMN `credit_role_code` VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL AFTER `credit_role`',
  'SELECT ''skip step 1-b: credit_role is no longer enum, migration likely already done'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (2) 旧 enum 値をリネームしながら作業列にコピー。
--     credit_role が enum のときだけ走らせる（移行済みではスキップ）。
-- ─────────────────────────────────────────────────────────────────────
SET @stmt = IF(@old_role_is_enum > 0,
  'UPDATE `song_credits` SET `credit_role_code` = CASE `credit_role`
      WHEN ''LYRICIST'' THEN ''LYRICS''
      WHEN ''COMPOSER''  THEN ''COMPOSITION''
      WHEN ''ARRANGER''  THEN ''ARRANGEMENT''
      ELSE `credit_role`
    END',
  'SELECT ''skip step 2: credit_role is no longer enum'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (3-pre) PK を削除する前に、FK fk_song_credits_song（song_id → songs.song_id）が
--          利用するインデックスを別途確保しておく。PK だけが song_id を含む唯一の
--          インデックスだと、PK を DROP すると FK が依存先を失って Error 1553 になる。
--          ix_song_credits_song を新規追加して FK の依存先を移すと、PK を安全に
--          DROP できる。本インデックスは移行完了後も残置（song_id 検索用に有用）。
-- ─────────────────────────────────────────────────────────────────────
SET @has_song_idx = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND INDEX_NAME = 'ix_song_credits_song'
);
SET @stmt = IF(@has_song_idx = 0,
  'ALTER TABLE `song_credits` ADD KEY `ix_song_credits_song` (`song_id`)',
  'SELECT ''skip step 3-pre: ix_song_credits_song already exists'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (3) PK を一旦削除。credit_role が enum のときだけ。
--     先に FK を落とさないと PK は DROP できないが、song_credits の PK を参照する
--     FK は schema.sql 上では存在しないはず。もし他経由で張られている場合は
--     Error 1553 が出るので、その場合は別途 FK を確認・撤去すること。
-- ─────────────────────────────────────────────────────────────────────
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `song_credits` DROP PRIMARY KEY',
  'SELECT ''skip step 3: already migrated'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (4) 旧 enum 列を削除。enum なら DROP。
-- ─────────────────────────────────────────────────────────────────────
SET @stmt = IF(@old_role_is_enum > 0,
  'ALTER TABLE `song_credits` DROP COLUMN `credit_role`',
  'SELECT ''skip step 4: credit_role column already removed/renamed'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (5) 作業列 credit_role_code を credit_role にリネーム。NOT NULL を付ける。
--     credit_role_code が存在するときだけ走らせる
--     （= 1-b で追加したか、リカバリで残っていた状態を 1-a で消さなかったケース）。
-- ─────────────────────────────────────────────────────────────────────
SET @work_col_exists_now = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND COLUMN_NAME = 'credit_role_code'
);
SET @stmt = IF(@work_col_exists_now > 0,
  'ALTER TABLE `song_credits`
     CHANGE COLUMN `credit_role_code` `credit_role` VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL',
  'SELECT ''skip step 5: credit_role_code does not exist'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (6) PK を再構成。PK が無い状態（= 3 で DROP した）なら追加する。
-- ─────────────────────────────────────────────────────────────────────
SET @has_pk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND CONSTRAINT_TYPE = 'PRIMARY KEY'
);
SET @stmt = IF(@has_pk = 0,
  'ALTER TABLE `song_credits` ADD PRIMARY KEY (`song_id`, `credit_role`, `credit_seq`)',
  'SELECT ''skip step 6: PK already exists'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (7) credit_role 単独の検索インデックスを追加。
--     INFORMATION_SCHEMA.STATISTICS で同名インデックスの有無を確認してから追加。
-- ─────────────────────────────────────────────────────────────────────
SET @has_idx = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND INDEX_NAME = 'ix_song_credits_role'
);
SET @stmt = IF(@has_idx = 0,
  'ALTER TABLE `song_credits` ADD KEY `ix_song_credits_role` (`credit_role`)',
  'SELECT ''skip step 7: ix_song_credits_role already exists'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ─────────────────────────────────────────────────────────────────────
-- (8) roles マスタへの FK を追加。INFORMATION_SCHEMA.TABLE_CONSTRAINTS で確認。
-- ─────────────────────────────────────────────────────────────────────
SET @has_fk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'song_credits'
    AND CONSTRAINT_NAME = 'fk_song_credits_role'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk = 0,
  'ALTER TABLE `song_credits`
     ADD CONSTRAINT `fk_song_credits_role`
       FOREIGN KEY (`credit_role`) REFERENCES `roles`(`role_code`)
       ON UPDATE CASCADE ON DELETE RESTRICT',
  'SELECT ''skip step 8: fk_song_credits_role already exists'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: song_credits.credit_role to varchar + roles FK' AS final_status;
