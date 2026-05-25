-- =====================================================================
-- v1.4.3_add_external_urls.sql
--
-- 人物・企業・キャラクター・音楽商品の各マスタに、外部リンク用の URL 列を
-- 追加する。サイト詳細ページの末尾「外部リンク」セクションでアイコン付きリンクとして
-- 表示する用途。Wikipedia URL のみ「内部値として保持はするがサイト UI からは
-- リンクしない」運用方針（schema.org の sameAs 出力など将来用途のためのメモ列）。
--
--   persons         + 5 列: official_url / x_url / instagram_url / youtube_url / wikipedia_url
--   companies       + 5 列: 同上 5 列
--   characters      + 2 列: official_url / wikipedia_url
--   products        + 1 列: official_url
--
-- 全列とも VARCHAR(1024) NULL（長めの URL や ?query=... も収まる余裕を取る）。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- 既存マイグレーションのスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 共通：列が無いときだけ ADD COLUMN するプロシージャ。
-- ALTER 内の COLUMN 定義を文字列で受け取り、テーブル名 + 列名 + 定義を組んで実行する。
-- 完了後 DROP して残骸を残さない。
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS _v143_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v143_add_col_if_missing(
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

-- ---------------------------------------------------------------------
-- persons + 5 列
-- ---------------------------------------------------------------------
CALL _v143_add_col_if_missing('persons', 'official_url',  "VARCHAR(1024) DEFAULT NULL COMMENT '事務所等の公式ページ URL（詳細ページに外部リンクとして表示）' AFTER `notes`");
CALL _v143_add_col_if_missing('persons', 'x_url',         "VARCHAR(1024) DEFAULT NULL COMMENT 'X (Twitter) プロフィール URL' AFTER `official_url`");
CALL _v143_add_col_if_missing('persons', 'instagram_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'Instagram プロフィール URL' AFTER `x_url`");
CALL _v143_add_col_if_missing('persons', 'youtube_url',   "VARCHAR(1024) DEFAULT NULL COMMENT 'YouTube チャンネル URL' AFTER `instagram_url`");
CALL _v143_add_col_if_missing('persons', 'wikipedia_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'Wikipedia 記事 URL（内部メモ、サイト UI ではリンクしない）' AFTER `youtube_url`");

-- ---------------------------------------------------------------------
-- companies + 5 列
-- ---------------------------------------------------------------------
CALL _v143_add_col_if_missing('companies', 'official_url',  "VARCHAR(1024) DEFAULT NULL COMMENT '企業公式ページ URL（詳細ページに外部リンクとして表示）' AFTER `notes`");
CALL _v143_add_col_if_missing('companies', 'x_url',         "VARCHAR(1024) DEFAULT NULL COMMENT 'X (Twitter) アカウント URL' AFTER `official_url`");
CALL _v143_add_col_if_missing('companies', 'instagram_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'Instagram アカウント URL' AFTER `x_url`");
CALL _v143_add_col_if_missing('companies', 'youtube_url',   "VARCHAR(1024) DEFAULT NULL COMMENT 'YouTube チャンネル URL' AFTER `instagram_url`");
CALL _v143_add_col_if_missing('companies', 'wikipedia_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'Wikipedia 記事 URL（内部メモ、サイト UI ではリンクしない）' AFTER `youtube_url`");

-- ---------------------------------------------------------------------
-- characters + 2 列
-- ---------------------------------------------------------------------
CALL _v143_add_col_if_missing('characters', 'official_url',  "VARCHAR(1024) DEFAULT NULL COMMENT 'キャラクター公式ページ URL（詳細ページに外部リンクとして表示）' AFTER `notes`");
CALL _v143_add_col_if_missing('characters', 'wikipedia_url', "VARCHAR(1024) DEFAULT NULL COMMENT 'Wikipedia 記事 URL（内部メモ、サイト UI ではリンクしない）' AFTER `official_url`");

-- ---------------------------------------------------------------------
-- products + 1 列
-- ---------------------------------------------------------------------
CALL _v143_add_col_if_missing('products', 'official_url', "VARCHAR(1024) DEFAULT NULL COMMENT '音楽商品の公式ページ URL（詳細ページに外部リンクとして表示）' AFTER `notes`");

DROP PROCEDURE _v143_add_col_if_missing;

COMMIT;
