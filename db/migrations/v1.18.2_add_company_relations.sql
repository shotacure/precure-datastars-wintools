-- =====================================================================
-- v1.18.2_add_company_relations.sql
--
-- 企業・団体どうしの関係を持つ company_relations テーブルを新設する。
--
-- 関係は 2 種類で、どちらも「団体 → 団体」の向きと期間を持つ。
--   PARENT     ... from が親、to が子。持株会社と子会社、会社と部署・編集部・雑誌など。
--   SUCCESSOR  ... from の事業を to が引き継いだ。会社分割・合併・事業譲渡など、
--                  別の法人どうしの系譜。
-- 同じ会社の改名（社名変更・屋号変更）はこのテーブルでは持たず、従来どおり
-- company_aliases の predecessor_alias_id / successor_alias_id でつなぐ。
--
-- relation_label は表示の言い回し（「部署」「子会社」「雑誌」「会社分割」など）。
-- NULL のときは種類ごとの既定の言葉で表示する。
-- valid_from / valid_to は関係の期間。分からなければ NULL。
-- 自分自身を指す行は入力画面（Catalog）で作らせない。from / to は外部キーの連動（CASCADE）に
-- 使うため、MySQL の制約上 CHECK 制約には入れられない。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `company_relations` (
  `relation_id`      int                                                                NOT NULL AUTO_INCREMENT,
  `from_company_id`  int                                                                NOT NULL,
  `to_company_id`    int                                                                NOT NULL,
  `relation_kind`    enum('PARENT','SUCCESSOR')                                         NOT NULL,
  `relation_label`   varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `valid_from`       date DEFAULT NULL,
  `valid_to`         date DEFAULT NULL,
  `notes`            text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64) DEFAULT NULL,
  `updated_by`       varchar(64) DEFAULT NULL,
  PRIMARY KEY (`relation_id`),
  UNIQUE KEY `uq_company_relations` (`from_company_id`, `to_company_id`, `relation_kind`, `valid_from`),
  KEY `ix_company_relations_to` (`to_company_id`),
  CONSTRAINT `fk_company_relations_from` FOREIGN KEY (`from_company_id`) REFERENCES `companies` (`company_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_company_relations_to`   FOREIGN KEY (`to_company_id`)   REFERENCES `companies` (`company_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_company_relations_period`   CHECK (`valid_from` IS NULL OR `valid_to` IS NULL OR `valid_from` <= `valid_to`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='企業・団体どうしの関係（所属・事業の引き継ぎ）';

COMMIT;
