-- =====================================================================
-- v1.13.4_add_published_character_slugs.sql
--
-- URL の公開記録 published_entity_slugs でキャラクターの URL も記録できるようにする。
--
-- キャラクター詳細ページの URL はキャラ名から作る（/characters/{名前}/）ので、キャラ名を変えると
-- URL も変わる。本番に公開したキャラ URL をここに記録しておき、いまの URL と違う記録済みの旧 URL は
-- 新 URL へ 301 で転送する（人物の旧名 URL と同じ仕組み。SiteBuilder が転送表 _edge/legacy-redirects.json を作る）。
--
--   published_entity_slugs:
--     entity_kind   PERSON / CHARACTER
--     person_id     entity_kind = PERSON の行だけ持つ（NULL 可に変更）
--     character_id  entity_kind = CHARACTER の行だけ持つ。ON UPDATE CASCADE で ID の振り直しに追従する。
--                   キャラを統合するときは削除の前に統合先へ付け替える（付け替えずに削除すると
--                   ON DELETE CASCADE で行が消え、その旧 URL は 404 になる）。
--     区分に応じて person_id / character_id のどちらか一方だけを持つ。両列とも参照先の ON DELETE / ON UPDATE CASCADE を
--     持つため MySQL では CHECK 制約にできず、整合は書き込み側（SiteBuilder の記録処理・初回投入）で守る。
--
-- 記録は SiteBuilder の本番デプロイ（--production --deploy）が成功したときだけ追記する（INSERT IGNORE）。
-- 最初の記録（これまでに公開したキャラ URL の一覧）は別途投入する。
--
-- 冪等性: 列・インデックス・外部キーは INFORMATION_SCHEMA で存在を確かめてから足す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1134_published_character_slugs;
DELIMITER $$
CREATE PROCEDURE _v1134_published_character_slugs()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND COLUMN_NAME = 'character_id') THEN
    ALTER TABLE `published_entity_slugs`
      MODIFY COLUMN `entity_kind` varchar(16) NOT NULL COMMENT 'PERSON / CHARACTER',
      MODIFY COLUMN `person_id` int DEFAULT NULL COMMENT 'その URL で公開した人物（PERSON の行のみ）',
      ADD COLUMN `character_id` int DEFAULT NULL COMMENT 'その URL で公開したキャラクター（CHARACTER の行のみ）' AFTER `person_id`;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND INDEX_NAME = 'ix_pes_character') THEN
    ALTER TABLE `published_entity_slugs` ADD KEY `ix_pes_character` (`character_id`);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND CONSTRAINT_NAME = 'fk_pes_character') THEN
    ALTER TABLE `published_entity_slugs`
      ADD CONSTRAINT `fk_pes_character` FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`)
        ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;

  ALTER TABLE `published_entity_slugs` COMMENT = '本番に公開した人物・キャラクター URL の記録（旧名 URL の 301 転送用）';
END$$
DELIMITER ;

CALL _v1134_published_character_slugs();
DROP PROCEDURE IF EXISTS _v1134_published_character_slugs;
