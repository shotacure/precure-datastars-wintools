
-- =============================================================================
-- Migration: v1.3.0 — song_recording_singers に role_code 列を追加 + PK 変更
-- =============================================================================
-- 背景:
--   song_recording_singers は「歌唱者連名」を保持するテーブルで、これまで PK は
--   (song_recording_id, singer_seq) の 2 列複合だった。
--   stage 16 で「録音に紐付く役職を歌（VOCALS）以外にもコーラス（CHORUS）等を
--   持たせられるようにする」方針に基づき、role_code 列を追加する。
--
--   PK を (song_recording_id, role_code, singer_seq) に変更し、role_code ごとに
--   singer_seq を独立採番する。これは song_credits / bgm_cue_credits と同じパターン。
--   既存データは全て VOCALS 役職として埋まる（既定値マイグレーション）。
--
-- 想定する役職値:
--   VOCALS  ... 歌（既定）
--   CHORUS  ... コーラス（roles マスタに別途投入が必要、本マイグレーションでは投入しない）
--   その他、roles マスタの定義に従う（FK で制約）。
--
-- 実装手順:
--   (1) role_code 列を追加（NOT NULL DEFAULT 'VOCALS' で既存行に値を充填）
--   (2) PK を再構成
--   (3) roles への FK を追加
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で role_code 列の存在をチェック
--   - 既に存在するなら何もせずスキップ
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

-- 既に role_code 列があるかチェック。
SET @has_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'song_recording_singers'
    AND COLUMN_NAME  = 'role_code'
);

-- (1) role_code 列を追加。NOT NULL DEFAULT 'VOCALS' で既存行を一括充填する。
--     song_recording_id の直後に置く（PK の構成順序に合わせる）。
SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `song_recording_singers`
     ADD COLUMN `role_code` VARCHAR(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT ''VOCALS'' AFTER `song_recording_id`',
  'SELECT ''song_recording_singers.role_code already exists, skipping ALTER'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (2) PK を変更。既存 PK が (song_recording_id, singer_seq) なら、それを削除して
--     (song_recording_id, role_code, singer_seq) を再作成する。
--     既存 PK の構成列を確認してスキップ判定する。
SET @pk_has_role = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
  WHERE TABLE_SCHEMA     = DATABASE()
    AND TABLE_NAME       = 'song_recording_singers'
    AND CONSTRAINT_NAME  = 'PRIMARY'
    AND COLUMN_NAME      = 'role_code'
);

-- PK に role_code が含まれていない（旧構成）なら再構成する。
SET @stmt = IF(@pk_has_role = 0,
  'ALTER TABLE `song_recording_singers` DROP PRIMARY KEY, ADD PRIMARY KEY (`song_recording_id`, `role_code`, `singer_seq`)',
  'SELECT ''song_recording_singers PK already includes role_code, skipping'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- (3) roles への FK を追加。
SET @has_fk = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA     = DATABASE()
    AND TABLE_NAME       = 'song_recording_singers'
    AND CONSTRAINT_NAME  = 'fk_srs_role'
    AND CONSTRAINT_TYPE  = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk = 0,
  'ALTER TABLE `song_recording_singers` ADD CONSTRAINT `fk_srs_role` FOREIGN KEY (`role_code`) REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE RESTRICT',
  'SELECT ''fk_srs_role already exists, skipping'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: song_recording_singers.role_code added + PK reconstructed' AS final_status;
