-- =====================================================================
-- v1.16.1_add_published_company_slugs.sql
--
-- URL の公開記録 published_entity_slugs で企業の URL も記録できるようにする。
--
-- 企業詳細ページの URL は企業の正式名から作る（/companies/{正式名}/）ので、正式名を直すと URL も変わる。
-- 本番に公開した企業 URL をここに記録しておき、いまの URL と違う記録済みの旧 URL は
-- 新 URL へ 301 で転送する（人物・キャラクターの旧名 URL と同じ仕組み。SiteBuilder が転送表 _edge/legacy-redirects.json を作る）。
--
--   published_entity_slugs:
--     entity_kind   PERSON / CHARACTER / COMPANY
--     company_id    entity_kind = COMPANY の行だけ持つ。ON UPDATE CASCADE で ID の振り直しに追従する。
--                   企業を統合するときは削除の前に統合先へ付け替える（付け替えずに削除すると
--                   ON DELETE CASCADE で行が消え、その旧 URL は 404 になる）。
--
-- 記録は SiteBuilder の本番デプロイ（--production --deploy）が成功したときだけ追記する（INSERT IGNORE）。
--
-- 冪等性: 列・インデックス・外部キーは INFORMATION_SCHEMA で存在を確かめてから足す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1161_published_company_slugs;
DELIMITER $$
CREATE PROCEDURE _v1161_published_company_slugs()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND COLUMN_NAME = 'company_id') THEN
    ALTER TABLE `published_entity_slugs`
      MODIFY COLUMN `entity_kind` varchar(16) NOT NULL COMMENT 'PERSON / CHARACTER / COMPANY',
      ADD COLUMN `company_id` int DEFAULT NULL COMMENT 'その URL で公開した企業（COMPANY の行のみ）' AFTER `character_id`;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND INDEX_NAME = 'ix_pes_company') THEN
    ALTER TABLE `published_entity_slugs` ADD KEY `ix_pes_company` (`company_id`);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND CONSTRAINT_NAME = 'fk_pes_company') THEN
    ALTER TABLE `published_entity_slugs`
      ADD CONSTRAINT `fk_pes_company` FOREIGN KEY (`company_id`) REFERENCES `companies` (`company_id`)
        ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;

  ALTER TABLE `published_entity_slugs` COMMENT = '本番に公開した人物・キャラクター・企業 URL の記録（旧名 URL の 301 転送用）';
END$$
DELIMITER ;

CALL _v1161_published_company_slugs();
DROP PROCEDURE IF EXISTS _v1161_published_company_slugs;
