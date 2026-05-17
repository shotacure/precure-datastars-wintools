-- =============================================================================
--  v1.3.3 既存環境向け：映画 BGM リスト用テーブル movie_bgm_cues の追加
--
--  映画作品の BGM リスト 1 行 = 1 キュー。bgm_cues（TV シリーズのセッション制・
--  劇伴専用）とは概念が異なるため独立テーブルとして新設する。映画にはセッション
--  やパートの概念が無く、その映画固有の M ナンバー文字列・順序（seq）・サブ順序
--  （sub_seq）と、そのキュー自体が何か（track_content_kinds 区分を tracks と共用）
--  のみを持つ。さらに映画 BGM 特有の「未使用（音源は存在するが本編未使用）」と
--  「欠番（そもそも制作されていない）」を独立 2 フラグで保持し、両立は CHECK で排他。
--
--  series_id は映画系シリーズ（kind_code が MOVIE / MOVIE_SHORT / SPRING / EVENT）
--  のみを許容する。MySQL の CHECK は他テーブル（series）を参照できないため、
--  この種別制約は BEFORE INSERT / BEFORE UPDATE トリガーで担保する。
--
--  前提：series / track_content_kinds は既存（FK 先）。
--
--  冪等性：CREATE TABLE IF NOT EXISTS と DROP TRIGGER IF EXISTS → CREATE TRIGGER
--  の組み合わせで、未適用環境でも適用済み環境でも安全に再実行できる。
--
--  実行方法:
--    mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.3.3_add_movie_bgm_cues.sql
-- =============================================================================

CREATE TABLE IF NOT EXISTS `movie_bgm_cues` (
  `movie_bgm_cue_id` int NOT NULL AUTO_INCREMENT,
  `series_id` int NOT NULL,
  -- 映画内での並び順。0 は新規追加直後の暫定値。
  `seq` int NOT NULL DEFAULT 0,
  -- サブ順序（同一 seq 内での枝番）。
  `sub_seq` int NOT NULL DEFAULT 0,
  -- その映画固有の M ナンバー文字列。欠番では値が無いため NULL 可。
  `m_no` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  -- このキュー自体が何か。tracks と共通の track_content_kinds を参照。既定は 'BGM'。
  `content_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'BGM',
  -- 曲名・メニュー表記等（任意）。
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  -- 未使用：音源は存在するが本編未使用。
  `is_unused` tinyint NOT NULL DEFAULT 0,
  -- 欠番：番号としては存在するがそもそも制作されていない（音源が存在しない）。
  `is_missing` tinyint NOT NULL DEFAULT 0,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`movie_bgm_cue_id`),
  UNIQUE KEY `uq_movie_bgm_cues_series_seq` (`series_id`,`seq`,`sub_seq`),
  KEY `ix_movie_bgm_cues_series_kind` (`series_id`,`content_kind_code`),
  KEY `ix_movie_bgm_cues_kind` (`content_kind_code`),
  CONSTRAINT `fk_movie_bgm_cues_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_movie_bgm_cues_kind` FOREIGN KEY (`content_kind_code`) REFERENCES `track_content_kinds` (`kind_code`) ON DELETE RESTRICT ON UPDATE CASCADE,
  -- 未使用と欠番は両立しない。排他を CHECK で担保。
  CONSTRAINT `ck_movie_bgm_cues_unused_missing_exclusive` CHECK (NOT (`is_unused` = 1 AND `is_missing` = 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 映画系 kind 限定の整合性トリガー（series.kind_code を検査）。
DROP TRIGGER IF EXISTS `trg_movie_bgm_cues_bi_series_kind`;
DROP TRIGGER IF EXISTS `trg_movie_bgm_cues_bu_series_kind`;

DELIMITER ;;

CREATE TRIGGER `trg_movie_bgm_cues_bi_series_kind`
BEFORE INSERT ON `movie_bgm_cues`
FOR EACH ROW
BEGIN
  IF NOT EXISTS (
       SELECT 1 FROM `series`
        WHERE `series_id` = NEW.`series_id`
          AND `kind_code` IN ('MOVIE','MOVIE_SHORT','SPRING','EVENT')
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'movie_bgm_cues: series_id must reference a movie-type series (kind_code in MOVIE, MOVIE_SHORT, SPRING, EVENT)';
  END IF;
END;;

CREATE TRIGGER `trg_movie_bgm_cues_bu_series_kind`
BEFORE UPDATE ON `movie_bgm_cues`
FOR EACH ROW
BEGIN
  IF NOT EXISTS (
       SELECT 1 FROM `series`
        WHERE `series_id` = NEW.`series_id`
          AND `kind_code` IN ('MOVIE','MOVIE_SHORT','SPRING','EVENT')
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'movie_bgm_cues: series_id must reference a movie-type series (kind_code in MOVIE, MOVIE_SHORT, SPRING, EVENT)';
  END IF;
END;;

DELIMITER ;
