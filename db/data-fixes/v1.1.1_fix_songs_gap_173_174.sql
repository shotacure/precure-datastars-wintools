-- =============================================================================
-- Data-fix: songs 173/174 の間、song_recordings 189/190 の間に 5 曲を押し込む
-- =============================================================================
-- 背景:
--   16 年前（旧 SQL Server 版運用開始時）のデータ採番ルールの適用ミスにより、
--   本来 songs.song_id = 173 と 174 の間に存在すべき 5 曲が登録漏れとなっていた。
--   それらの song に対応する song_recording も song_recording_id = 189 と 190 の
--   間に入るべきだった。ID の大小関係を時系列順の指標として使っている運用上、
--   末尾に追加するのではなく、あるべき位置（173 と 174 の間）に「ねじ込む」必要がある。
--
--   同種の事象は本件限りで今後発生しない（以降は採番ルールが固定されている）ため、
--   本スクリプトはワンショット運用を前提とする（冪等性は保証しない）。
--
-- 方針:
--   1. songs.song_id >= 174 を一括で +5 シフト
--      → song_recordings.song_id は ON UPDATE CASCADE で自動追従
--   2. 空いた song_id = 174..178 の 5 スロットに新 songs を INSERT
--   3. song_recordings.song_recording_id >= 190 を一括で +5 シフト
--      → tracks.song_recording_id は ON UPDATE CASCADE で自動追従
--   4. 空いた song_recording_id = 190..194 の 5 スロットに新 song_recordings を INSERT
--   5. AUTO_INCREMENT カウンタを max+1 に再設定（InnoDB は通常自動だが念のため）
--
-- 前提:
--   - schema.sql 相当の FK 定義（両方 ON UPDATE CASCADE）であること
--   - tracks のトリガーは FK cascade では発火しない（MySQL 仕様）ので影響なし
--
-- 必ず単一トランザクションで実行し、失敗時は ROLLBACK で完全復旧できる状態にしておく。
-- 実運用前に、DB スナップショット or mysqldump によるバックアップを強く推奨。
-- =============================================================================

START TRANSACTION;

-- -----------------------------------------------------------------------------
-- Step 0: 前提確認（手動で事前にチェックしておくこと）
-- -----------------------------------------------------------------------------
-- 実行前に下記 SELECT で現状を確認する:
--   SELECT song_id, title FROM songs WHERE song_id BETWEEN 170 AND 180 ORDER BY song_id;
--   SELECT song_recording_id, song_id, singer_name FROM song_recordings
--     WHERE song_recording_id BETWEEN 185 AND 195 ORDER BY song_recording_id;
--
-- 想定: 173 と 174 の間に空きはなく連続しているはず。5 件分 +5 シフト後に空きが発生する。

-- -----------------------------------------------------------------------------
-- Step 1: songs.song_id >= 174 を +5 シフト
-- -----------------------------------------------------------------------------
-- ORDER BY song_id DESC で降順に処理するのは、row-by-row 実行になった場合の PK 衝突回避策。
-- MySQL の UPDATE は基本 set-oriented で中間状態は外部から見えないが、FK cascade と
-- 組み合わせたときの挙動を安全側に寄せるため明示指定する。
-- この時点で song_recordings.song_id も ON UPDATE CASCADE で自動追従する。
UPDATE songs
   SET song_id    = song_id + 5,
       updated_by = 'data-fix-2026-04'
 WHERE song_id >= 174
 ORDER BY song_id DESC;

-- -----------------------------------------------------------------------------
-- Step 2: 新 5 曲を song_id = 174..178 で INSERT
-- -----------------------------------------------------------------------------
-- ↓ 以下の 5 行は実データに書き換えること（タイトル・作詞作曲・シリーズ等）
--   song_id は 174..178 で固定（ここを変えると Step 1 のシフト量 +5 との整合が取れなくなる）。
--   series_id NULL はオールスターズ / 不明扱い、music_class_code NULL は種別未設定。
INSERT INTO songs
  (song_id, title, title_kana, music_class_code, series_id,
   original_lyricist_name, original_lyricist_name_kana,
   original_composer_name, original_composer_name_kana,
   arranger_name, arranger_name_kana,
   notes, created_by, updated_by)
