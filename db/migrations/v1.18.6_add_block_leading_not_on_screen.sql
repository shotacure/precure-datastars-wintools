-- =====================================================================
-- v1.18.6_add_block_leading_not_on_screen.sql
--
-- クレジットのブロックの筆頭の会社（leading_company_alias_id）に、「画面には出ていない」印を持てるようにする。
--   leading_company_not_on_screen  TINYINT NOT NULL DEFAULT 0
--
-- 用途：役職と人の名前は画面に出ているのに、ふだん筆頭に出る会社（「デジタル撮影」の三晃プロダクションなど）だけが
-- その回の画面から抜けているクレジット漏れ。会社はデータとして入れて所属・集計に数え、表示だけ「（三晃プロダクション）」と
-- 括弧で包んで薄い色で示す（役職の行ごとの not_on_screen と同じ考え方を、筆頭の会社だけに当てる）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_add_block_leading_not_on_screen;
DELIMITER $$
CREATE PROCEDURE _v1186_add_block_leading_not_on_screen()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks' AND COLUMN_NAME = 'leading_company_not_on_screen'
  ) THEN
    ALTER TABLE `credit_role_blocks`
      ADD COLUMN `leading_company_not_on_screen` tinyint NOT NULL DEFAULT 0
        COMMENT '1 なら筆頭の会社は画面にはクレジットされていない（クレジット漏れ）。表示だけ括弧で包む'
        AFTER `leading_company_alias_id`;
  END IF;
END$$
DELIMITER ;

CALL _v1186_add_block_leading_not_on_screen();
DROP PROCEDURE IF EXISTS _v1186_add_block_leading_not_on_screen;
