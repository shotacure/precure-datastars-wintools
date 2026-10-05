-- =====================================================================
-- v1.18.4_add_unit_published_slugs.sql
--
-- ユニット（人物の行を持たない名義。ぷりきゅあ5・ヤング・フレッシュなど）の詳細ページ /units/{名前}/ を作るのに合わせ、
-- 本番で公開した URL の記録（published_entity_slugs）にユニットの欄を足す。
--   person_alias_id  INT NULL   その URL で公開したユニットの名義（entity_kind = 'UNIT' の行のみ）。
--
-- 人物・キャラ・企業と同じく、名前が変わって URL が変わったら、記録した旧 URL から新しい URL へ 301 で転送する。
--
-- 冪等性: 列・索引・外部キーごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_unit_published_slugs;
DELIMITER $$
CREATE PROCEDURE _v1184_add_unit_published_slugs()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND COLUMN_NAME = 'person_alias_id'
  ) THEN
    ALTER TABLE `published_entity_slugs`
      ADD COLUMN `person_alias_id` int DEFAULT NULL
        COMMENT 'その URL で公開したユニットの名義（UNIT の行のみ）'
        AFTER `company_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND INDEX_NAME = 'ix_pes_person_alias'
  ) THEN
    ALTER TABLE `published_entity_slugs` ADD KEY `ix_pes_person_alias` (`person_alias_id`);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND CONSTRAINT_NAME = 'fk_pes_person_alias'
  ) THEN
    ALTER TABLE `published_entity_slugs`
      ADD CONSTRAINT `fk_pes_person_alias` FOREIGN KEY (`person_alias_id`)
        REFERENCES `person_aliases` (`alias_id`) ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_unit_published_slugs();
DROP PROCEDURE IF EXISTS _v1184_add_unit_published_slugs;
