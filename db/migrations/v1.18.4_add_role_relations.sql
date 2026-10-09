-- =====================================================================
-- v1.18.4_add_role_relations.sql
--
-- 別の役職どうしの関連（段階・並列）を持つ role_relations テーブルを新設する。
--
-- 役職の系譜（role_successions）は「同じ役職の名前が変わった」ことを表し、集計を 1 つにまとめる。
-- こちらは別の役職どうしの関係で、集計は分けたまま、人物の歩み（演出助手を経て演出を初担当、など）と
-- 役職詳細の年表に関連する役職の担当を重ねて描くために使う。
--
--   relation_kind = 'STEP_UP'  ... 段階。from_role_code が前段階、to_role_code が後段階
--                                  （例：演出助手 → 演出、動画 → 原画）。向きを持つ。
--   relation_kind = 'PARALLEL' ... 並列。同じ段階で並んで担う役職（例：絵コンテ ⇔ 演出）。向きを持たないので、
--                                  role_code の小さいほうを from_role_code にそろえて 1 行で持つ
--                                  （RoleRelationsRepository.UpsertAsync が並べ替える）。
--
-- 1 組の役職には関係を 1 つだけ持つ（PK は from / to の組。段階と並列を同じ組に重ねない）。
-- 系譜でまとめた役職は、使う側（SiteBuilder）で系譜の代表へ寄せてから関係を引く。
--
-- 自己ループ（from = to）は role_successions と同じ理由（MySQL 8 の Error 3823：FK の参照アクションで
-- 変わる列を CHECK で参照できない）で DB 制約にせず、アプリ層（RoleRelationsRepository.UpsertAsync）で弾く。
--
-- 役職コードの列は、稼働中の roles.role_code（varchar(64)・テーブル既定の照合順序）と同じ型にする
-- （外部キーは型・照合順序がそろっていないと張れない）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `role_relations` (
  `from_role_code` varchar(64) NOT NULL COMMENT '段階なら前段階の役職。並列なら role_code の小さいほう',
  `to_role_code`   varchar(64) NOT NULL COMMENT '段階なら後段階の役職。並列なら role_code の大きいほう',
  `relation_kind`  enum('STEP_UP','PARALLEL') NOT NULL COMMENT 'STEP_UP=段階（from→to）、PARALLEL=並列（向きなし）',
  `notes`          text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64) DEFAULT NULL,
  `updated_by`     varchar(64) DEFAULT NULL,
  PRIMARY KEY (`from_role_code`, `to_role_code`),
  KEY `idx_role_relations_to` (`to_role_code`),
  CONSTRAINT `fk_role_relations_from` FOREIGN KEY (`from_role_code`) REFERENCES `roles` (`role_code`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_role_relations_to`   FOREIGN KEY (`to_role_code`)   REFERENCES `roles` (`role_code`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='別の役職どうしの関連（段階・並列）';

COMMIT;

SELECT 'v1.18.4 migration completed: role_relations' AS final_status;
