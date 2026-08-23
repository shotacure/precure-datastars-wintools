-- =====================================================================
-- v1.9.1_add_book_original_author_role.sql
--
-- 書籍役職マスタに「原作」（ORIGINAL_AUTHOR）を追加する。
--
-- v1.9.0 の初期データには 著 / 監修 / 編集 …… は入れたが 原作 が抜けていた。
-- コミカライズは原作者（プリキュアなら東堂いづみ名義）が必ずクレジットされる形式で、
-- Amazon Creators API も contributors[].roleType = "original_author"（表示は「原著」）で
-- 返してくる。マスタに無いと取り込みが OTHER（その他）へ落ちてしまうため補う。
--
-- 表示順は「著 → 原作 → 監修 → 編集 …」が自然なので 2 番へ差し込み、以降を 1 つずつ後ろへ
-- ずらす。display_order は UNIQUE なので、衝突しないよう大きい番号から順に更新する。
--
-- 冪等性: ORIGINAL_AUTHOR が既に存在する場合は並べ替えごとスキップする（一時 procedure で分岐）。
-- =====================================================================

DROP PROCEDURE IF EXISTS `tmp_add_original_author_role`;
DELIMITER $$
CREATE PROCEDURE `tmp_add_original_author_role`()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM `book_credit_roles` WHERE `role_code` = 'ORIGINAL_AUTHOR') THEN
    -- 2 番を空けるため、既存の 2〜10 番を大きい方から 1 つずつ後ろへずらす。
    UPDATE `book_credit_roles` SET `display_order` = 11 WHERE `role_code` = 'PLANNER';
    UPDATE `book_credit_roles` SET `display_order` = 10 WHERE `role_code` = 'TRANSLATOR';
    UPDATE `book_credit_roles` SET `display_order` =  9 WHERE `role_code` = 'PHOTOGRAPHER';
    UPDATE `book_credit_roles` SET `display_order` =  8 WHERE `role_code` = 'DESIGNER';
    UPDATE `book_credit_roles` SET `display_order` =  7 WHERE `role_code` = 'COVER_ILLUST';
    UPDATE `book_credit_roles` SET `display_order` =  6 WHERE `role_code` = 'ILLUSTRATOR';
    UPDATE `book_credit_roles` SET `display_order` =  5 WHERE `role_code` = 'WRITER';
    UPDATE `book_credit_roles` SET `display_order` =  4 WHERE `role_code` = 'EDITOR';
    UPDATE `book_credit_roles` SET `display_order` =  3 WHERE `role_code` = 'SUPERVISOR';

    INSERT INTO `book_credit_roles` (`role_code`,`name_ja`,`name_en`,`amazon_role_type`,`display_order`)
    VALUES ('ORIGINAL_AUTHOR', '原作', 'Original Author', 'original_author', 2);
  END IF;
END$$
DELIMITER ;

CALL `tmp_add_original_author_role`();
DROP PROCEDURE `tmp_add_original_author_role`;

SELECT `role_code`, `name_ja`, `amazon_role_type`, `display_order`
FROM `book_credit_roles` ORDER BY `display_order`;
