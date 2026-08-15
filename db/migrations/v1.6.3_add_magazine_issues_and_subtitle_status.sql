-- =====================================================================
-- v1.6.3_add_magazine_issues_and_subtitle_status.sql
--
-- アニメ雑誌でのサブタイトル掲載状態を扱うための 3 変更。
--
-- 1) magazine_issues テーブル新設
--    アニメ雑誌の「号」マスタ。各誌の発売日はほぼ横並びなので誌名は持たず、
--    (号の年, 号の月) を複合 PK に、実際の発売日 1 つを代表値として持つ。
--    ある号がサブタイトルを掲載する対象は「その号の発売日 〜 次号の発売日の前日」に
--    放送されるエピソード（日曜発売は無いため境界の同日競合は考えない）。
--    エピソード → 号の対応は放送日から一意に導出できるため、エピソード側には持たせない。
--    次号の発売予定日は事前に判明するため先行登録する運用（最新号のカバー範囲を
--    「次号発売日」で閉じられるようにする）。
--
-- 2) episodes.magazine_subtitle_status 列追加
--    アニメ雑誌でのサブタイトル掲載状態。NULL = データなし（サイト非表示）。
--      PUBLISHED     ... 掲載（誌面にサブタイトルが載った）
--      NOT_DISCLOSED ... 非公開（誌面で「サブタイトル非公開」と案内された）
--      UNDECIDED     ... 未定（誌面で「未定」と案内された）
--    サイト表示は「状態が非 NULL」かつ「放送日が連続する 2 つの発売日に挟まれている」
--    場合のみ（最新号より後の放送は次号未登録＝号未確定なので表示しない）。
--
-- 3) episodes.title_text の NULL 許容化 + 整合性 CHECK
--    サブタイトル未確定（放送予定だけ確定している）状態のエピソードを登録できるよう
--    title_text を NULL 許容にする。ただし「サブタイトル無し」を許すのは誌面根拠が
--    ある場合（非公開 / 未定）だけで、以下の CHECK で担保する：
--      title_text IS NULL → magazine_subtitle_status ∈ (NOT_DISCLOSED, UNDECIDED)
--    （掲載 PUBLISHED やデータなし NULL でサブタイトル無しはエラー）
--
-- 冪等性: テーブルは CREATE TABLE IF NOT EXISTS、列・制約は INFORMATION_SCHEMA で
-- 存在確認してから ALTER する。v1.5.14_add_episode_special_trailer_url.sql
-- のスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 1) アニメ雑誌の号マスタ
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `magazine_issues` (
  `issue_year`   smallint unsigned NOT NULL COMMENT '号の年（「2026年9月号」の 2026）',
  `issue_month`  tinyint unsigned  NOT NULL COMMENT '号の月（「2026年9月号」の 9）',
  `release_date` date              NOT NULL COMMENT '実際の発売日（各誌横並びの代表日）',
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`issue_year`, `issue_month`),
  UNIQUE KEY `uq_magazine_issues_release_date` (`release_date`),
  CONSTRAINT `ck_magazine_issue_month` CHECK ((`issue_month` between 1 and 12))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='アニメ雑誌の号マスタ（各誌横並びの代表値）。サブタイトル掲載号の解決に使う';

-- ---------------------------------------------------------------------
-- 2) episodes.magazine_subtitle_status（列が無いときだけ ADD COLUMN）
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v163_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v163_add_col_if_missing(
  IN p_table VARCHAR(64),
  IN p_col   VARCHAR(64),
  IN p_def   TEXT)
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_col;
  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

-- youtube_special_trailer_url の直後に追加する。既存行はすべて NULL（データなし）のまま。
CALL _v163_add_col_if_missing(
  'episodes',
  'magazine_subtitle_status',
  "ENUM('PUBLISHED','NOT_DISCLOSED','UNDECIDED') DEFAULT NULL COMMENT 'アニメ雑誌でのサブタイトル掲載状態（NULL=データなし）' AFTER `youtube_special_trailer_url`");

DROP PROCEDURE _v163_add_col_if_missing;

-- ---------------------------------------------------------------------
-- 3) episodes.title_text の NULL 許容化（NULL = サブタイトル未確定）
--    既に NULL 許容なら何もしない。照合順序は既存定義を維持する。
-- ---------------------------------------------------------------------
SET @title_not_nullable = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'episodes'
    AND COLUMN_NAME  = 'title_text'
    AND IS_NULLABLE  = 'NO'
);
SET @stmt = IF(@title_not_nullable = 1,
  "ALTER TABLE `episodes`
     MODIFY COLUMN `title_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NULL
       COMMENT 'サブタイトル（NULL=未確定。NULL は掲載状態が非公開/未定のときのみ許可）'",
  "SELECT 'episodes.title_text is already nullable, skipping ALTER' AS msg");
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ---------------------------------------------------------------------
-- 「サブタイトル無し」は誌面根拠（非公開 / 未定）があるときだけ許す CHECK。
-- COALESCE で status NULL を空文字に落とし、UNKNOWN 通過（CHECK は UNKNOWN を
-- 違反にしない）で NULL + NULL の組み合わせがすり抜けるのを防ぐ。
-- ---------------------------------------------------------------------
SET @has_ck = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME        = 'episodes'
    AND CONSTRAINT_NAME   = 'ck_ep_title_or_magazine_reason'
);
SET @stmt = IF(@has_ck = 0,
  "ALTER TABLE `episodes`
     ADD CONSTRAINT `ck_ep_title_or_magazine_reason`
     CHECK ((`title_text` IS NOT NULL)
         OR (COALESCE(`magazine_subtitle_status`, '') IN ('NOT_DISCLOSED','UNDECIDED')))",
  "SELECT 'ck_ep_title_or_magazine_reason already exists, skipping ALTER' AS msg");
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

COMMIT;

SELECT 'v1.6.3 migration completed: magazine_issues + episodes.magazine_subtitle_status + nullable title_text' AS final_status;
