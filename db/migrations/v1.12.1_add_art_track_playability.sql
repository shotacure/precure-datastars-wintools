-- =====================================================================
-- v1.12.1_add_art_track_playability.sql
--
-- 配信音源（YouTube アートトラック）の「実際に再生できるか」を保持する列を追加する。
--
--   tracks + 1 列:
--     youtube_playability  再生可否の判定結果
--
-- 背景：
-- YouTube Data API の status.embeddable は「埋め込みタグを置いてよいか」しか示さず、
-- 「誰でも再生できるか」は分からない。実際に embeddable = true のまま
-- 「この動画を視聴できるのは、Music Premium のメンバーのみです」となる音源が存在する
-- （例: oTX7jTo6RhQ / Happy Go Lucky! ドキドキ!プリキュア）。
-- 両者は status・contentDetails・regionRestriction・oEmbed のすべてが同一で、
-- Data API 上では区別できないことを実測で確認済み。
-- 判別できるのは動画ページの playabilityStatus だけなので、取り込み時にそこを見て
-- 結果をこの列へ残し、サイト生成時に「押す前に分かる」形で出し分ける。
--
-- 取り得る値（マスタテーブルは持たず、アプリ側で解釈する）:
--     OK             誰でも再生できる
--     PREMIUM_ONLY   YouTube Music Premium 会員のみ再生できる
--     UNPLAYABLE     上記以外の理由で再生できない（削除・地域制限など）
--     NULL           未確認
--
-- 冪等性: 列の存在確認をしてから ALTER を発行する。既存マイグレーションのスタイルを踏襲。
-- =====================================================================

START TRANSACTION;

DROP PROCEDURE IF EXISTS _v1121_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1121_add_col_if_missing(
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

CALL _v1121_add_col_if_missing('tracks', 'youtube_playability', "VARCHAR(24) DEFAULT NULL COMMENT '配信音源の再生可否：OK / PREMIUM_ONLY / UNPLAYABLE。NULL は未確認。embeddable とは別軸で、埋め込み可でも Premium 限定のことがある' AFTER `youtube_embeddable`");

DROP PROCEDURE _v1121_add_col_if_missing;

COMMIT;
