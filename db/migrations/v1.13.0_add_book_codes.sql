-- =====================================================================
-- v1.13.0_add_book_codes.sql
--
-- 書籍（books）に、ISBN 以外の流通コードを持たせる。
--
--   books + 3 列（いずれも任意）:
--     c_code           Cコード（分類コード。"C" + 数字 4 桁、例: C8776）。
--                      書籍 JAN の 2 段目（192 + Cコード + 本体価格）は Cコードと税抜価格から導けるため持たない。
--     magazine_code    雑誌コード（5 桁 + "-" + 月号 2 桁、例: 66557-17）。ムック・増刊・別冊向け。
--                      月号に年を含まず年ごとに同じ値が巡るため一意にはしない。
--     periodical_code  定期刊行物コード（雑誌 JAN）。"491" で始まる 13 桁 + 価格アドオン 5 桁の 18 桁（区切り無し）。
--
-- 書籍詳細ページの URL は ISBN-13 → 定期刊行物コード → Kindle ASIN → 紙の ASIN の順に最初にあるコードを使う。
--
-- 冪等性: 列・索引の状態を確認してから追加する。
-- =====================================================================

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'books'
       AND COLUMN_NAME  = 'c_code'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `books`
        ADD COLUMN `c_code` char(5) DEFAULT NULL
            COMMENT ''Cコード（"C" + 4 桁、例: C8776）''
            AFTER `isbn13`;',
    'SELECT ''books.c_code already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'books'
       AND COLUMN_NAME  = 'magazine_code'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `books`
        ADD COLUMN `magazine_code` varchar(8) DEFAULT NULL
            COMMENT ''雑誌コード（5 桁-月号、例: 66557-17）''
            AFTER `c_code`;',
    'SELECT ''books.magazine_code already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'books'
       AND COLUMN_NAME  = 'periodical_code'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `books`
        ADD COLUMN `periodical_code` varchar(18) DEFAULT NULL
            COMMENT ''定期刊行物コード（雑誌 JAN、491 始まり 18 桁）''
            AFTER `magazine_code`;',
    'SELECT ''books.periodical_code already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @uq_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'books'
       AND INDEX_NAME   = 'uq_books_periodical_code'
);
SET @ddl := IF(@uq_exists = 0,
    'ALTER TABLE `books` ADD UNIQUE KEY `uq_books_periodical_code` (`periodical_code`);',
    'SELECT ''uq_books_periodical_code already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
