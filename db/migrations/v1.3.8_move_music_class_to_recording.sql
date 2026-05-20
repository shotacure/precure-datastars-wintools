-- =====================================================================
-- v1.3.8：歌の音楽種別を song_recordings へ移設するマイグレーション
--
-- 内容：
--   1. song_recordings に music_class_code 列を追加（FK は後段で張る）。
--   2. 旧 songs.music_class_code の値を、所属 song_id の全 song_recordings
--      に伝播コピーする（録音単位への移設）。
--   3. songs.music_class_code の FK・INDEX・列を撤去。
--   4. song_music_classes マスタを再シードする：
--        - display_order の重複バグ修正（旧: INSERT=3, CHARA=3）。
--        - 旧 'MOVIE'（映画主題歌の総称）を撤去し、
--          'MOVIE_OP' / 'MOVIE_ED' / 'MOVIE_INSERT' の 3 種に分割。
--        - 旧 'MOVIE' を参照する recording があれば NULL に退避
--          （OP/ED/挿入歌の自動判定不可のため）。
--   5. song_recordings.music_class_code に FK と INDEX を張る。
--
-- 背景：music_class_code を songs 側に持つ設計では、同一曲のカバーや
-- アレンジが OP→キャラソン等で文脈変化するケースを表現できなかった。
-- 種別を録音単位で持つことで、業務実態（カバー版の種別変化）と整合させる。
--
-- 安全設計：
--   - 全 DDL は INFORMATION_SCHEMA で対象オブジェクトの存在を確認してから
--     PREPARE / EXECUTE する冪等パターン（リポジトリ既定方針）。途中で失敗
--     しても、現在の DB 状態から本スクリプト全体を再実行して残処理だけを
--     進めることができる（適用済みのステップは静かに素通りする）。
--   - DDL 文は MySQL の挙動上 implicit commit を発生させるため、スクリプト
--     全体としての原子性は担保できない。重要なのは各 DDL/DML が単独で安全に
--     再実行できる冪等性を持つこと。
--   - PK 以外の列を WHERE 条件にする UPDATE と、WHERE 無しの DELETE を含む
--     ため、SQL_SAFE_UPDATES を一時的に OFF にする（MySQL Workbench の既定
--     有効化への対策）。終端でセッションの元値に戻す。
-- =====================================================================

-- SQL_SAFE_UPDATES を一時的に無効化（PK 非参照の UPDATE と全削除があるため）。
-- セッション終了後に影響しないよう元の値を保存して終端で復元する。
SET @saved_sql_safe_updates := @@SESSION.SQL_SAFE_UPDATES;
SET SESSION SQL_SAFE_UPDATES = 0;

-- ---------------------------------------------------------------------
-- 1. song_recordings.music_class_code 列を追加（冪等）
--    既に存在する場合は何もしない。
-- ---------------------------------------------------------------------
SET @col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'song_recordings'
      AND COLUMN_NAME  = 'music_class_code'
);
SET @sql := IF(@col_exists = 0,
    'ALTER TABLE `song_recordings` ADD COLUMN `music_class_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL AFTER `variant_label`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. 旧 songs.music_class_code を全 recording に伝播コピー（冪等）
--    songs 側の列が既に撤去済みなら（=再実行時）コピーをスキップする。
--    曲単位で持っていた種別を、その曲のすべての録音バージョンに
--    暫定的に同値展開する。録音ごとに種別が異なる実態がある場合は
--    本マイグレ後に Catalog GUI から個別補正する運用。
-- ---------------------------------------------------------------------
SET @src_col_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND COLUMN_NAME  = 'music_class_code'
);
SET @sql := IF(@src_col_exists = 1,
    'UPDATE `song_recordings` sr JOIN `songs` s ON s.`song_id` = sr.`song_id` SET sr.`music_class_code` = s.`music_class_code` WHERE s.`music_class_code` IS NOT NULL',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. songs.music_class_code の FK / INDEX / 列を撤去（冪等）
--    すでに撤去済みのものは静かに素通り。
-- ---------------------------------------------------------------------
-- 3a. FK 撤去
SET @fk_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME        = 'songs'
      AND CONSTRAINT_NAME   = 'fk_songs_music_class'
);
SET @sql := IF(@fk_exists = 1,
    'ALTER TABLE `songs` DROP FOREIGN KEY `fk_songs_music_class`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3b. INDEX 撤去（INFORMATION_SCHEMA.STATISTICS は複合インデックスでも 1 行/カラム
--     なので COUNT >= 1 で存在判定）
SET @idx_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND INDEX_NAME   = 'ix_songs_music_class'
);
SET @sql := IF(@idx_exists >= 1,
    'ALTER TABLE `songs` DROP INDEX `ix_songs_music_class`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3c. 列撤去
SET @col_still_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'songs'
      AND COLUMN_NAME  = 'music_class_code'
);
SET @sql := IF(@col_still_exists = 1,
    'ALTER TABLE `songs` DROP COLUMN `music_class_code`',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 4. song_music_classes マスタの再シード
--    旧 'MOVIE' を参照していた録音が残っていたら NULL に退避する
--    （映画主題歌の OP/ED/挿入歌区別は自動判定できないため。
--      実データ移行時点では 'MOVIE' 参照行は存在しないと聞いているが、
--      安全側に UPDATE しておく）。
--    マスタは全削除して入れ直す。display_order の UNIQUE 制約と
--    値の入れ替え（INSERT=3, CHARA=4 への振り替え）を一度に行うため
--    DELETE → INSERT 方式を採る。冪等：何度実行しても同じ結果に収束する。
-- ---------------------------------------------------------------------
UPDATE `song_recordings`
   SET `music_class_code` = NULL
 WHERE `music_class_code` = 'MOVIE';

DELETE FROM `song_music_classes`;
INSERT INTO `song_music_classes` (`class_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('OP','オープニング主題歌','Opening Theme',1),
  ('ED','エンディング主題歌','Ending Theme',2),
  ('INSERT','挿入歌','Insert Song',3),
  ('CHARA','キャラクターソング','Character Song',4),
  ('IMAGE','イメージソング','Image Song',5),
  ('MOVIE_OP','映画オープニング主題歌','Movie Opening Theme',6),
  ('MOVIE_ED','映画エンディング主題歌','Movie Ending Theme',7),
  ('MOVIE_INSERT','映画挿入歌','Movie Insert Song',8),
  ('OTHER','その他','Other',99);

-- ---------------------------------------------------------------------
-- 5. song_recordings.music_class_code に INDEX と FK を張る（冪等）
-- ---------------------------------------------------------------------
-- 5a. INDEX 追加
SET @idx_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'song_recordings'
      AND INDEX_NAME   = 'ix_song_recordings_music_class'
);
SET @sql := IF(@idx_exists = 0,
    'ALTER TABLE `song_recordings` ADD KEY `ix_song_recordings_music_class` (`music_class_code`)',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 5b. FK 追加
SET @fk_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME        = 'song_recordings'
      AND CONSTRAINT_NAME   = 'fk_song_recordings_music_class'
);
SET @sql := IF(@fk_exists = 0,
    'ALTER TABLE `song_recordings` ADD CONSTRAINT `fk_song_recordings_music_class` FOREIGN KEY (`music_class_code`) REFERENCES `song_music_classes` (`class_code`)',
    'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- SQL_SAFE_UPDATES をセッションの元の値に戻す
SET SESSION SQL_SAFE_UPDATES = @saved_sql_safe_updates;
