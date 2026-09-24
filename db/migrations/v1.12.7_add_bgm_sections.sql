-- =====================================================================
-- v1.12.7_add_bgm_sections.sql
--
-- 劇伴の録音セッション（bgm_sessions）の下にもう 1 段の区分「セクション」を設ける。
--
--   bgm_sections 新設:
--     (series_id, session_no, section_no) を PK とする bgm_sessions の子テーブル。
--     section_no はセッション内の表示順を兼ねる（1 から採番）。
--
--   bgm_cues + 1 列:
--     section_no  所属セクション。NULL はセクション無し（従来通りセッション直下に並ぶ）。
--     (series_id, session_no, section_no) の複合 FK で bgm_sections を参照するため、
--     「音源の所属セッションとセクションの所属セッションが食い違う」状態は DB が受け付けない。
--
-- 運用上の前提：
--   1 つのセッション内で「セクション所属あり」と「所属なし」の音源は混在させない。
--   DB では強制せず、SiteBuilder がビルド時に混在を検出したら警告を出す。
--
-- 冪等性：
--   テーブルは CREATE TABLE IF NOT EXISTS、列・インデックス・FK は INFORMATION_SCHEMA で
--   存在確認してから ALTER を発行する。適用済みの環境で再実行しても素通りする。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `bgm_sections` (
  `series_id`    int NOT NULL,
  `session_no`   tinyint unsigned NOT NULL,
  `section_no`   tinyint unsigned NOT NULL,
  `section_name` varchar(128) NOT NULL,
  `notes`        text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`   varchar(64) DEFAULT NULL,
  `updated_by`   varchar(64) DEFAULT NULL,
  PRIMARY KEY (`series_id`,`session_no`,`section_no`),
  CONSTRAINT `fk_bgm_sections_session` FOREIGN KEY (`series_id`,`session_no`) REFERENCES `bgm_sessions` (`series_id`,`session_no`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- bgm_cues.section_no（seq_in_session の直後）
SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'bgm_cues'
       AND COLUMN_NAME  = 'section_no'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `bgm_cues`
        ADD COLUMN `section_no` tinyint unsigned DEFAULT NULL
            COMMENT ''所属セクション（→ bgm_sections）。NULL はセクション無し''
            AFTER `seq_in_session`;',
    'SELECT ''bgm_cues.section_no already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 複合 FK 用のインデックス
SET @idx_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'bgm_cues'
       AND INDEX_NAME   = 'ix_bgm_cues_section'
);
SET @ddl := IF(@idx_exists = 0,
    'ALTER TABLE `bgm_cues` ADD KEY `ix_bgm_cues_section` (`series_id`,`session_no`,`section_no`);',
    'SELECT ''ix_bgm_cues_section already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 複合 FK（section_no が NULL の行は MySQL の仕様で参照検査の対象外になる）
SET @fk_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA    = DATABASE()
       AND TABLE_NAME      = 'bgm_cues'
       AND CONSTRAINT_NAME = 'fk_bgm_cues_section'
);
SET @ddl := IF(@fk_exists = 0,
    'ALTER TABLE `bgm_cues`
        ADD CONSTRAINT `fk_bgm_cues_section` FOREIGN KEY (`series_id`,`session_no`,`section_no`)
            REFERENCES `bgm_sections` (`series_id`,`session_no`,`section_no`)
            ON DELETE RESTRICT ON UPDATE CASCADE;',
    'SELECT ''fk_bgm_cues_section already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
