-- =====================================================================
-- v1.4.2_add_credit_card_role_affiliation_layout.sql
--
-- credit_card_roles に「所属レイアウト」フラグを追加する。
-- 人物エントリの所属（affiliation_company_alias_id / affiliation_text）の
-- 描画方法を、役職インスタンス単位で切り替えるための per-instance フラグ。
--
--   SUFFIX (デフォルト): 名前の右側に小さく `(所属)` を後置。TV のキャスト所属など従来の挙動。
--   PREFIX             : 名前の左側に 80% 縮小フォントの muted な列として屋号を前置。
--                        映画の「製作:」「配給:」「宣伝:」のような 2 カラム表記の役職向け。
--
-- 同じ role_code（例: 製作）でも作品ごとに前置になったり後置になったりするため、
-- ロールマスタ側ではなく credit_card_roles 側（クレジット内に配置された役職インスタンス）で持つ。
--
-- 冪等性: INFORMATION_SCHEMA で列存在チェックしてから ADD COLUMN を発行する。
-- =====================================================================

START TRANSACTION;

DROP PROCEDURE IF EXISTS _v142_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v142_add_col_if_missing(
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

CALL _v142_add_col_if_missing(
  'credit_card_roles',
  'affiliation_layout',
  "ENUM('SUFFIX','PREFIX') NOT NULL DEFAULT 'SUFFIX' COMMENT '人物エントリの所属表記レイアウト。SUFFIX=名前右の(屋号) / PREFIX=名前左の屋号列（映画の製作・配給など）。' AFTER `order_in_group`"
);

DROP PROCEDURE _v142_add_col_if_missing;

COMMIT;