VALUES
  (174, '★★★ TITLE 1 ★★★', NULL, NULL, NULL,
   NULL, NULL, NULL, NULL, NULL, NULL,
   'data-fix: legacy gap 173/174 (slot 1/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (175, '★★★ TITLE 2 ★★★', NULL, NULL, NULL,
   NULL, NULL, NULL, NULL, NULL, NULL,
   'data-fix: legacy gap 173/174 (slot 2/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (176, '★★★ TITLE 3 ★★★', NULL, NULL, NULL,
   NULL, NULL, NULL, NULL, NULL, NULL,
   'data-fix: legacy gap 173/174 (slot 3/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (177, '★★★ TITLE 4 ★★★', NULL, NULL, NULL,
   NULL, NULL, NULL, NULL, NULL, NULL,
   'data-fix: legacy gap 173/174 (slot 4/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (178, '★★★ TITLE 5 ★★★', NULL, NULL, NULL,
   NULL, NULL, NULL, NULL, NULL, NULL,
   'data-fix: legacy gap 173/174 (slot 5/5)', 'data-fix-2026-04', 'data-fix-2026-04');

-- -----------------------------------------------------------------------------
-- Step 3: song_recordings.song_recording_id >= 190 を +5 シフト
-- -----------------------------------------------------------------------------
-- tracks.song_recording_id は ON UPDATE CASCADE で自動追従する。
-- songs.song_id は既に Step 1 で追従済みなので、ここでは PK 列のみ更新。
UPDATE song_recordings
   SET song_recording_id = song_recording_id + 5,
       updated_by        = 'data-fix-2026-04'
 WHERE song_recording_id >= 190
 ORDER BY song_recording_id DESC;

-- -----------------------------------------------------------------------------
-- Step 4: 新 5 件の recording を song_recording_id = 190..194 で INSERT
-- -----------------------------------------------------------------------------
-- song_id は Step 2 で割り当てた新 songs の PK（174..178）を指す。
-- 1 曲 = 1 recording の想定（歌唱者バージョン違いが複数ある場合は別途追加 INSERT）。
-- ↓ 歌唱者名などは実データに書き換えること。
INSERT INTO song_recordings
  (song_recording_id, song_id, singer_name, singer_name_kana,
   variant_label, notes, created_by, updated_by)
VALUES
  (190, 174, '★★★ SINGER 1 ★★★', NULL, NULL,
   'data-fix: legacy gap 189/190 (slot 1/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (191, 175, '★★★ SINGER 2 ★★★', NULL, NULL,
   'data-fix: legacy gap 189/190 (slot 2/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (192, 176, '★★★ SINGER 3 ★★★', NULL, NULL,
   'data-fix: legacy gap 189/190 (slot 3/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (193, 177, '★★★ SINGER 4 ★★★', NULL, NULL,
   'data-fix: legacy gap 189/190 (slot 4/5)', 'data-fix-2026-04', 'data-fix-2026-04'),
  (194, 178, '★★★ SINGER 5 ★★★', NULL, NULL,
   'data-fix: legacy gap 189/190 (slot 5/5)', 'data-fix-2026-04', 'data-fix-2026-04');

-- -----------------------------------------------------------------------------
-- Step 5: AUTO_INCREMENT カウンタを max+1 に再設定
-- -----------------------------------------------------------------------------
-- InnoDB は次回 INSERT 時に max+1 を探すため原則不要だが、AUTO_INCREMENT が
-- 内部的に古い値のままになる環境もあるため明示リセットする。
-- ALTER TABLE ... AUTO_INCREMENT = N は数値リテラルしか受け付けないため動的 SQL で渡す。
SET @ai_songs = (SELECT IFNULL(MAX(song_id), 0) + 1 FROM songs);
SET @sql = CONCAT('ALTER TABLE songs AUTO_INCREMENT = ', @ai_songs);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @ai_srec = (SELECT IFNULL(MAX(song_recording_id), 0) + 1 FROM song_recordings);
SET @sql = CONCAT('ALTER TABLE song_recordings AUTO_INCREMENT = ', @ai_srec);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- Step 6: 実行後検証（手動で目視確認するためのコメントアウト済みクエリ）
-- -----------------------------------------------------------------------------
-- 下記 SELECT で、174..178 / 190..194 に新データが入り、旧 174/190 以降は +5 された
-- ことを確認する。
--
--   SELECT song_id, title FROM songs WHERE song_id BETWEEN 170 AND 185 ORDER BY song_id;
--   SELECT song_recording_id, song_id, singer_name FROM song_recordings
--     WHERE song_recording_id BETWEEN 187 AND 200 ORDER BY song_recording_id;
--
-- tracks 側の自動追従も確認:
--   SELECT catalog_no, track_no, song_recording_id FROM tracks
--     WHERE song_recording_id BETWEEN 187 AND 200 ORDER BY song_recording_id;

COMMIT;

-- ROLLBACK;  -- ← 異常時はこちらを実行して全変更を取り消す
