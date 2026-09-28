-- =====================================================================
-- v1.14.1_add_products_music_credits_checked.sql
--
-- 盤ごとに「クレジット確認済み」の印を持てるようにする。
--
--   products + 1 列:
--     music_credits_checked  この盤のクレジット（音盤の音楽クレジット・楽曲クレジット）を確認・投入済みなら 1。
--                            クレジット情報が載っていないと確認できた盤も 1。サイトの音楽系ページの
--                            「yyyy年M月d日発売「商品名」時点の情報」の基準点（1 の盤のうち発売日が最も新しいもの）に使う。
--
-- 冪等性: 列の存在を確認してから追加する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1141_products_music_credits_checked;
DELIMITER $$
CREATE PROCEDURE _v1141_products_music_credits_checked()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
                    AND COLUMN_NAME = 'music_credits_checked') THEN
    ALTER TABLE `products`
      ADD COLUMN `music_credits_checked` tinyint(1) NOT NULL DEFAULT 0
        COMMENT 'クレジット確認済みなら 1（音楽系ページの基準点に使う）'
        AFTER `product_kind_code`;
  END IF;
END$$
DELIMITER ;
CALL _v1141_products_music_credits_checked();
DROP PROCEDURE IF EXISTS _v1141_products_music_credits_checked;
