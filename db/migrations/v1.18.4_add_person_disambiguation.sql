-- =====================================================================
-- v1.18.4_add_person_disambiguation.sql
--
-- 同姓同名の別人を見分けるための添え書きを人物（persons）に持てるようにする。
--   disambiguation  VARCHAR(64) NULL  見分け用の添え書き（例:「声優」「背景美術」）。
--
-- 添え書きのある人物は、サイトの URL と名乗り（人物詳細の見出し・ページタイトル・検索・一覧の行表記）に
-- 「渡辺 久美子 (声優)」の形で添える（URL は /people/渡辺久美子_(声優)/）。クレジットの中の名前には付けない。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_person_disambiguation;
DELIMITER $$
CREATE PROCEDURE _v1184_add_person_disambiguation()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'disambiguation'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `disambiguation` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '同姓同名の別人を見分ける添え書き（例: 声優 / 背景美術）。サイトの URL と名乗りに添える'
        AFTER `name_en`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_person_disambiguation();
DROP PROCEDURE IF EXISTS _v1184_add_person_disambiguation;
