-- =====================================================================
-- v1.4.0_migration_bgm_sessions_caption.sql
--
-- bgm_sessions テーブルに公開サイト表示用の補足説明カラム `caption` を追加する。
--
-- 用途：
--   劇伴詳細ページ（/bgms/{slug}/）の録音セッション見出しの横に、録音日・スタジオ名などを
--   控えめに添えて表示するための公開向け自由テキスト。既存の `notes`（内部メモ用途）とは
--   別役割となる。
--
-- 整合性：
--   - NULL 許容、デフォルト NULL。
--   - 自由テキスト。書式は運用側に委ね、構造化検索の対象とはしない。
--   - utf8mb4 / utf8mb4_ja_0900_as_cs_ks（テキスト本文向けの照合順序）を採用。
--
-- 配置：
--   `session_name` の直後。
--
-- 冪等性：
--   INFORMATION_SCHEMA.COLUMNS で既存有無を確認してから ALTER を流す。
--   適用済みの環境で再実行しても 0 件追加で素通りする。
-- =====================================================================

SET @col_exists := (
    SELECT COUNT(*)
      FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE()
       AND TABLE_NAME   = 'bgm_sessions'
       AND COLUMN_NAME  = 'caption'
);

SET @ddl := IF(@col_exists = 0,
    'ALTER TABLE `bgm_sessions`
        ADD COLUMN `caption` VARCHAR(255)
            CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks
            DEFAULT NULL
            COMMENT ''公開サイトのセッション見出し横に小さく添える補足説明（録音日・スタジオ等の自由テキスト）''
            AFTER `session_name`;',
    'SELECT ''bgm_sessions.caption already exists, skipped.'' AS info;'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
