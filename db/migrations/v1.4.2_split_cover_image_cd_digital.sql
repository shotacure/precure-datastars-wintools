-- v1.4.2: ジャケット画像を CD / デジタルの 2 系統で保持できるようにする。
--   CD とデジタルでジャケットが異なる場合があるため、単一の cover_image_url を
--   cover_image_url_cd / cover_image_url_digital の 2 列に分割し、表示に使う方（代表）は
--   cover_image_source（'amazon_cd' / 'amazon_digital' / NULL）で明示選択する方式へ移行する。
--   さらに「商品詳細ページで CD・デジタル両方を並べて表示するか」を商品ごとに制御する
--   cover_image_show_both（TINYINT, 既定 0）を追加する。
--   本マイグレーションは以下 4 段で構成（いずれも冪等）：
--     1) cover_image_url_cd / cover_image_url_digital 列を追加（cover_image_url の後）。
--     2) 既存 cover_image_url を、その行の cover_image_source に応じて該当列へ移送。
--        （source='amazon_cd' なら _cd 列へ、'amazon_digital' なら _digital 列へ。
--         source が NULL かつ URL ありの行は、現行の既定優先がデジタルのため _digital 列へ寄せ、
--         source も 'amazon_digital' に確定させる。）
--     3) cover_image_show_both 列を追加（既定 0＝代表 1 枚のみ表示）。
--     4) 旧 cover_image_url 列を撤去。
--   INFORMATION_SCHEMA.COLUMNS 経由の存在チェックでガードし、再実行しても安全に空動作する。

-- ── 1) 新 2 列を追加（冪等） ──
DROP PROCEDURE IF EXISTS _tmp_add_cover_cols;
DELIMITER //
CREATE PROCEDURE _tmp_add_cover_cols()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
           AND COLUMN_NAME = 'cover_image_url_cd'
    ) THEN
        ALTER TABLE `products`
            ADD COLUMN `cover_image_url_cd` varchar(512) DEFAULT NULL AFTER `cover_image_url`;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
           AND COLUMN_NAME = 'cover_image_url_digital'
    ) THEN
        ALTER TABLE `products`
            ADD COLUMN `cover_image_url_digital` varchar(512) DEFAULT NULL AFTER `cover_image_url_cd`;
    END IF;
END //
DELIMITER ;
CALL _tmp_add_cover_cols();
DROP PROCEDURE _tmp_add_cover_cols;

-- ── 2) 既存 cover_image_url を該当列へ移送（旧列が残っているときのみ実行） ──
DROP PROCEDURE IF EXISTS _tmp_migrate_cover_url;
DELIMITER //
CREATE PROCEDURE _tmp_migrate_cover_url()
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
           AND COLUMN_NAME = 'cover_image_url'
    ) THEN
        -- CD 由来は _cd 列へ。
        UPDATE `products`
           SET `cover_image_url_cd` = `cover_image_url`
         WHERE `cover_image_url` IS NOT NULL
           AND `cover_image_source` = 'amazon_cd';
        -- デジタル由来は _digital 列へ。
        UPDATE `products`
           SET `cover_image_url_digital` = `cover_image_url`
         WHERE `cover_image_url` IS NOT NULL
           AND `cover_image_source` = 'amazon_digital';
        -- source 未設定だが URL ありの行はデジタル側へ寄せ、source も確定させる。
        UPDATE `products`
           SET `cover_image_url_digital` = `cover_image_url`,
               `cover_image_source`      = 'amazon_digital'
         WHERE `cover_image_url` IS NOT NULL
           AND (`cover_image_source` IS NULL OR `cover_image_source` = '');
    END IF;
END //
DELIMITER ;
CALL _tmp_migrate_cover_url();
DROP PROCEDURE _tmp_migrate_cover_url;

-- ── 3) cover_image_show_both 列を追加（冪等、既定 0） ──
DROP PROCEDURE IF EXISTS _tmp_add_show_both;
DELIMITER //
CREATE PROCEDURE _tmp_add_show_both()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
           AND COLUMN_NAME = 'cover_image_show_both'
    ) THEN
        ALTER TABLE `products`
            ADD COLUMN `cover_image_show_both` tinyint(1) NOT NULL DEFAULT 0 AFTER `cover_image_source`;
    END IF;
END //
DELIMITER ;
CALL _tmp_add_show_both();
DROP PROCEDURE _tmp_add_show_both;

-- ── 4) 旧 cover_image_url 列を撤去（冪等） ──
DROP PROCEDURE IF EXISTS _tmp_drop_cover_url;
DELIMITER //
CREATE PROCEDURE _tmp_drop_cover_url()
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'
           AND COLUMN_NAME = 'cover_image_url'
    ) THEN
        ALTER TABLE `products` DROP COLUMN `cover_image_url`;
    END IF;
END //
DELIMITER ;
CALL _tmp_drop_cover_url();
DROP PROCEDURE _tmp_drop_cover_url;
