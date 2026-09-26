-- =====================================================================
-- v1.12.9_add_srba_size_variant.sql
--
-- 歌と劇伴の両性紐付け（song_recording_bgm_assignments）にサイズの指定を追加する。
--
-- 同じ録音でも、サイズ違いの版（フルサイズと短い版など）が別々の劇伴 cue に当たることがある。
-- これまでは (録音, パート) でしか紐付けを区別できず、サイズ違いの版を別の cue に振り分けられなかった。
--
--   song_size_variants + 1 行:
--     '_ANY'  サイズ区別なく適用する sentinel（song_part_variants の '_ANY' と同じ扱い）
--
--   song_recording_bgm_assignments + 1 列（PK に追加）:
--     song_size_variant_code  適用サイズ（→ song_size_variants）。既定 '_ANY'。
--       実サイズコードを指定した行は、tracks.song_size_variant_code が一致するトラックにだけ当たる。
--       '_ANY' の行はトラックのサイズに関わらず（サイズ未登録の NULL も含めて）当たる。
--     既存行は '_ANY' になるため、紐付け結果は変わらない。
--
-- 冪等性: 行・列・FK の存在を確認してから追加する。PK の張り替えは列追加と同時にのみ行う。
-- =====================================================================

INSERT INTO song_size_variants (variant_code, name_ja, name_en, display_order)
SELECT '_ANY', '(指定なし)', NULL, NULL
 WHERE NOT EXISTS (SELECT 1 FROM song_size_variants WHERE variant_code = '_ANY');

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'song_recording_bgm_assignments'
       AND COLUMN_NAME  = 'song_size_variant_code'
);
SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `song_recording_bgm_assignments`
        ADD COLUMN `song_size_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT ''_ANY''
            COMMENT ''適用サイズ（→ song_size_variants）。_ANY はサイズ区別なく適用''
            AFTER `song_part_variant_code`,
        DROP PRIMARY KEY,
        ADD PRIMARY KEY (`song_recording_id`, `song_size_variant_code`, `song_part_variant_code`, `bgm_series_id`, `bgm_m_no_detail`),
        ADD KEY `ix_srba_size` (`song_size_variant_code`);',
    'SELECT ''song_recording_bgm_assignments.song_size_variant_code already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @fk_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA    = DATABASE()
       AND TABLE_NAME      = 'song_recording_bgm_assignments'
       AND CONSTRAINT_NAME = 'fk_srba_size'
);
SET @ddl := IF(@fk_exists = 0,
    'ALTER TABLE `song_recording_bgm_assignments`
        ADD CONSTRAINT `fk_srba_size` FOREIGN KEY (`song_size_variant_code`)
            REFERENCES `song_size_variants` (`variant_code`)
            ON DELETE RESTRICT ON UPDATE CASCADE;',
    'SELECT ''fk_srba_size already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
