-- =====================================================================
-- v1.13.1_add_published_entity_slugs.sql
--
-- 人物 URL の公開記録 published_entity_slugs を作る。
--
-- 人物詳細ページは最新名義（全クレジット横断で最後に使われた名義）で名乗り、URL も
-- /people/{最新名義}/ で作る。最新名義はクレジットの入力が進むと変わるので、URL も変わる。
-- 本番に公開した URL をここに記録しておき、いまの URL と違う記録済みの旧 URL は
-- 新 URL へ 301 で転送する（SiteBuilder が転送表 _edge/legacy-redirects.json を作る）。
--
--   published_entity_slugs:
--     entity_kind  PERSON（いまは人物だけ。区分を持たせておくのは他の区分へ広げる余地のため）
--     slug         公開した URL のスラッグ（デコード済みの素の文字列。/people/{slug}/）。
--                  照合は完全一致にしたいので utf8mb4_bin（既定の照合順序だと「か」と「が」が同一視される）
--     person_id    その URL で公開した人物。ON UPDATE CASCADE で ID の振り直しに追従する。
--                  人物を統合するときは削除の前に統合先へ付け替える（付け替えずに削除すると
--                  ON DELETE CASCADE で行が消え、その旧 URL は 404 になる）。
--
-- 記録は SiteBuilder の本番デプロイ（--production --deploy）が成功したときだけ追記する（INSERT IGNORE、
-- 記録済みの行は変えない）。最初の記録（v1.13.0 で公開した /people/{正式名}/ の一覧）は別途投入する。
--
-- 冪等性: テーブルは IF NOT EXISTS。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `published_entity_slugs` (
  `entity_kind`  varchar(16) NOT NULL COMMENT 'PERSON',
  `slug`         varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '公開した URL のスラッグ（デコード済み）',
  `person_id`    int NOT NULL COMMENT 'その URL で公開した人物',
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT '最初に公開を記録した日時',
  PRIMARY KEY (`entity_kind`, `slug`),
  KEY `ix_pes_person` (`person_id`),
  CONSTRAINT `fk_pes_person` FOREIGN KEY (`person_id`) REFERENCES `persons` (`person_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='本番に公開した人物 URL の記録（旧名 URL の 301 転送用）';
