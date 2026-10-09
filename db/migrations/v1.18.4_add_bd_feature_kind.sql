-- =====================================================================
-- v1.18.4_add_bd_feature_kind.sql
--
-- 映画など作品単位（series_kinds.credit_attach_to = 'SERIES'）の Blu-ray を当てるために、
-- bd_playlists.playlist_kind と bd_chapters.chapter_kind に FEATURE（本編。作品そのもの）を足す。
--
-- 作品単位の盤は episodes / episode_parts の行を持たないので、話・パートでは当てられない。
-- 代わりに bd_discs.series_id に作品を入れ、本編のプレイリストを FEATURE にし（そのチャプターも FEATURE）、
-- 話・パートの列は NULL のままにする。
--
-- 冪等性: MODIFY COLUMN は同じ定義で何度流しても変わらない。既存の値はそのまま残る。
-- =====================================================================

START TRANSACTION;

ALTER TABLE `bd_playlists`
  MODIFY COLUMN `playlist_kind` enum('EPISODE','PLAY_ALL','FEATURE','BONUS','MENU','OTHER') DEFAULT NULL
    COMMENT 'プレイリストの種別（EPISODE=本編 1 話、PLAY_ALL=全話連続、FEATURE=作品単位の本編〔映画など〕、BONUS=特典、MENU、OTHER。NULL=未判定）';

ALTER TABLE `bd_chapters`
  MODIFY COLUMN `chapter_kind` enum('EPISODE_PART','FEATURE','BLANK','BONUS','OTHER') DEFAULT NULL
    COMMENT 'チャプターの種別（EPISODE_PART=話のパート、FEATURE=作品単位の本編〔映画など〕、BLANK=余白、BONUS、OTHER。NULL=未判定）';

COMMIT;
