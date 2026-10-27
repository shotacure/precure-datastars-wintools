-- =====================================================================
-- v1.19.1_drop_books_price_kindle.sql
--
-- books から Kindle 版の税込価格 price_kindle_inc_tax と、その CHECK 制約
-- ck_books_price_kindle_nonneg を外す。
-- Kindle の価格は Amazon の売値で、Creators API 使用要件は画像でない内容の保存を最長 24 時間に限り、
-- 表示するなら取得時刻と免責の文を隣に出すよう求める。サイトは書籍の価格を出さないので持たない。
-- 紙の定価（price_ex_tax / price_inc_tax）は人手で入れる値なので残す。
--
-- 冪等性: INFORMATION_SCHEMA で制約・列の有無を確かめてから ALTER を発行する。
-- 列の削除は DDL なのでトランザクションには入れない。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_drop_books_price_kindle;
DELIMITER $$
CREATE PROCEDURE _v1191_drop_books_price_kindle()
BEGIN
  DECLARE v_ck INT DEFAULT 0;
  DECLARE v_col INT DEFAULT 0;

  -- 列を参照する CHECK 制約が残っていると列を消せないので、先に外す。
  SELECT COUNT(*) INTO v_ck
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
   WHERE CONSTRAINT_SCHEMA = DATABASE()
     AND TABLE_NAME        = 'books'
     AND CONSTRAINT_NAME   = 'ck_books_price_kindle_nonneg'
     AND CONSTRAINT_TYPE   = 'CHECK';
  IF v_ck > 0 THEN
    ALTER TABLE `books` DROP CHECK `ck_books_price_kindle_nonneg`;
  END IF;

  SELECT COUNT(*) INTO v_col
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'books'
     AND COLUMN_NAME  = 'price_kindle_inc_tax';
  IF v_col > 0 THEN
    ALTER TABLE `books` DROP COLUMN `price_kindle_inc_tax`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_drop_books_price_kindle();

DROP PROCEDURE IF EXISTS _v1191_drop_books_price_kindle;
