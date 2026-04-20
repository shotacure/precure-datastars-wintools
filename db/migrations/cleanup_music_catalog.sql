-- =============================================================================
-- CLEANUP: 音楽・映像カタログ系テーブルの全データ削除
-- =============================================================================
-- 用途:
--   LegacyImport を途中で失敗させた、BDAnalyzer の取り込みをやり直したい、
--   または全件再移行したい場合に、音楽・映像カタログ系のデータだけを一括削除する
--   ユーティリティ SQL。
--
-- 注意:
--   !!! このスクリプトは自動実行されません。ユーザーが明示的に流す必要があります !!!
--   !!! 実行するとカタログ系の全データが消えます。必要に応じてバックアップを取得してから実行してください !!!
--
-- 消えるテーブル:
--   tracks / video_chapters /
--   bgm_cues / bgm_sessions /
--   song_recordings / songs /
--   discs / products
--
-- 残るテーブル:
--   series / episodes / episode_parts 等のエピソード系（移行前から存在するデータ）
--   product_kinds / disc_kinds / track_content_kinds / song_music_classes /
--   song_arrange_classes / song_size_variants / song_part_variants / part_types /
--   series_kinds / series_relation_kinds 等のマスタ（初期データはそのまま）
--
-- 実装メモ:
--   TRUNCATE は FOREIGN_KEY_CHECKS=0 を設定しても FK で参照されているテーブルでは
--   Error 1701 で拒否される仕様なので、ここでは DELETE を使って各テーブルを空にする。
--   AUTO_INCREMENT は DELETE ではリセットされないため、末尾で ALTER TABLE で明示的に戻す。
--
--   bgm_sessions は旧設計ではマスタ扱い（session_no=0 を常駐させる運用）だったが、
--   採番 A 案（常に 1 始まり、既定セッション撤廃）への変更に伴い、cleanup 時にも
--   全削除してゼロから再採番させる運用に変更した。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/cleanup_music_catalog.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;
SET FOREIGN_KEY_CHECKS = 0;  -- FK 依存順を気にせず DELETE できるよう一時的に無効化
SET SQL_SAFE_UPDATES   = 0;  -- MySQL Workbench のセーフ更新モードで WHERE 無し DELETE が弾かれるのを回避

-- 子テーブルから順に DELETE（ただし FOREIGN_KEY_CHECKS=0 なので順不同でも可）
-- video_chapters は BD/DVD ディスクのチャプター情報（BDAnalyzer で登録）。
-- tracks と同じくディスク配下にぶら下がるので同列に扱う。
-- ※ まだ v1.1.1 以降のマイグレーションを流していない環境では video_chapters は存在しないため
--   テーブル非存在で失敗しないよう、IF EXISTS を使って DELETE する。
SET @has_video_chapters = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'video_chapters'
);
SET @stmt = IF(@has_video_chapters > 0, 'DELETE FROM `video_chapters`', 'DO 0');
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

DELETE FROM `tracks`;
DELETE FROM `bgm_cues`;
DELETE FROM `bgm_sessions`;      -- セッションも撤廃。LegacyImport 再実行時に 1 から採番される
DELETE FROM `song_recordings`;
DELETE FROM `songs`;
DELETE FROM `discs`;
DELETE FROM `products`;

-- AUTO_INCREMENT を 1 にリセット（再投入時に旧 ID を再利用できるようにする）
-- products / discs / bgm_cues / bgm_sessions / video_chapters は全て自然キー (varchar / (series_id, ...) 複合)
-- のため AUTO_INCREMENT 列なし。AUTO_INCREMENT を持つのは songs / song_recordings のみ。
ALTER TABLE `songs`           AUTO_INCREMENT = 1;
ALTER TABLE `song_recordings` AUTO_INCREMENT = 1;

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;

-- Cleanup completed
