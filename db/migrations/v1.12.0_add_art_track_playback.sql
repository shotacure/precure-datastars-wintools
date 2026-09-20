-- =====================================================================
-- v1.12.0_add_art_track_playback.sql
--
-- 配信音源（YouTube アートトラック）再生機能のための列を追加する。
-- アートトラックは、レーベルが正規に配信した音源へ YouTube がジャケット画像を
-- 付けて自動生成する公式動画（`<アーティスト名> - Topic` チャンネル）。
-- アルバムは `OLAK5uy_` で始まる ID の自動生成プレイリストになる。
--
--   products + 3 列:
--     youtube_art_track_playlist_id  アルバムのプレイリスト ID（取り込みの種）
--     youtube_art_track_status       取り込み状態（sweeper の中断・再開用）
--     youtube_art_track_checked_at   取り込み・照合の最終実行時刻
--
--   tracks + 3 列:
--     youtube_art_track_id           トラックに対応する動画 ID（再生時はこれを使う）
--     youtube_embeddable             埋め込み可否（videos.list の status.embeddable）
--     youtube_checked_at             埋め込み可否の最終確認時刻
--
-- プレイリスト ID は取り込みの種としてのみ使い、展開結果である
-- tracks.youtube_art_track_id を実行時の正とする。レンダリング時に
-- 「プレイリストの N 番目 = ディスク M 枚目の track X」を解決する設計にすると、
-- 複数枚組のフラット化の有無・特典ディスク混在・将来の曲追加で破綻するため。
--
-- youtube_art_track_status が取る値（マスタテーブルは持たず、
-- episode_theme_songs.usage_actuality と同じくアプリ側で解釈する）:
--     MATCHED    プレイリストを特定し、全トラックの照合が一致した
--     AMBIGUOUS  候補は見つかったが照合が部分一致。人の確認待ち
--     NOT_FOUND  候補が見つからなかった（配信されていない等）
--     MANUAL     人がプレイリスト ID を直接入力した
--     NULL       未処理
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
DROP PROCEDURE IF EXISTS _v1120_add_col_if_missing;
DELIMITER $$
CREATE PROCEDURE _v1120_add_col_if_missing(
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
-- products + 3 列
-- プレイリスト ID は `OLAK5uy_` + 33 文字で 41 文字。余裕を見て 64。
-- ---------------------------------------------------------------------
CALL _v1120_add_col_if_missing('products', 'youtube_art_track_playlist_id', "VARCHAR(64) DEFAULT NULL COMMENT 'YouTube のアルバム自動生成プレイリスト ID（OLAK5uy_...）。取り込みの種であり、再生時は tracks.youtube_art_track_id を使う' AFTER `official_url`");
CALL _v1120_add_col_if_missing('products', 'youtube_art_track_status',      "VARCHAR(16) DEFAULT NULL COMMENT 'アートトラック取り込み状態：MATCHED / AMBIGUOUS / NOT_FOUND / MANUAL。NULL は未処理' AFTER `youtube_art_track_playlist_id`");
CALL _v1120_add_col_if_missing('products', 'youtube_art_track_checked_at',  "DATETIME DEFAULT NULL COMMENT 'アートトラック取り込み・照合の最終実行時刻' AFTER `youtube_art_track_status`");

-- ---------------------------------------------------------------------
-- tracks + 3 列
-- 動画 ID は 11 文字固定だが、将来の仕様変更に備えて 16。
-- youtube_embeddable は NULL を「未確認」として使うため NOT NULL にしない
-- （既存の is_data_track 等の TINYINT(1) NOT NULL とは意味づけが異なる）。
-- ---------------------------------------------------------------------
CALL _v1120_add_col_if_missing('tracks', 'youtube_art_track_id', "VARCHAR(16) DEFAULT NULL COMMENT 'このトラックに対応する YouTube アートトラックの動画 ID。再生時はこの値を使う' AFTER `notes`");
CALL _v1120_add_col_if_missing('tracks', 'youtube_embeddable',   "TINYINT(1) DEFAULT NULL COMMENT '埋め込み再生可否（videos.list の status.embeddable）。NULL は未確認で、再生ボタンを出さない' AFTER `youtube_art_track_id`");
CALL _v1120_add_col_if_missing('tracks', 'youtube_checked_at',   "DATETIME DEFAULT NULL COMMENT '埋め込み可否の最終確認時刻。配信移管でアートトラックが消えるため定期的に再確認する' AFTER `youtube_embeddable`");

DROP PROCEDURE _v1120_add_col_if_missing;

COMMIT;
