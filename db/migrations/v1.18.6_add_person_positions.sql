-- =====================================================================
-- v1.18.6_add_person_positions.sql
--
-- 人物の「役職履歴」（会社での役職の移り変わり）を持つ person_positions テーブルを新設する。
--
-- 1 行 = 1 人物の 1 回の異動（その日付で就いた役職）。人物詳細に「役職履歴（会社名）」の見出しで
-- 「異動日 ｜ 役職」の表を出す（クレジットの役職と紛れないよう、見出しに会社名を添える）。
-- company_id はその役職の会社（任意。見出しの会社名から企業詳細へリンクする）。
-- effective_date は異動日。NULL は日付不明（サイトでは「？」と出す）。
-- position_text は役職の文字（発表の表記どおり。「執行役員 企画製作本部 テレビ企画部長 兼 映画室長」など）。
-- display_order は人物ごとの並び順（小さい順。同じなら異動日順）。日付不明の行も並び順で正しい位置に置く。
-- source_url は裏取りに使った発表のページ（内部用。サイトには出さない）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `person_positions` (
  `position_id`     int                                                                 NOT NULL AUTO_INCREMENT,
  `person_id`       int                                                                 NOT NULL,
  `company_id`      int                                                                 DEFAULT NULL COMMENT 'その役職の会社（companies.company_id）',
  `display_order`   smallint unsigned                                                   NOT NULL DEFAULT 0,
  `effective_date`  date                                                                DEFAULT NULL COMMENT '異動日。NULL は日付不明',
  `position_text`   varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '役職（発表の表記どおり）',
  `source_url`      varchar(1024) DEFAULT NULL COMMENT '裏取りに使った発表のページ（内部用）',
  `notes`           text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64) DEFAULT NULL,
  `updated_by`      varchar(64) DEFAULT NULL,
  PRIMARY KEY (`position_id`),
  KEY `ix_person_positions_person` (`person_id`, `display_order`),
  KEY `ix_person_positions_company` (`company_id`),
  CONSTRAINT `fk_person_positions_person`  FOREIGN KEY (`person_id`)  REFERENCES `persons`   (`person_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_person_positions_company` FOREIGN KEY (`company_id`) REFERENCES `companies` (`company_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='人物の役職履歴（会社での役職の移り変わり）';
