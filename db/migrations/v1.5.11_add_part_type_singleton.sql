-- =====================================================================
-- v1.5.11_add_part_type_singleton.sql
--
-- パート種別ごとに「同一エピソード内で 1 回までしか出現しないか」を宣言できるように
-- する。これまで episode_parts には UNIQUE KEY uq_ep_part_type (episode_id, part_type)
-- が張られており、全パート種別が一律「1 話 1 回」に縛られていた。
-- 映画予告（MOVIE_TRAILER）・各種告知（NOTICE）のように同一話で複数回出現しうる
-- 種別を許容するため、一律ガードを撤去し、種別ごとのフラグ + アプリ層バリデーションへ
-- 移行する。
--
--   1) part_types に singleton_per_episode TINYINT(1) NOT NULL DEFAULT 1 を追加
--      （既存全行は 1＝1 話 1 回もの。複数回可にするものだけ後段で 0 に更新）
--   2) MOVIE_TRAILER / NOTICE を singleton_per_episode = 0（複数回出現可）に設定
--   3) episode_parts の UNIQUE KEY uq_ep_part_type を削除
--
-- 既存データは uq_ep_part_type により重複が存在しないため、撤去しても整合は崩れない。
-- 「1 話 1 回もの」種別の重複入力拒否は、エピソード編集ツールの保存バリデーションが担う。
--
-- 冪等性: 列・インデックスの存在を INFORMATION_SCHEMA で確認してから ALTER を発行する。
-- 既存マイグレーション（v1.4.3_add_external_urls.sql 等）のスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 列が無いときだけ ADD COLUMN するプロシージャ。
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v1511_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1511_add_col_if_missing(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_col;
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

-- ---------------------------------------------------------------------
-- インデックス（ここでは UNIQUE KEY）が在るときだけ DROP するプロシージャ。
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v1511_drop_index_if_exists;
DELIMITER $$
CREATE PROCEDURE _v1511_drop_index_if_exists(
  IN p_table VARCHAR(64),
  IN p_index VARCHAR(64))
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND INDEX_NAME   = p_index;
  IF v_exists > 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` DROP INDEX `', p_index, '`');
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

-- ---------------------------------------------------------------------
-- 1) part_types + singleton_per_episode 列
-- ---------------------------------------------------------------------
CALL _v1511_add_col_if_missing(
  'part_types',
  'singleton_per_episode',
  "tinyint(1) NOT NULL DEFAULT '1' COMMENT '1=1話1回もの（重複入力をアプリ層で拒否） / 0=同一話に複数回出現可' AFTER `default_credit_kind`");

-- ---------------------------------------------------------------------
-- 2) 複数回出現可にする種別（映画予告 / 各種告知）を 0 に更新。
--    再実行しても結果は同じ（冪等）。
-- ---------------------------------------------------------------------
UPDATE part_types SET singleton_per_episode = 0 WHERE part_type IN ('MOVIE_TRAILER', 'NOTICE');

-- ---------------------------------------------------------------------
-- 3) episode_parts の一律重複ガード uq_ep_part_type を撤去。
-- ---------------------------------------------------------------------
CALL _v1511_drop_index_if_exists('episode_parts', 'uq_ep_part_type');

DROP PROCEDURE _v1511_add_col_if_missing;
DROP PROCEDURE _v1511_drop_index_if_exists;

COMMIT;
