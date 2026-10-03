-- =====================================================================
-- v1.18.4_add_person_notable_works.sql
--
-- 人物の「プリキュア以外の代表作」を持つ person_notable_works テーブルを新設する。
--
-- 1 行 = 1 人物 × 1 作品 × 1 役職。作品はプリキュアシリーズの外のものなので DB の作品マスタには
-- つながず、作品名・役職をテキストで持つ。対象は作品の顔になる役職（監督・シリーズディレクター・
-- キャラクターデザイン・シリーズ構成・プロデューサー）。
--
-- official_url はサイトでリンクする先＝作品の公式サイト。公式サイトが閉鎖されていて
-- Internet Archive に残る公式サイトで確かめたときは、そのアーカイブの URL を入れて
-- official_url_is_archive を 1 にする（サイトではアーカイブへのリンクだと分かるように出す）。
-- source_url は裏取りに使ったスタッフ表のページ（内部用。サイトには出さない）。
-- year_from / year_to は時期（年）。1 年だけなら year_to は NULL。
-- display_order は人物ごとの並び順（小さい順。同じなら year_from 順）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `person_notable_works` (
  `work_id`                  int                                                                 NOT NULL AUTO_INCREMENT,
  `person_id`                int                                                                 NOT NULL,
  `display_order`            smallint unsigned                                                   NOT NULL DEFAULT 0,
  `work_title`               varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `role_label`               varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `year_from`                smallint unsigned DEFAULT NULL,
  `year_to`                  smallint unsigned DEFAULT NULL,
  `official_url`             varchar(1024) DEFAULT NULL COMMENT '作品の公式サイト（サイトでリンクする先）',
  `official_url_is_archive`  tinyint(1) NOT NULL DEFAULT 0 COMMENT '1 なら official_url は閉鎖済み公式サイトのアーカイブ',
  `source_url`               varchar(1024) DEFAULT NULL COMMENT '裏取りに使ったスタッフ表のページ（内部用）',
  `notes`                    text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`               timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`               timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`               varchar(64) DEFAULT NULL,
  `updated_by`               varchar(64) DEFAULT NULL,
  PRIMARY KEY (`work_id`),
  KEY `ix_person_notable_works_person` (`person_id`, `display_order`),
  CONSTRAINT `fk_person_notable_works_person` FOREIGN KEY (`person_id`) REFERENCES `persons` (`person_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_person_notable_works_years`  CHECK (`year_to` IS NULL OR (`year_from` IS NOT NULL AND `year_from` <= `year_to`)),
  CONSTRAINT `ck_person_notable_works_archive` CHECK (`official_url_is_archive` IN (0, 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='人物のプリキュア以外の代表作';

COMMIT;
