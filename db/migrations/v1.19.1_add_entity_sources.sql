-- =====================================================================
-- v1.19.1_add_entity_sources.sql
--
-- 人物・企業のプロフィール（読み・誕生日など）の出典を持つ表 entity_sources を足す。
--   1 行 = 1 人物または 1 企業 × 1 ページ。どちらか一方（person_id / company_id）だけを持つ。
--   supports      ：この出典で裏付ける項目。サイトの本文でその項目に脚注の番号を付ける
--                     KANA（読み）・BIRTH（誕生日）・DEATH（没年）は人物、KANA・FOUNDED（設立）・DISSOLVED（解散）・MEMBERS（メンバー）は企業
--   page_title    ：ページタイトル（ページの <title> を一字一句そのまま）
--   section_title ：情報が載っている欄の見出し（ページの表記どおり。無ければ NULL）
--   publisher     ：発行者（ページの著作権表記の名前どおり）
--   url           ：開いたページの URL（見出しへの # 付きでもよい）
--   checked_on    ：そのページで確かめた日
--   外部キーは RESTRICT（MySQL は CASCADE の外部キーの列を CHECK に使えないため）。出典のある人物・企業は、出典を消してから物理削除する。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `entity_sources` (
  `source_id`      int                                                                 NOT NULL AUTO_INCREMENT,
  `person_id`      int                                                                 DEFAULT NULL,
  `company_id`     int                                                                 DEFAULT NULL,
  `display_order`  smallint unsigned                                                   NOT NULL DEFAULT 0,
  `supports`       set('KANA','BIRTH','DEATH','FOUNDED','DISSOLVED','MEMBERS')         NOT NULL DEFAULT '' COMMENT 'この出典で裏付ける項目（本文の脚注の番号を付ける所）',
  `page_title`     varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT 'ページタイトル（<title> のまま）',
  `section_title`  varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT '情報が載っている欄の見出し（表記どおり）',
  `publisher`      varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '発行者（著作権表記の名前どおり）',
  `url`            varchar(1024)                                                       NOT NULL,
  `checked_on`     date                                                                NOT NULL COMMENT 'そのページで確かめた日',
  `notes`          text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64) DEFAULT NULL,
  `updated_by`     varchar(64) DEFAULT NULL,
  PRIMARY KEY (`source_id`),
  KEY `ix_entity_sources_person`  (`person_id`, `display_order`),
  KEY `ix_entity_sources_company` (`company_id`, `display_order`),
  CONSTRAINT `fk_entity_sources_person`  FOREIGN KEY (`person_id`)  REFERENCES `persons`   (`person_id`)  ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_entity_sources_company` FOREIGN KEY (`company_id`) REFERENCES `companies` (`company_id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `ck_entity_sources_target`  CHECK ((`person_id` IS NULL) <> (`company_id` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='人物・企業のプロフィールの出典';
