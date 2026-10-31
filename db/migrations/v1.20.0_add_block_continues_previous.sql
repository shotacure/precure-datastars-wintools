-- =====================================================================
-- v1.20.0_add_block_continues_previous.sql
--
-- クレジットのブロックに、「前のブロックの続き」の印を持てるようにする。
--   continues_previous  TINYINT(1) NOT NULL DEFAULT 0
--
-- 用途：画面では同じまとまり（空行なし）なのに、途中の行だけ升の数が違うクレジット。
-- 列数（col_count）はブロックごとに 1 つなので、列数の違う行は別のブロックに分けて持ち、
-- 2 つ目のブロックにこの印を付ける。表示はブロックの間の空き（block-break）を入れず、前の行にそのまま続ける。
-- 役職の最初のブロック（block_seq = 1）には付けない。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_block_continues_previous;
DELIMITER $$
CREATE PROCEDURE _v1200_add_block_continues_previous()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'continues_previous'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `continues_previous` tinyint(1) NOT NULL DEFAULT 0
        COMMENT '1 なら前のブロックの続き。間に空きを入れず、列数だけ変えて続ける（役職の最初のブロックには付けない）'
        AFTER `col_count`;
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_block_continues_previous();
DROP PROCEDURE IF EXISTS _v1200_add_block_continues_previous;
