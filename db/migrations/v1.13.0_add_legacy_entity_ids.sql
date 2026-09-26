-- =====================================================================
-- v1.13.0_add_legacy_entity_ids.sql
--
-- 旧 ID URL の台帳 legacy_entity_ids を作り、現時点の ID を凍結して登録する。
--
-- サイトの人物・キャラクター・企業・書籍の詳細ページ URL は、v1.13.0 から ID ではなく
-- 名前（書籍はコード）で作る。公開済みの旧 URL（/persons/123/ 等）は 301 で新 URL へ転送するが、
-- その「旧 ID → 実体」の対応は URL を切り替えた時点の ID で固定しておく必要がある
-- （以後 ID を振り直しても、旧 URL は元の実体を指し続けなければならない）。
--
--   legacy_entity_ids:
--     entity_kind   PERSON / CHARACTER / COMPANY / BOOK
--     legacy_id     公開済みの旧 URL に出ていた ID（凍結値。以後変えない）
--     person_id / character_id / company_id / book_id
--                   いまの実体。entity_kind に対応する 1 列だけを持つ（MySQL は参照アクション付きの列を
--                   CHECK に使えないため制約では縛らず、登録側で守る）。
--                   ON UPDATE CASCADE なので ID を振り直すと一緒に動く。
--                   ただし FOREIGN_KEY_CHECKS=0 で振り直すときは CASCADE が働かないため、
--                   振り直しスクリプト側でこの列も明示的に更新すること。
--                   実体を別の実体へ統合するときは、削除の前にこの列を統合先へ付け替える
--                   （付け替えずに削除すると ON DELETE CASCADE で行が消え、旧 URL は 404 になる）。
--
-- 冪等性: テーブルは IF NOT EXISTS、登録は INSERT IGNORE（既存行は上書きしない）。
-- 登録は必ず ID の振り直しより前に 1 度流すこと（振り直し後に流すと、ずれた ID で凍結される）。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `legacy_entity_ids` (
  `entity_kind`   varchar(16) NOT NULL COMMENT 'PERSON / CHARACTER / COMPANY / BOOK',
  `legacy_id`     int NOT NULL COMMENT '公開済みの旧 URL に出ていた ID（凍結値）',
  `person_id`     int DEFAULT NULL,
  `character_id`  int DEFAULT NULL,
  `company_id`    int DEFAULT NULL,
  `book_id`       int DEFAULT NULL,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`entity_kind`, `legacy_id`),
  KEY `ix_lei_person`    (`person_id`),
  KEY `ix_lei_character` (`character_id`),
  KEY `ix_lei_company`   (`company_id`),
  KEY `ix_lei_book`      (`book_id`),
  CONSTRAINT `fk_lei_person`    FOREIGN KEY (`person_id`)    REFERENCES `persons` (`person_id`)       ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_lei_character` FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_lei_company`   FOREIGN KEY (`company_id`)   REFERENCES `companies` (`company_id`)   ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_lei_book`      FOREIGN KEY (`book_id`)      REFERENCES `books` (`book_id`)          ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='旧 ID URL の台帳（URL 切り替え時点の ID を凍結）';

-- 現時点の ID を凍結して登録する（論理削除済みの行は公開されていないので除く）。
INSERT IGNORE INTO `legacy_entity_ids` (`entity_kind`, `legacy_id`, `person_id`)
SELECT 'PERSON', `person_id`, `person_id` FROM `persons` WHERE `is_deleted` = 0;
INSERT IGNORE INTO `legacy_entity_ids` (`entity_kind`, `legacy_id`, `character_id`)
SELECT 'CHARACTER', `character_id`, `character_id` FROM `characters` WHERE `is_deleted` = 0;
INSERT IGNORE INTO `legacy_entity_ids` (`entity_kind`, `legacy_id`, `company_id`)
SELECT 'COMPANY', `company_id`, `company_id` FROM `companies` WHERE `is_deleted` = 0;
INSERT IGNORE INTO `legacy_entity_ids` (`entity_kind`, `legacy_id`, `book_id`)
SELECT 'BOOK', `book_id`, `book_id` FROM `books` WHERE `is_deleted` = 0;
