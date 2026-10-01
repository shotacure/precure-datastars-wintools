-- =====================================================================
-- v1.15.0_add_persons_affiliation_url.sql
--
-- 人物の所属先（事務所・会社・楽団など）のサイトにある本人のプロフィールページの URL を持つ列を
-- persons に追加する。
--   affiliation_url  varchar(1024) NULL  所属先のプロフィールページ URL。無い人・分からない人は NULL
--
-- 本人の公式サイト（official_url）とは別に持ち、両方ある人物はどちらも出す。サイトの人物詳細の
-- 末尾「外部リンク」セクションに「所属先」のバッジとして、公式ページの次に並べる。
-- 所属先が変わったときは、この列だけを新しい所属先のページに書き換える。
--
-- あわせて official_url の列の説明（COMMENT）を「本人の公式サイト URL」にそろえる（型・既定値は変えない）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在・説明を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1150_add_persons_affiliation_url;
DELIMITER $$
CREATE PROCEDURE _v1150_add_persons_affiliation_url()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'affiliation_url'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `affiliation_url` varchar(1024) DEFAULT NULL
        COMMENT '所属先（事務所・会社・楽団など）のサイトにある本人のプロフィールページ URL'
        AFTER `official_url`;
  END IF;

  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'official_url'
       AND COLUMN_COMMENT <> '本人の公式サイト URL（詳細ページに外部リンクとして表示）'
  ) THEN
    ALTER TABLE `persons`
      MODIFY COLUMN `official_url` varchar(1024) DEFAULT NULL
        COMMENT '本人の公式サイト URL（詳細ページに外部リンクとして表示）';
  END IF;
END$$
DELIMITER ;

CALL _v1150_add_persons_affiliation_url();
DROP PROCEDURE IF EXISTS _v1150_add_persons_affiliation_url;
