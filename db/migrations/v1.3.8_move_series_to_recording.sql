-- =====================================================================
-- v1.3.8：歌の出典シリーズ (series_id) を song_recordings へ移設するマイグレーション
--
-- 内容：
--   1. song_recordings に series_id 列を追加（FK は後段で張る）。
--   2. 旧 songs.series_id の値を、所属 song_id の全 song_recordings
--      に伝播コピーする（録音単位への移設）。
--   3. songs.series_id の FK・INDEX・列を撤去。
--   4. song_recordings.series_id に FK と INDEX を張る。
--
-- 背景：同一曲（メロディ + アレンジ）でも、カバー版や挿入歌としての再利用で
-- 「出典シリーズ」が文脈変化することがある（A 作品の OP がのちに B 作品の
-- 挿入歌として歌い直されるケース等）。出典を録音単位で持つことで、業務実態と
-- 整合させる。マイグレ後、必要に応じて Catalog GUI で recording 単位の出典を
-- 個別調整する。
--
-- 安全設計：
--   - 全 DDL は INFORMATION_SCHEMA で対象オブジェクトの存在を確認してから
--     PREPARE / EXECUTE する冪等パターン（リポジトリ既定方針）。途中で失敗
--     しても、現在の DB 状態から本スクリプト全体を再実行して残処理だけを
--     進めることができる（適用済みのステップは静かに素通りする）。
--   - DDL 文は MySQL の挙動上 implicit commit を発生させるため、スクリプト
--     全体としての原子性は担保できない。重要なのは各 DDL/DML が単独で安全に
--     再実行できる冪等性を持つこと。
--   - PK 以外の列を WHERE 条件にする UPDATE を含むため、
--     SQL_SAFE_UPDATES を一時的に OFF にする（MySQL Workbench の既定
--     有効化への対策）。終端でセッションの元値に戻す。
-- =====================================================================

-- SQL_SAFE_UPDATES を一時的に無効化（PK 非参照の UPDATE があるため）。
-- セッション終了後に影響しないよう元の値を保存して終端で復元する。
SET @saved_sql_safe_updates := @@SESSION.SQL_SAFE_UPDATES;
SET SESSION SQL_SAFE_UPDATES = 0;

-- ---------------------------------------------------------------------
-- 1. song_recordings に series_id 列を追加（冪等）
-- ---------------------------------------------------------------------
-- 列が既に存在する場合は何もしない。NULL 許容（オールスターズや出典不明用）。
-- FK は移行データ確定後（ステップ 4）に張るため、ここでは列のみ追加する。
SET @col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'song_recordings'
      AND COLUMN_NAME  = 'series_id'
);
SET @sql := IF(@col_exists = 0,
    'ALTER TABLE `song_recordings` ADD COLUMN `series_id` int DEFAULT NULL AFTER `song_id`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. songs.series_id の値を全 song_recordings へ伝播
--    songs 側の値が真である前提で、当該 song_id を持つ全 recording に
--    一律コピーする。recording.series_id が既に非 NULL なら上書きしない
--    （再実行時に Catalog GUI で個別調整した値を消さないため）。
-- ---------------------------------------------------------------------
-- songs.series_id 列がまだ存在する場合のみ伝播を実行（過去に本マイグレが
-- ステップ 3 まで通っていれば songs.series_id は既に消えている）。
SET @songs_col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND COLUMN_NAME  = 'series_id'
);
SET @sql := IF(@songs_col_exists = 1,
    'UPDATE `song_recordings` AS sr
        JOIN `songs` AS s ON s.`song_id` = sr.`song_id`
        SET sr.`series_id` = s.`series_id`
      WHERE sr.`series_id` IS NULL
        AND s.`series_id` IS NOT NULL',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. songs.series_id の FK・INDEX・列を撤去（冪等）
-- ---------------------------------------------------------------------
-- 3a. FK 撤去
SET @fk_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME        = 'songs'
      AND CONSTRAINT_NAME   = 'fk_songs_series'
);
SET @sql := IF(@fk_exists = 1,
    'ALTER TABLE `songs` DROP FOREIGN KEY `fk_songs_series`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3b. INDEX 撤去
SET @idx_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND INDEX_NAME   = 'ix_songs_series'
);
SET @sql := IF(@idx_exists = 1,
    'ALTER TABLE `songs` DROP INDEX `ix_songs_series`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3c. 列撤去
SET @col_still_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND COLUMN_NAME  = 'series_id'
);
SET @sql := IF(@col_still_exists = 1,
    'ALTER TABLE `songs` DROP COLUMN `series_id`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 4. song_recordings.series_id に INDEX と FK を張る（冪等）
-- ---------------------------------------------------------------------
-- 4a. INDEX 追加
SET @idx_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'song_recordings'
      AND INDEX_NAME   = 'ix_song_recordings_series'
);
SET @sql := IF(@idx_exists = 0,
    'ALTER TABLE `song_recordings` ADD KEY `ix_song_recordings_series` (`series_id`)',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 4b. FK 追加。ON DELETE SET NULL（出典シリーズが削除されたら NULL に倒す。
-- 旧 songs.series_id 同型の挙動）。ON UPDATE CASCADE（series_id 値変更は追従）。
SET @fk_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME        = 'song_recordings'
      AND CONSTRAINT_NAME   = 'fk_song_recordings_series'
);
SET @sql := IF(@fk_exists = 0,
    'ALTER TABLE `song_recordings` ADD CONSTRAINT `fk_song_recordings_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- SQL_SAFE_UPDATES をセッションの元の値に戻す
SET SESSION SQL_SAFE_UPDATES = @saved_sql_safe_updates;
