-- =====================================================================
-- v1.18.4_add_card_role_join.sql
--
-- クレジットの役職（credit_card_roles）を、画面どおり 1 行にまとめて表示できるようにする。
--   joined_label   VARCHAR(128) NULL     まとめた行の役職名の文字（画面の表記どおり）。
--                                         まとめる役職のうち先頭の役職に持たせる。
--   join_previous  TINYINT NOT NULL 0    1 なら直前の役職（同じグループの order_in_group 一つ前）と 1 行にまとめる。
--                                         まとめる役職のうち 2 つ目以降に立てる。
--
-- 画面で「キャラクターデザイン・作画監督　青山 充」のように 1 行で出る表記を、
-- データとしては CHARACTER_DESIGN と ANIMATION_DIRECTOR の 2 役職に同じエントリを入れて持つ。
-- まとめるのはクレジットの表示だけで、集計（人物ページ・シリーズ一覧・統計）は役職ごとに分かれたまま。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_card_role_join;
DELIMITER $$
CREATE PROCEDURE _v1184_add_card_role_join()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'joined_label'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `joined_label` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '後続の役職と 1 行にまとめて表示するときの役職名の文字（画面の表記どおり）。まとめる先頭の役職に持たせる'
        AFTER `affiliation_layout`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'join_previous'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `join_previous` tinyint NOT NULL DEFAULT 0
        COMMENT '1 なら直前の役職（同じグループの一つ前）と 1 行にまとめて表示する'
        AFTER `joined_label`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_card_role_join();
DROP PROCEDURE IF EXISTS _v1184_add_card_role_join;
