-- =====================================================================
-- v1.20.0_precures_key_color_gradient.sql
--
-- precures にキーカラーのグラデーション key_color_gradient を足す。
-- 1 色で表せないキーカラー（色見本が左から右へのグラデーションのプリキュア）用。
--   形式: 「#RRGGBB 位置%」をカンマと空白でつないだ色の並び（2 色以上、位置は 0〜100）
--         例: '#F1346E 1%, #FF65F8 19%, #8474FF 29%'
--   サイトは linear-gradient(to right, <この値>) としてそのまま CSS に入れ、
--   OGP カードの色帯も同じ並びで左から右へ塗る。
--   key_color（1 色）は残し、文字色の明るさの判定などグラデーションを描けない所で使う。
-- CSS に直接入るため、CHECK 制約で上の形式以外の文字列を通さない。
--
-- 冪等性: INFORMATION_SCHEMA で列・制約の有無を確かめてから ALTER を発行する。
-- 列の追加は DDL なのでトランザクションには入れない。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_key_color_gradient;
DELIMITER $$
CREATE PROCEDURE _v1200_add_key_color_gradient()
BEGIN
  IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
       WHERE TABLE_SCHEMA = DATABASE()
         AND TABLE_NAME   = 'precures'
         AND COLUMN_NAME  = 'key_color_gradient') = 0 THEN
    ALTER TABLE `precures`
      ADD COLUMN `key_color_gradient` varchar(512) DEFAULT NULL
        COMMENT 'キーカラーのグラデーション（「#RRGGBB 位置%」をカンマでつないだ左から右への色の並び、NULL=1 色のキーカラーだけ）'
        AFTER `key_color`;
  END IF;
  IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
       WHERE TABLE_SCHEMA    = DATABASE()
         AND TABLE_NAME      = 'precures'
         AND CONSTRAINT_NAME = 'ck_precures_key_color_gradient') = 0 THEN
    ALTER TABLE `precures`
      ADD CONSTRAINT `ck_precures_key_color_gradient`
        CHECK (`key_color_gradient` IS NULL
               OR (`key_color_gradient` REGEXP '^#[0-9A-Fa-f]{6} [0-9]{1,3}%(, #[0-9A-Fa-f]{6} [0-9]{1,3}%)+$'));
  END IF;
END$$
DELIMITER ;

CALL _v1200_add_key_color_gradient();

DROP PROCEDURE IF EXISTS _v1200_add_key_color_gradient;
